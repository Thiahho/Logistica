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
        CancellationToken ct = default)
    {
        if (peajes < 0)
            throw new InvalidOperationException("Peajes no puede ser negativo.");

        var precioBase = await db.Database
            .SqlQuery<decimal?>($"select tarifa_vigente({clienteId}, {zonaId}, {fecha}) as \"Value\"")
            .SingleAsync(ct);

        if (precioBase is null)
            throw new InvalidOperationException($"No hay tarifa vigente para la zona {zonaId}.");

        var factores = opciones.Value;
        var recargoUrgencia = urgente ? precioBase.Value * factores.FactorUrgencia : 0m;
        var descuento = descuentoRuta ? precioBase.Value * factores.FactorDescuentoRuta : 0m;
        var total = precioBase.Value + recargoUrgencia - descuento + peajes;

        return new DesglosePrecio(precioBase.Value, recargoUrgencia, descuento, peajes, total);
    }
}
