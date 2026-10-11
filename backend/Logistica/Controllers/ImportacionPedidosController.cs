using Logistica.Auth;
using Logistica.Dominio;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Logistica.Controllers;

/// <summary>
/// B11 (Anexo I §5, E4): importación masiva de pedidos desde una planilla Excel, para un cliente
/// elegido en el back-office. La pantalla lee el archivo (previsualización, sin escribir nada) y
/// después manda las filas a importar en tandas chicas: cada dirección nueva consulta al geocoder, y
/// un solo request con cientos de filas no terminaría antes del timeout del proxy.
/// </summary>
[ApiController]
[Route("api/pedidos/importar")]
[Authorize(Policy = "BackOffice")]
public class ImportacionPedidosController(ImportacionPedidosService importacion) : ControllerBase
{
    /// <summary>Filas por tanda de importación: con todas las direcciones nuevas, unos 12 segundos.</summary>
    public const int MaxFilasPorTanda = 10;

    public record ImportarRequest(int ClienteId, List<FilaPlanilla> Filas);

    [HttpGet("plantilla")]
    public IActionResult Plantilla() =>
        File(PlanillaPedidos.Plantilla(Reloj.HoyLocal().AddDays(2)), PlanillaPedidos.TipoContenido, "plantilla_pedidos.xlsx");

    [HttpPost("leer")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    public async Task<IActionResult> Leer([FromForm] int clienteId, IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0) return BadRequest("Elegí una planilla Excel (.xlsx).");
        if (await importacion.ErrorDeClienteAsync(clienteId, ct) is { } errorCliente) return Conflict(errorCliente);

        using var copia = new MemoryStream();
        await archivo.CopyToAsync(copia, ct);
        copia.Position = 0;
        var lectura = PlanillaPedidos.Leer(copia);
        if (lectura.Error is not null) return BadRequest(lectura.Error);

        return Ok(await importacion.PrevisualizarAsync(clienteId, lectura.Filas, ct));
    }

    [HttpPost]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Importar(ImportarRequest req, CancellationToken ct)
    {
        if (req.Filas is null || req.Filas.Count == 0) return BadRequest("No hay filas para importar.");
        if (req.Filas.Count > MaxFilasPorTanda)
            return BadRequest($"Se importan hasta {MaxFilasPorTanda} filas por tanda.");
        if (await importacion.ErrorDeClienteAsync(req.ClienteId, ct) is { } errorCliente) return Conflict(errorCliente);

        return Ok(await importacion.ImportarAsync(req.ClienteId, req.Filas, User.UsuarioId(), ct));
    }
}
