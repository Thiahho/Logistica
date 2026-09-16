using Logistica.Datos;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

public record DesglosePrecio(
    decimal PrecioBase,
    decimal RecargoKm,
    decimal RecargoUrgencia,
    decimal DescuentoRuta,
    decimal Peajes,
    decimal Total,
    decimal? KmCobrados,
    string? KmFuente);

/// <summary>
/// construccion_v1.md §6. Resuelve precio_base con la función tarifa_vigente ya presente en la
/// base (Migrations/20260828170859_ReglasDeBaseDeDatos.cs) en vez de reimplementar en C# la
/// resolución cliente→lista general: sería lógica de negocio duplicada (regla §3.3).
/// </summary>
public class PrecioService(LogisticaDbContext db, IOptions<OpcionesPrecio> opciones)
{
    public async Task<DesglosePrecio> CotizarAsync(
        int clienteId,
        int zonaId,
        DateOnly fecha,
        bool urgente,
        decimal peajes,
        bool descuentoRuta,
        string tipoVehiculo,
        decimal? precioManual = null,
        DistanciaResuelta? distancia = null,
        CancellationToken ct = default)
    {
        if (peajes < 0)
            throw new InvalidOperationException("Peajes no puede ser negativo.");

        // B9 (Anexo I §4, "+40 km → Cotización"): precio_manual reemplaza solo el origen de
        // precio_base cuando la zona no tiene tarifa cargada — recargo/descuento/peajes se
        // siguen aplicando encima igual, la fórmula de construccion_v1.md §6 no cambia.
        var precioBase = precioManual ?? await db.Database
            .SqlQuery<decimal?>($"select tarifa_vigente({clienteId}, {zonaId}, {fecha}, {tipoVehiculo}) as \"Value\"")
            .SingleAsync(ct);

        if (precioBase is null)
            throw new InvalidOperationException(
                $"No hay tarifa vigente para la zona {zonaId} en {tipoVehiculo}. Cargá la tarifa en /tarifas o fijá un precio manual en el pedido.");

        var factores = opciones.Value;
        // Anexo I §10.2-N lectura (ii): recargo proporcional al kilometraje recorrido, encima del
        // precio de zona vigente — no lo reemplaza. TramosKm vacío o distancia null (sin
        // coordenadas utilizables) => recargo_km = 0, fórmula idéntica a la de hoy.
        var recargoKm = distancia is not null
            ? precioBase.Value * FactorKm(distancia.Km, factores.TramosKm)
            : 0m;
        var recargoUrgencia = urgente ? precioBase.Value * factores.FactorUrgencia : 0m;
        var descuento = descuentoRuta ? precioBase.Value * factores.FactorDescuentoRuta : 0m;
        var total = precioBase.Value + recargoKm + recargoUrgencia - descuento + peajes;

        return new DesglosePrecio(
            precioBase.Value, recargoKm, recargoUrgencia, descuento, peajes, total,
            distancia?.Km, distancia?.Fuente);
    }

    /// <summary>Último tramo cuyo DesdeKm &lt;= km — semiabierto [DesdeKm, siguiente), mismo
    /// criterio que Zona.KmDesde/KmHasta. Pura y estática: testeable sin base, sin tirar nunca
    /// (tramos vacíos o km por debajo del primer tramo => 0).</summary>
    private static decimal FactorKm(decimal km, TramoKm[] tramos)
    {
        var vigente = tramos
            .Where(t => t.DesdeKm <= km)
            .OrderByDescending(t => t.DesdeKm)
            .FirstOrDefault();
        return vigente?.Porcentaje ?? 0m;
    }
}
