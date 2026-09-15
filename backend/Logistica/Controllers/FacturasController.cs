using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// E1 (Anexo I §5, B1). Listado y detalle de facturas, y el cierre de ciclo manual (sin
/// scheduler en el proyecto — construccion_v1.md §1). Ninguna acción necesita público más amplio
/// que Administracion, así que el atributo de clase es correcto acá (construccion_v1.md §3
/// regla 8, a diferencia de ClientesController/UsuariosController).
/// </summary>
[ApiController]
[Route("api/facturas")]
[Authorize(Policy = "Administracion")]
public class FacturasController(LogisticaDbContext db, CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    public record FacturaResumen(
        long Id, int ClienteId, string ClienteRazonSocial, string Ciclo,
        DateOnly PeriodoDesde, DateOnly PeriodoHasta, DateOnly FechaEmision, DateOnly FechaVencimiento,
        decimal Total, decimal Pagado, decimal Saldo, string Estado);

    public record FacturaItemResumen(long Id, long? PedidoId, string Tipo, string Descripcion, decimal? Monto, string Estado);

    public record FacturaDetalle(
        long Id, int ClienteId, string ClienteRazonSocial, string Ciclo,
        DateOnly PeriodoDesde, DateOnly PeriodoHasta, DateOnly FechaEmision, DateOnly FechaVencimiento,
        decimal Total, decimal Pagado, decimal Saldo, string Estado, List<FacturaItemResumen> Items);

    public record CierreRequest(DateOnly? Fecha, int? ClienteId);

    /// <summary>pendiente | parcial | pagada | vencida — derivado de v_facturas_saldo, nunca
    /// persistido. `saldo` prioriza "vencida" sobre "parcial": una factura parcialmente pagada
    /// y ya vencida es, ante todo, deuda vencida (lo que gatea el corte, §10.2-L).</summary>
    private static string EstadoDe(decimal saldo, decimal total, DateOnly vencimiento, DateOnly hoy)
    {
        if (saldo <= 0) return "pagada";
        if (vencimiento < hoy) return "vencida";
        return saldo < total ? "parcial" : "pendiente";
    }

    /// <summary>
    /// Paginado y filtrable, mismo patrón que /api/pedidos. Antes traía TODA v_facturas_saldo (join
    /// clientes) a memoria y resolvía estado/orden/página ahí — asumido barato por volumen bajo
    /// (P6: dos clientes cerrados hoy), pero es justo lo que deja de ser cierto al crecer. El
    /// "estado" (pendiente/parcial/pagada/vencida) es una expresión de f.Saldo/f.Total/
    /// f.FechaVencimiento comparados contra `hoy` — EF Core la traduce a un CASE WHEN, así que
    /// filtro, orden y Skip/Take pasan a resolverse en SQL, y solo la página pedida viaja.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? clienteId,
        [FromQuery] string? estado,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta,
        [FromQuery] string? q,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanioPagina,
        CancellationToken ct)
    {
        var hoy = Reloj.HoyLocal();

        var query =
            from f in db.Set<FacturaSaldo>().AsNoTracking()
            join c in db.Clientes.AsNoTracking() on f.ClienteId equals c.Id
            select new
            {
                f.Id, f.ClienteId, ClienteRazonSocial = c.RazonSocial, f.Ciclo,
                f.PeriodoDesde, f.PeriodoHasta, f.FechaEmision, f.FechaVencimiento,
                f.Total, f.Pagado, f.Saldo,
                // Mismo criterio que EstadoDe (Detalle usa la versión en memoria, una sola fila —
                // no hace falta duplicar acá, se mantiene igual a propósito para que ambas no se
                // desincronicen): saldo <= 0 gana pagada; si no, vencimiento < hoy gana vencida.
                Estado = f.Saldo <= 0 ? "pagada"
                    : f.FechaVencimiento < hoy ? "vencida"
                    : f.Saldo < f.Total ? "parcial" : "pendiente",
            };

        if (clienteId is not null) query = query.Where(x => x.ClienteId == clienteId.Value);
        if (fechaDesde is not null) query = query.Where(x => x.FechaEmision >= fechaDesde.Value);
        if (fechaHasta is not null) query = query.Where(x => x.FechaEmision <= fechaHasta.Value);
        if (!string.IsNullOrWhiteSpace(estado)) query = query.Where(x => x.Estado == estado);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var texto = q.Trim();
            query = long.TryParse(texto, out var idBuscado)
                ? query.Where(x => x.Id == idBuscado || x.ClienteRazonSocial.ToLower().Contains(texto.ToLower()))
                : query.Where(x => x.ClienteRazonSocial.ToLower().Contains(texto.ToLower()));
        }

        var total = await query.CountAsync(ct);

        query = orden switch
        {
            "fecha" => query.OrderBy(x => x.FechaEmision).ThenBy(x => x.Id),
            "-fecha" => query.OrderByDescending(x => x.FechaEmision).ThenByDescending(x => x.Id),
            "vencimiento" => query.OrderBy(x => x.FechaVencimiento).ThenBy(x => x.Id),
            "-vencimiento" => query.OrderByDescending(x => x.FechaVencimiento).ThenByDescending(x => x.Id),
            "total" => query.OrderBy(x => x.Total).ThenByDescending(x => x.Id),
            "-total" => query.OrderByDescending(x => x.Total).ThenByDescending(x => x.Id),
            "cliente" => query.OrderBy(x => x.ClienteRazonSocial).ThenByDescending(x => x.Id),
            _ => query.OrderByDescending(x => x.FechaEmision).ThenByDescending(x => x.Id),
        };

        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? pagina.Value : 1;
            query = query.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value);
        }

        var lista = await query
            .Select(x => new FacturaResumen(
                x.Id, x.ClienteId, x.ClienteRazonSocial, x.Ciclo,
                x.PeriodoDesde, x.PeriodoHasta, x.FechaEmision, x.FechaVencimiento,
                x.Total, x.Pagado, x.Saldo, x.Estado))
            .ToListAsync(ct);

        return Ok(new ListaPaginada<FacturaResumen>(lista, total));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var saldo = await db.Set<FacturaSaldo>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
        if (saldo is null) return NotFound();

        var clienteRazonSocial = await db.Clientes.Where(c => c.Id == saldo.ClienteId)
            .Select(c => c.RazonSocial).SingleAsync(ct);

        var items = await db.FacturaItems.AsNoTracking()
            .Where(i => i.FacturaId == id)
            .OrderBy(i => i.CreadoEn)
            .Select(i => new FacturaItemResumen(i.Id, i.PedidoId, i.Tipo, i.Descripcion, i.Monto, i.Estado))
            .ToListAsync(ct);

        var hoy = Reloj.HoyLocal();
        return Ok(new FacturaDetalle(
            saldo.Id, saldo.ClienteId, clienteRazonSocial, saldo.Ciclo,
            saldo.PeriodoDesde, saldo.PeriodoHasta, saldo.FechaEmision, saldo.FechaVencimiento,
            saldo.Total, saldo.Pagado, saldo.Saldo,
            EstadoDe(saldo.Saldo, saldo.Total, saldo.FechaVencimiento, hoy), items));
    }

    /// <summary>Dry-run del cierre — imprescindible: una factura emitida es inmutable, no hay
    /// deshacer (trg_facturas_inmutable). Mismo cómputo que el POST, revertido al final.</summary>
    [HttpGet("cierre/previsualizacion")]
    public async Task<IActionResult> PrevisualizarCierre(
        [FromQuery] DateOnly? fecha, [FromQuery] int? clienteId, CancellationToken ct)
    {
        var resultado = await cuentaCorriente.CerrarCiclosAsync(
            fecha ?? Reloj.HoyLocal(), clienteId, previsualizar: true, User.UsuarioId(), ct);
        return Ok(resultado);
    }

    [HttpPost("cierre")]
    public async Task<IActionResult> Cierre(CierreRequest req, CancellationToken ct)
    {
        var resultado = await cuentaCorriente.CerrarCiclosAsync(
            req.Fecha ?? Reloj.HoyLocal(), req.ClienteId, previsualizar: false, User.UsuarioId(), ct);
        return Ok(resultado);
    }
}
