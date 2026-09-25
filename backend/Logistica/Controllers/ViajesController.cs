using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Opciones;
using Logistica.Servicios;
using Logistica.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Controllers;

/// <summary>
/// Viajes desde el BackOffice: Operación carga un envío de varias paradas para un cliente, con su
/// ruta propuesta (ViajeService). Mismas validaciones que PedidosController.Crear (cliente activo,
/// fecha de −30 a +365 días, corte por deuda) y sin precio al cargar: como cualquier pedido interno,
/// se cotiza al cerrar la planificación, con el vehículo real de la ruta (acta changelog 3.11).
/// </summary>
[ApiController]
[Route("api/viajes")]
[Authorize(Policy = "BackOffice")]
public class ViajesController(
    LogisticaDbContext db, ViajeService viajes, CuentaCorrienteService cuentaCorriente, IOptions<OpcionesPortal> opcionesPortal)
    : ControllerBase
{
    public record ViajeBackOfficeRequest(
        int ClienteId, DateOnly FechaEntrega, bool Urgente, string? Observaciones, List<ParadaViajeEntrada> Paradas);

    public record ViajeDeRuta(long Id, int ClienteId, string ClienteRazonSocial);

    private async Task<IActionResult?> ValidarAsync(ViajeBackOfficeRequest req, int minimoParadas, CancellationToken ct)
    {
        if (ViajeService.ValidarParadas(req.Paradas, opcionesPortal.Value.MaxParadasViaje, minimoParadas) is { } error)
            return BadRequest(error);

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == req.ClienteId)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return BadRequest("El cliente no existe.");
        if (!cliente.Activo) return Conflict($"{cliente.RazonSocial} está inactivo; no admite pedidos nuevos.");

        var hoy = Reloj.HoyLocal();
        if (ValidacionFechas.FechaEntrega(req.FechaEntrega, hoy, diasAtras: 30, diasAdelante: 365) is { } errorFecha)
            return BadRequest(errorFecha);

        var suspendido = cliente.CorteSuspendidoHasta is { } h && h >= hoy;
        if (!suspendido && await cuentaCorriente.DeudaVencidaAsync(req.ClienteId, hoy, ct) is > 0 and var deuda)
            return Conflict(
                $"Servicio cortado por deuda vencida: {cliente.RazonSocial} adeuda ${deuda:N2} de comprobantes " +
                "vencidos (§10.2-L1). Se rehabilita solo al pagar el total (§10.2-L5) o con un plan de cuotas.");
        return null;
    }

    [HttpPost("previsualizar")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Previsualizar(ViajeBackOfficeRequest req, CancellationToken ct)
    {
        if (await ValidarAsync(req, minimoParadas: 1, ct) is { } rechazo) return rechazo;
        var (previa, error) = await viajes.PrevisualizarAsync(req.Paradas, ct);
        return previa is null ? BadRequest(error) : Ok(previa);
    }

    [HttpPost]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Crear(ViajeBackOfficeRequest req, CancellationToken ct)
    {
        if (await ValidarAsync(req, minimoParadas: 2, ct) is { } rechazo) return rechazo;

        var (previa, _) = await viajes.PrevisualizarAsync(req.Paradas, ct);
        var (creado, error) = await viajes.CrearAsync(
            new NuevoViaje(req.ClienteId, req.FechaEntrega, req.Urgente, TipoVehiculo: null, req.Observaciones,
                ClienteUsuarioId: null, UsuarioId: User.UsuarioId()),
            req.Paradas, req.Paradas.Select(_ => (DesglosePrecio?)null).ToList(), previa?.Km, "interno", ct);
        return creado is null ? BadRequest(error) : Ok(creado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? clienteId, [FromQuery] int? pagina, [FromQuery] int? tamanioPagina, CancellationToken ct) =>
        Ok(await viajes.ListarAsync(clienteId, pagina, tamanioPagina, ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var detalle = await viajes.DetalleAsync(id, clienteId: null, conPrecios: true, ct);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    /// <summary>Si la ruta es la propuesta de un viaje, cuál (para el aviso en el armado y el detalle de
    /// la ruta). 204 si es una ruta común.</summary>
    [HttpGet("de-ruta/{rutaId:long}")]
    public async Task<IActionResult> DeRuta(long rutaId, CancellationToken ct)
    {
        var viaje = await db.Viajes.AsNoTracking()
            .Where(v => v.RutaId == rutaId)
            .Select(v => new ViajeDeRuta(v.Id, v.ClienteId, v.Cliente.RazonSocial))
            .FirstOrDefaultAsync(ct);
        return viaje is null ? NoContent() : Ok(viaje);
    }

    [HttpPost("{id:long}/cancelar")]
    public async Task<IActionResult> Cancelar(long id, CancelarViajeRequest req, CancellationToken ct)
    {
        var clienteId = await db.Viajes.Where(v => v.Id == id).Select(v => (int?)v.ClienteId).SingleOrDefaultAsync(ct);
        if (clienteId is null) return NotFound();

        var motivo = string.IsNullOrWhiteSpace(req.Motivo) ? null : req.Motivo.Trim();
        var error = await viajes.CancelarAsync(id, clienteId.Value, User.UsuarioId(),
            $"Viaje #{id} cancelado{(motivo is null ? "" : $": {motivo}")}", ct);
        return error is null ? NoContent() : Conflict(error);
    }

    public record CancelarViajeRequest(string? Motivo);
}
