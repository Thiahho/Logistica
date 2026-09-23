using Logistica.Servicios;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Logistica.Controllers;

[ApiController]
[Route("api/recorrido")]
[Authorize(Policy = "Recorrido")]
public class RecorridoController(RuteoService ruteo) : ControllerBase
{
    public record TrazarRequest(List<PuntoRuta> Puntos);

    /// <summary>Traza el recorrido real por calles entre los puntos, en orden (armar ruta y la
    /// guía del repartidor, acta changelog 3.4). Cacheado en RuteoService; nunca tira — un OSRM
    /// caído devuelve 204 y el mapa cae a línea recta entre los puntos.</summary>
    [HttpPost]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Trazar(TrazarRequest req, CancellationToken ct)
    {
        var recorrido = await ruteo.TrazarAsync(req.Puntos, ct);
        return recorrido is null ? NoContent() : Ok(recorrido);
    }
}
