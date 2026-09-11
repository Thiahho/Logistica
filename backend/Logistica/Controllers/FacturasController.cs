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
    /// Paginado y filtrable, mismo patrón que /api/pedidos. Volumen bajo (P6: dos clientes
    /// cerrados hoy) — se resuelve `estado`/orden/página en memoria porque "estado" es derivado
    /// (v_facturas_saldo) y no es una columna real que SQL pueda filtrar ni paginar sola.
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
        var query =
            from f in db.Set<FacturaSaldo>().AsNoTracking()
            join c in db.Clientes.AsNoTracking() on f.ClienteId equals c.Id
            select new { Saldo = f, ClienteRazonSocial = c.RazonSocial };

        if (clienteId is not null) query = query.Where(x => x.Saldo.ClienteId == clienteId.Value);
        if (fechaDesde is not null) query = query.Where(x => x.Saldo.FechaEmision >= fechaDesde.Value);
        if (fechaHasta is not null) query = query.Where(x => x.Saldo.FechaEmision <= fechaHasta.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var texto = q.Trim();
            query = long.TryParse(texto, out var idBuscado)
                ? query.Where(x => x.Saldo.Id == idBuscado || x.ClienteRazonSocial.ToLower().Contains(texto.ToLower()))
                : query.Where(x => x.ClienteRazonSocial.ToLower().Contains(texto.ToLower()));
        }

        var filas = await query.ToListAsync(ct);
        var hoy = Reloj.HoyLocal();

        var resumenes = filas.Select(x => new FacturaResumen(
            x.Saldo.Id, x.Saldo.ClienteId, x.ClienteRazonSocial, x.Saldo.Ciclo,
            x.Saldo.PeriodoDesde, x.Saldo.PeriodoHasta, x.Saldo.FechaEmision, x.Saldo.FechaVencimiento,
            x.Saldo.Total, x.Saldo.Pagado, x.Saldo.Saldo,
            EstadoDe(x.Saldo.Saldo, x.Saldo.Total, x.Saldo.FechaVencimiento, hoy)));

        if (!string.IsNullOrWhiteSpace(estado))
            resumenes = resumenes.Where(r => r.Estado == estado);

        var lista = resumenes.ToList();
        var total = lista.Count;

        lista = orden switch
        {
            "fecha" => lista.OrderBy(r => r.FechaEmision).ThenBy(r => r.Id).ToList(),
            "-fecha" => lista.OrderByDescending(r => r.FechaEmision).ThenByDescending(r => r.Id).ToList(),
            "vencimiento" => lista.OrderBy(r => r.FechaVencimiento).ThenBy(r => r.Id).ToList(),
            "-vencimiento" => lista.OrderByDescending(r => r.FechaVencimiento).ThenByDescending(r => r.Id).ToList(),
            "total" => lista.OrderBy(r => r.Total).ThenByDescending(r => r.Id).ToList(),
            "-total" => lista.OrderByDescending(r => r.Total).ThenByDescending(r => r.Id).ToList(),
            "cliente" => lista.OrderBy(r => r.ClienteRazonSocial).ThenByDescending(r => r.Id).ToList(),
            _ => lista.OrderByDescending(r => r.FechaEmision).ThenByDescending(r => r.Id).ToList(),
        };

        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? pagina.Value : 1;
            lista = lista.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value).ToList();
        }

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
