using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Desglose del pago al repartidor por una ruta. PctExito sobre entregas + fallidas imputables.</summary>
public record DesgloseLiquidacion(
    string TipoVehiculo, int Entregas, int FallidasImputables, int FallidasNoImputables, decimal PctExito,
    decimal PagoPorEntrega, decimal PagoEntregas, decimal PctMinimoExitosas, decimal Bono, decimal Total);

/// <summary>
/// B4 — pago al repartidor calculado al cerrar la ruta (acta RF-41, changelog 4.21;
/// diseño_e2_rangos_liquidacion.md §2.2). Entregas × valor según el tipo de vehículo, más el bono si
/// la ruta cumple el mínimo de entregas exitosas. Se paga por parada, no por pedido: una parada
/// consolidada (RF-14) es un viaje. Las fallidas que no dependen del repartidor (motivo fuera de
/// MotivosImputables) y las paradas canceladas no entran al cálculo.
/// </summary>
public class LiquidacionService(LogisticaDbContext db)
{
    /// <summary>Cálculo puro, sin base. Sin ninguna parada que cuente (todas excusadas o canceladas),
    /// el % de éxito es 100: al repartidor no se le descuenta lo que no dependió de él. Pero el bono es
    /// por ruta completa (D6): sin ninguna entrega no hay ruta que premiar.</summary>
    public static DesgloseLiquidacion Calcular(
        ParametroLiquidacion parametros, int entregas, int fallidasImputables, int fallidasNoImputables)
    {
        var base_ = entregas + fallidasImputables;
        var pctExito = base_ == 0 ? 100m : Math.Round(entregas * 100m / base_, 2);
        var pagoEntregas = entregas * parametros.PagoPorEntrega;
        var bono = entregas > 0 && pctExito >= parametros.PctMinimoExitosas ? parametros.BonoRuta : 0m;
        return new DesgloseLiquidacion(
            parametros.TipoVehiculo, entregas, fallidasImputables, fallidasNoImputables, pctExito,
            parametros.PagoPorEntrega, pagoEntregas, parametros.PctMinimoExitosas, bono, pagoEntregas + bono);
    }

    /// <summary>Los parámetros vigentes a una fecha para un tipo de vehículo, o null si no hay.</summary>
    public Task<ParametroLiquidacion?> VigenteAsync(string tipoVehiculo, DateOnly fecha, CancellationToken ct) =>
        db.ParametrosLiquidacion.AsNoTracking()
            .Where(p => p.TipoVehiculo == tipoVehiculo && p.VigenteDesde <= fecha
                        && (p.VigenteHasta == null || p.VigenteHasta >= fecha))
            .OrderByDescending(p => p.VigenteDesde)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Lo que corresponde pagar por una ruta, o null si no se puede calcular: la ruta no tiene vehículo
    /// o no hay parámetros vigentes a su fecha para ese tipo. En ese caso el pago se tipea a mano.
    /// </summary>
    public async Task<DesgloseLiquidacion?> SugerirAsync(long rutaId, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking()
            .Where(r => r.Id == rutaId)
            .Select(r => new { r.Fecha, TipoVehiculo = r.Vehiculo != null ? r.Vehiculo.Tipo : null })
            .SingleOrDefaultAsync(ct);
        if (ruta?.TipoVehiculo is null) return null;

        var parametros = await VigenteAsync(ruta.TipoVehiculo, ruta.Fecha, ct);
        if (parametros is null) return null;

        var paradas = await db.RutaParadas.AsNoTracking()
            .Where(p => p.RutaId == rutaId && p.Tipo == "entrega" && (p.Estado == "completada" || p.Estado == "fallida"))
            .Select(p => new
            {
                p.Estado,
                // Una parada consolidada tiene una prueba por pedido, normalmente con el mismo motivo.
                // Con que uno sea imputable, la parada cuenta contra el repartidor.
                Imputable = db.PruebasEntrega.Any(pe => pe.ParadaId == p.Id && pe.Resultado == "fallido"
                                                        && parametros.MotivosImputables.Contains(pe.MotivoFallo!)),
            })
            .ToListAsync(ct);

        var entregas = paradas.Count(p => p.Estado == "completada");
        var fallidas = paradas.Where(p => p.Estado == "fallida").ToList();
        return Calcular(parametros, entregas, fallidas.Count(f => f.Imputable), fallidas.Count(f => !f.Imputable));
    }
}
