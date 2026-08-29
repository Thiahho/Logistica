using Logistica.Datos;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

[ApiController]
[Route("api/ubicaciones")]
[Authorize(Policy = "BackOffice")]
public class UbicacionesController(UbicacionService ubicaciones, LogisticaDbContext db) : ControllerBase
{
    public record ResolverRequest(string CalleNumero, int LocalidadId, string? Referencia);
    public record UbicacionResuelta(long Id, decimal? Lat, decimal? Lng, string? GeoConfianza);
    public record Coordenadas(decimal? Lat, decimal? Lng);

    /// <summary>Geocodifica al salir del campo dirección (construccion_v1.md §4.1). Resolver-o-crear:
    /// una dirección ya cargada no vuelve a pegarle al geocoder.</summary>
    [HttpPost]
    public async Task<IActionResult> Resolver(ResolverRequest req, CancellationToken ct)
    {
        var ubicacion = await ubicaciones.ResolverOCrearAsync(req.CalleNumero, req.LocalidadId, req.Referencia, ct);
        return Ok(new UbicacionResuelta(ubicacion.Id, ubicacion.Lat, ubicacion.Lng, ubicacion.GeoConfianza));
    }

    /// <summary>Punto de partida para el orden sugerido (lib/dominio/ruteo.ts), que corre en el
    /// navegador (construccion_v1.md §1). Mismo criterio de búsqueda que PedidosController.Crear.</summary>
    [HttpGet("deposito")]
    public async Task<IActionResult> Deposito(CancellationToken ct)
    {
        var deposito = await db.Ubicaciones.AsNoTracking().SingleAsync(u => u.Referencia == "deposito", ct);
        return Ok(new Coordenadas(deposito.Lat, deposito.Lng));
    }
}

[ApiController]
[Route("api/localidades")]
[Authorize(Policy = "BackOffice")]
public class LocalidadesController(LogisticaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Localidades.AsNoTracking().OrderBy(l => l.Nombre)
            .Select(l => new { l.Id, l.Nombre, l.Partido, l.ZonaId })
            .ToListAsync(ct));
}
