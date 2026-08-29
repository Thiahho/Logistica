using Logistica.Datos;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Lista general de precios (tarifas.cliente_id null — RF-09). Las tarifas por cliente se fijan
/// desde /api/clientes/{id}/tarifas/{zonaId} (ClientesController); ambos comparten TarifaService
/// para no duplicar la regla de vigencia.
/// </summary>
[ApiController]
[Route("api/tarifas")]
[Authorize(Policy = "Administracion")]
public class TarifasController(LogisticaDbContext db, TarifaService tarifas) : ControllerBase
{
    public record TarifaGeneral(int ZonaId, string ZonaCodigo, string ZonaNombre, decimal? Precio);
    public record FijarTarifaRequest(decimal? Precio);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var zonas = await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct);

        var resultado = new List<TarifaGeneral>();
        foreach (var zona in zonas)
        {
            var precio = await db.Database
                .SqlQuery<decimal?>($"select tarifa_vigente(null, {zona.Id}, {hoy}) as \"Value\"")
                .SingleAsync(ct);
            resultado.Add(new TarifaGeneral(zona.Id, zona.Codigo, zona.Nombre, precio));
        }

        return Ok(resultado);
    }

    [HttpPut("{zonaId:int}")]
    public async Task<IActionResult> Fijar(int zonaId, FijarTarifaRequest req, CancellationToken ct)
    {
        await tarifas.FijarAsync(clienteId: null, zonaId, req.Precio, ct);
        return NoContent();
    }
}
