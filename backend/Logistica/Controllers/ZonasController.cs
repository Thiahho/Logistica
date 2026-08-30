using Logistica.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

[ApiController]
[Route("api/zonas")]
[Authorize(Policy = "BackOffice")]
public class ZonasController(LogisticaDbContext db) : ControllerBase
{
    public record ActualizarKmRequest(int? KmDesde, int? KmHasta);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct));

    /// <summary>Rango de km de la zona, editable desde /tarifas junto al precio. Más estricto que
    /// la clase (Administracion sobre BackOffice): la combinación AND funciona como se espera
    /// (construccion_v1.md §3 regla 8, mismo caso que RutasController.Cerrar/Resultado) — las
    /// zonas son categorías fijas del negocio, no algo que operación deba poder tocar.</summary>
    [HttpPut("{id:int}/km")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ActualizarKm(int id, ActualizarKmRequest req, CancellationToken ct)
    {
        var zona = await db.Zonas.SingleOrDefaultAsync(z => z.Id == id, ct);
        if (zona is null) return NotFound();

        zona.KmDesde = req.KmDesde;
        zona.KmHasta = req.KmHasta;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
