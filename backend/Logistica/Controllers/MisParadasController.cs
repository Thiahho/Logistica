using Logistica.Auth;
using Logistica.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Consulta v_paradas_repartidor (construccion_v1.md §3 regla 4), nunca la tabla pedidos:
/// esa vista no tiene importes. Solo las paradas de rutas asignadas al repartidor autenticado.
/// </summary>
[ApiController]
[Route("api/mis-paradas")]
[Authorize(Policy = "Repartidor")]
public class MisParadasController(LogisticaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        // "en_curso": recién visible una vez cerrada la planificación (RF-17,
        // RutasController.CerrarPlanificacion). Antes de eso la ruta todavía se está armando.
        var paradas = await (
            from p in db.Set<ParadaRepartidor>()
            join r in db.Rutas on p.RutaId equals r.Id
            where r.RepartidorId == repartidorId && r.Estado == "en_curso"
            orderby p.Orden
            select p
        ).ToListAsync(ct);

        return Ok(paradas);
    }
}
