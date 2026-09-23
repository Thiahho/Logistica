using Logistica.Datos;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Descarga de la foto de una prueba de entrega. Sin [Authorize] a nivel de clase a propósito
/// (regla 8): si mañana hace falta una acción con público distinto de esta, la clase ya está
/// preparada. Nunca UseStaticFiles/wwwroot: las fotos tienen datos personales del destinatario
/// (RNF-09), no se sirven sin autenticación.
/// </summary>
[ApiController]
[Route("api/pruebas-entrega")]
public class PruebasEntregaController(LogisticaDbContext db, AlmacenamientoFotos almacenamiento) : ControllerBase
{
    /// <summary>El token va en el header Authorization, así que el frontend no puede usar
    /// &lt;img src&gt; directo — baja el blob con fetchConSesion y arma un object URL.</summary>
    [HttpGet("{id:long}/foto")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Foto(long id, CancellationToken ct)
    {
        var fotoPath = await db.PruebasEntrega.AsNoTracking()
            .Where(pe => pe.Id == id)
            .Select(pe => pe.FotoPath)
            .SingleOrDefaultAsync(ct);

        if (fotoPath is null) return NotFound();

        var stream = await almacenamiento.AbrirAsync(fotoPath, ct);
        if (stream is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        return File(stream, "image/jpeg");
    }
}
