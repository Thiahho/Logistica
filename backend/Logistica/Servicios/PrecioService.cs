using Logistica.Datos;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

public record DesglosePrecio(
    decimal PrecioBase,
    decimal RecargoUrgencia,
    decimal DescuentoRuta,
    decimal Peajes,
    decimal Total);

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
        var recargoUrgencia = urgente ? precioBase.Value * factores.FactorUrgencia : 0m;
        var descuento = descuentoRuta ? precioBase.Value * factores.FactorDescuentoRuta : 0m;
        var total = precioBase.Value + recargoUrgencia - descuento + peajes;

        return new DesglosePrecio(precioBase.Value, recargoUrgencia, descuento, peajes, total);
    }
}
