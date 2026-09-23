using System.ComponentModel.DataAnnotations;
using Logistica.Datos;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

[ApiController]
[Route("api/zonas")]
[Authorize(Policy = "BackOffice")]
public class ZonasController(LogisticaDbContext db, ZonaLocalidadService zonasLocalidad) : ControllerBase
{
    public record ActualizarKmRequest(
        [Range(0, int.MaxValue, ErrorMessage = "El km desde no puede ser negativo.")] int? KmDesde,
        [Range(0, int.MaxValue, ErrorMessage = "El km hasta no puede ser negativo.")] int? KmHasta);

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
        if (req.KmDesde is not null && req.KmHasta is not null && req.KmHasta <= req.KmDesde)
            return BadRequest("El km hasta debe ser mayor al km desde.");

        var zona = await db.Zonas.SingleOrDefaultAsync(z => z.Id == id, ct);
        if (zona is null) return NotFound();

        // Auditoría §7 (coherencia de km): un rango que se solapa con otra zona activa hace que
        // la sugerencia de zona por distancia (UbicacionesController.LocalidadesPendientes) la
        // resuelva el orden de iteración, no el negocio. El hueco entre zonas no se bloquea acá
        // — ver TarifasController.CalcularHuecos, que solo lo reporta como aviso.
        if (req.KmDesde is not null)
        {
            var otras = await db.Zonas.AsNoTracking()
                .Where(z => z.Id != id && z.Activa && z.KmDesde != null)
                .Select(z => new { z.Id, z.Codigo, z.KmDesde, z.KmHasta })
                .ToListAsync(ct);

            var solapada = otras.FirstOrDefault(z => Solapan(req.KmDesde, req.KmHasta, z.KmDesde, z.KmHasta));
            if (solapada is not null)
                return BadRequest($"El rango se solapa con la zona {solapada.Codigo} ({Describir(solapada.KmDesde, solapada.KmHasta)}).");
        }

        zona.KmDesde = req.KmDesde;
        zona.KmHasta = req.KmHasta;
        await db.SaveChangesAsync(ct);
        // Cambió un rango: las localidades automáticas se reasignan con el km ya guardado (sin red).
        await zonasLocalidad.RecalcularTodasAsync(soloZona: true, ct);
        return NoContent();
    }

    /// <summary>Solapamiento entre dos rangos semiabiertos [desde, hasta) — hasta null = sin
    /// límite superior. Mismo criterio semiabierto que UbicacionesController.LocalidadesPendientes:
    /// una distancia exacta en la frontera pertenece a la zona de arriba, no a las dos.</summary>
    private static bool Solapan(int? d1, int? h1, int? d2, int? h2) =>
        (h1 is null || d2 < h1) && (h2 is null || d1 < h2);

    private static string Describir(int? desde, int? hasta) => hasta is null ? $"{desde}+ km" : $"{desde}–{hasta} km";
}
