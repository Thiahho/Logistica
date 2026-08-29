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
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct));
}
