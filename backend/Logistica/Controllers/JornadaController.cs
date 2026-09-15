using Logistica.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Monitor del día en curso (jornada §9.3: "armado y cierre de ruta, ejecución en calle" es
/// función básica del ciclo diario). NO es el tablero de indicadores (B2/E3, Anexo I §5, sigue
/// fuera de alcance) — no calcula ninguna de sus 10 métricas operativas, no acumula ni compara
/// períodos: cuenta filas de `rutas`/`ruta_paradas` para UNA fecha. Mismo encuadre que el panel
/// de Cobranza (acta changelog 4.3), que declara la misma distinción. Cero tablas nuevas.
/// </summary>
[ApiController]
[Route("api/jornada")]
[Authorize(Policy = "BackOffice")]
public class JornadaController(LogisticaDbContext db) : ControllerBase
{
    public record ContadorRutas(int Planificadas, int EnCurso, int Cerradas, int Total);
    public record ContadorParadas(int Total, int Pendientes, int Completadas, int Fallidas);

    public record RutaDelDia(
        long Id, string Estado, string? VehiculoPatente,
        Guid? RepartidorId, string? RepartidorNombre,
        int Paradas, int Pendientes, int Completadas, int Fallidas);

    public record RepartidorDelDia(
        Guid RepartidorId, string Nombre, long RutaId, string RutaEstado,
        string? VehiculoPatente, int Paradas, int Completadas, int Fallidas, int Pendientes,
        DateTimeOffset? PrimeraLlegada, DateTimeOffset? UltimaActividad);

    public record ResumenJornada(
        DateOnly Fecha, ContadorRutas Rutas, ContadorParadas Paradas,
        List<RutaDelDia> RutasDelDia, List<RepartidorDelDia> Repartidores,
        DateTimeOffset GeneradoEn);

    /// <summary>Dos queries, no N+1: una trae las rutas de la fecha con vehículo/repartidor, otra
    /// agrupa ruta_paradas por ruta. La cantidad de rutas por día es de un dígito — el resto se
    /// compone en memoria.</summary>
    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen([FromQuery] DateOnly? fecha, CancellationToken ct)
    {
        var fechaValor = fecha ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.Fecha == fechaValor)
            .Include(r => r.Vehiculo)
            .Include(r => r.Repartidor)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var rutaIds = rutas.Select(r => r.Id).ToList();

        var paradasPorRuta = await db.RutaParadas.AsNoTracking()
            .Where(rp => rutaIds.Contains(rp.RutaId))
            .GroupBy(rp => rp.RutaId)
            .Select(g => new
            {
                RutaId = g.Key,
                Total = g.Count(),
                Pendientes = g.Count(p => p.Estado == "pendiente"),
                Completadas = g.Count(p => p.Estado == "completada"),
                Fallidas = g.Count(p => p.Estado == "fallida"),
                PrimeraLlegada = g.Min(p => p.LlegadaEn),
                UltimaActividad = g.Max(p => p.SalidaEn ?? p.LlegadaEn),
            })
            .ToDictionaryAsync(g => g.RutaId, ct);

        var rutasDelDia = rutas.Select(r =>
        {
            paradasPorRuta.TryGetValue(r.Id, out var p);
            return new RutaDelDia(
                r.Id, r.Estado, r.Vehiculo?.Patente, r.RepartidorId, r.Repartidor?.Nombre,
                p?.Total ?? 0, p?.Pendientes ?? 0, p?.Completadas ?? 0, p?.Fallidas ?? 0);
        }).ToList();

        var repartidores = rutas
            .Where(r => r.RepartidorId is not null)
            .Select(r =>
            {
                paradasPorRuta.TryGetValue(r.Id, out var p);
                return new RepartidorDelDia(
                    r.RepartidorId!.Value, r.Repartidor!.Nombre, r.Id, r.Estado, r.Vehiculo?.Patente,
                    p?.Total ?? 0, p?.Completadas ?? 0, p?.Fallidas ?? 0, p?.Pendientes ?? 0,
                    p?.PrimeraLlegada, p?.UltimaActividad);
            })
            .OrderBy(rd => rd.Nombre)
            .ToList();

        var contadorRutas = new ContadorRutas(
            rutas.Count(r => r.Estado == "planificada"),
            rutas.Count(r => r.Estado == "en_curso"),
            rutas.Count(r => r.Estado == "cerrada"),
            rutas.Count);

        var contadorParadas = new ContadorParadas(
            paradasPorRuta.Values.Sum(p => p.Total),
            paradasPorRuta.Values.Sum(p => p.Pendientes),
            paradasPorRuta.Values.Sum(p => p.Completadas),
            paradasPorRuta.Values.Sum(p => p.Fallidas));

        return Ok(new ResumenJornada(
            fechaValor, contadorRutas, contadorParadas, rutasDelDia, repartidores, DateTimeOffset.UtcNow));
    }
}
