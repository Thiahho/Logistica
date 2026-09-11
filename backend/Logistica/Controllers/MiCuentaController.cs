using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// E1: cuenta corriente para el rol 'cliente' — resuelve el cliente desde el claim de sesión,
/// nunca desde la URL, así que no puede filtrar por id ajeno. Controller propio, no una acción de
/// FacturasController (que es Administracion de clase) — mismo motivo que separa
/// MisParadasController del resto: construccion_v1.md §3 regla 8, `[Authorize]` de clase y de
/// acción se combinan con AND, no se reemplazan. Nunca expone color_pago/color_trato/color_oper
/// ni corte_suspendido_motivo (RF-33: los semáforos internos no se exponen al cliente).
/// </summary>
[ApiController]
[Route("api/mi-cuenta")]
[Authorize(Policy = "Cliente")]
public class MiCuentaController(LogisticaDbContext db, CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    public record FacturaPropia(
        long Id, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
        DateOnly FechaVencimiento, decimal Total, decimal Saldo, string Estado);

    public record CuentaPropia(
        decimal Saldo, decimal DeudaVencida, bool ServicioCortado,
        DateOnly? ProximoVencimiento, List<FacturaPropia> Facturas);

    private static string EstadoDe(decimal saldo, decimal total, DateOnly vencimiento, DateOnly hoy)
    {
        if (saldo <= 0) return "pagada";
        if (vencimiento < hoy) return "vencida";
        return saldo < total ? "parcial" : "pendiente";
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var hoy = Reloj.HoyLocal();
        var saldo = await cuentaCorriente.SaldoAsync(clienteId.Value, ct);
        var deudaVencida = await cuentaCorriente.DeudaVencidaAsync(clienteId.Value, hoy, ct);
        var servicioCortado = await cuentaCorriente.ServicioCortadoAsync(clienteId.Value, hoy, ct);

        var facturas = (await db.Set<FacturaSaldo>().AsNoTracking()
            .Where(f => f.ClienteId == clienteId.Value && f.Saldo > 0)
            .OrderBy(f => f.FechaVencimiento)
            .ToListAsync(ct))
            .Select(f => new FacturaPropia(
                f.Id, f.PeriodoDesde, f.PeriodoHasta, f.FechaVencimiento, f.Total, f.Saldo,
                EstadoDe(f.Saldo, f.Total, f.FechaVencimiento, hoy)))
            .ToList();

        var proximoVencimiento = facturas.Count > 0 ? facturas.Min(f => f.FechaVencimiento) : (DateOnly?)null;

        return Ok(new CuentaPropia(saldo, deudaVencida, servicioCortado, proximoVencimiento, facturas));
    }
}
