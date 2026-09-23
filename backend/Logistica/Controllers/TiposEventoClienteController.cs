using Logistica.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

[ApiController]
[Route("api/tipos-evento-cliente")]
[Authorize(Policy = "BackOffice")]
public class TiposEventoClienteController(LogisticaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.TiposEventoCliente.AsNoTracking()
            .OrderBy(t => t.Dimension).ThenBy(t => t.Codigo)
            .ToListAsync(ct));
}
