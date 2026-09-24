using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// B2 — tablero de indicadores, etapa E3 (acta RF-44, changelog 4.24; definiciones en
/// docs/diseño_b2_tablero.md). Solo lectura, recalculado en cada request: no guarda nada, no hay
/// scheduler. Solo Administración, porque trae márgenes (RNF-08).
///
/// Un bloque de consultas planas por grupo de indicadores y composición en memoria, mismo patrón que
/// RepartidoresController.Listar: los round-trips no crecen con la cantidad de rutas ni de pedidos.
/// El costo de una ruta NO se prorratea entre clientes o zonas: con esos filtros, los indicadores de
/// ruta vuelven null y la pantalla dice por qué.
/// </summary>
[ApiController]
[Route("api/tablero")]
[Authorize(Policy = "Administracion")]
public class TableroController(LogisticaDbContext db) : ControllerBase
{
    private const int MaxDiasRango = 366;
    private const int TopClientes = 10;

    public record PuntoDia(DateOnly Fecha, int Entregas, decimal? Margen);
    public record Monto(string Nombre, decimal Valor);

    public record Tablero(
        DateOnly Desde, DateOnly Hasta, bool IndicadoresDeRuta,
        int Entregas, decimal? EntregasPorDia,
        decimal? KmPorEntrega, decimal? MinutosPorEntrega,
        decimal Facturacion, List<Monto> FacturacionPorCliente, List<Monto> FacturacionPorRango,
        int Cancelados, decimal? PctCancelaciones,
        int RutasCerradas, decimal? CostoPorEntrega, decimal? CostoPorRuta,
        decimal? MargenTotal, decimal? MargenPorRuta,
        decimal? PctOcupacion,
        List<PuntoDia> PorDia);

    [HttpGet]
    public async Task<IActionResult> Obtener(
        [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta,
        [FromQuery] int? clienteId, [FromQuery] int? zonaId, [FromQuery] long? vehiculoId, [FromQuery] Guid? repartidorId,
        CancellationToken ct)
    {
        if (hasta < desde || hasta.DayNumber - desde.DayNumber > MaxDiasRango)
            return BadRequest($"El período debe ser válido y de hasta {MaxDiasRango} días.");

        var indicadoresDeRuta = clienteId is null && zonaId is null;

        // ── Pedidos del período (entregas, cancelaciones, entregas por día) ─────────────────────────
        var pedidos = db.Pedidos.AsNoTracking()
            .Where(p => p.FechaEntrega >= desde && p.FechaEntrega <= hasta && p.Estado != EstadoPedido.Borrador);
        if (clienteId is not null) pedidos = pedidos.Where(p => p.ClienteId == clienteId);
        if (zonaId is not null) pedidos = pedidos.Where(p => p.ZonaId == zonaId);
        if (vehiculoId is not null || repartidorId is not null)
            pedidos = pedidos.Where(p => db.ParadaPedidos.Any(pp => pp.PedidoId == p.Id
                && (vehiculoId == null || pp.Parada.Ruta.VehiculoId == vehiculoId)
                && (repartidorId == null || pp.Parada.Ruta.RepartidorId == repartidorId)));

        var porEstado = await pedidos
            .GroupBy(p => new { p.FechaEntrega, p.Estado })
            .Select(g => new { g.Key.FechaEntrega, g.Key.Estado, Cantidad = g.Count() })
            .ToListAsync(ct);

        var entregasPorFecha = porEstado.Where(x => x.Estado == EstadoPedido.Entregado)
            .GroupBy(x => x.FechaEntrega).ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));
        var entregas = entregasPorFecha.Values.Sum();
        var totalPedidos = porEstado.Sum(x => x.Cantidad);
        var cancelados = porEstado.Where(x => x.Estado == EstadoPedido.Cancelado).Sum(x => x.Cantidad);

        // ── Facturación (factura_items nacidos en el período, criterio de B7 y rangos) ─────────────
        var (desdeUtc, _) = Reloj.RangoLocalUtc(desde);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(hasta);
        var items = db.FacturaItems.AsNoTracking()
            .Where(i => i.Estado == "aprobado" && i.Monto != null && i.CreadoEn >= desdeUtc && i.CreadoEn < hastaUtc);
        if (clienteId is not null)
            items = items.Where(i => (i.Factura != null ? i.Factura.ClienteId : i.Pedido!.ClienteId) == clienteId);
        if (zonaId is not null) items = items.Where(i => i.Pedido != null && i.Pedido.ZonaId == zonaId);
        if (vehiculoId is not null || repartidorId is not null)
            items = items.Where(i => i.PedidoId != null && db.ParadaPedidos.Any(pp => pp.PedidoId == i.PedidoId
                && (vehiculoId == null || pp.Parada.Ruta.VehiculoId == vehiculoId)
                && (repartidorId == null || pp.Parada.Ruta.RepartidorId == repartidorId)));

        var facturadoPorCliente = await items
            .Select(i => new
            {
                ClienteId = i.Factura != null ? (int?)i.Factura.ClienteId : i.Pedido != null ? (int?)i.Pedido.ClienteId : null,
                i.Monto,
            })
            .Where(x => x.ClienteId != null)
            .GroupBy(x => x.ClienteId!.Value)
            .Select(g => new { ClienteId = g.Key, Total = g.Sum(x => x.Monto!.Value) })
            .ToListAsync(ct);

        var ids = facturadoPorCliente.Select(f => f.ClienteId).ToList();
        var clientes = await db.Clientes.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.RazonSocial, c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence })
            .ToDictionaryAsync(c => c.Id, ct);
        var rangos = await db.Rangos.AsNoTracking().OrderBy(r => r.Orden).ToListAsync(ct);
        var hoy = Reloj.HoyLocal();

        var ordenados = facturadoPorCliente.OrderByDescending(f => f.Total).ToList();
        var porCliente = ordenados.Take(TopClientes)
            .Select(f => new Monto(clientes[f.ClienteId].RazonSocial, f.Total)).ToList();
        if (ordenados.Count > TopClientes)
            porCliente.Add(new Monto("Otros", ordenados.Skip(TopClientes).Sum(f => f.Total)));

        var porRango = rangos
            .Select(r => new Monto(r.Nombre, facturadoPorCliente
                .Where(f => RangosCliente.Efectivo(clientes[f.ClienteId].RangoCalculado, clientes[f.ClienteId].RangoAjuste,
                    clientes[f.ClienteId].RangoAjusteVence, hoy, rangos) == r.Codigo)
                .Sum(f => f.Total)))
            .ToList();

        // ── Rutas del período (km, tiempo, costo, margen, ocupación) ────────────────────────────────
        decimal? kmPorEntrega = null, minutosPorEntrega = null, costoPorEntrega = null, costoPorRuta = null,
            margenTotal = null, margenPorRuta = null, pctOcupacion = null;
        var rutasCerradas = 0;
        var margenPorFecha = new Dictionary<DateOnly, decimal>();

        if (indicadoresDeRuta)
        {
            var rutas = db.Rutas.AsNoTracking().Where(r => r.Fecha >= desde && r.Fecha <= hasta);
            if (vehiculoId is not null) rutas = rutas.Where(r => r.VehiculoId == vehiculoId);
            if (repartidorId is not null) rutas = rutas.Where(r => r.RepartidorId == repartidorId);

            var filas = await rutas
                .Where(r => r.Estado == "cerrada" || r.Estado == "en_curso")
                .Select(r => new
                {
                    r.Id, r.Fecha, r.Estado, r.CapacidadParadas, r.KmInicial, r.KmFinal,
                    Costos = (r.CombustibleMonto ?? 0) + (r.PeajesMonto ?? 0) + (r.OtrosCostos ?? 0) + (r.PagoRepartidor ?? 0),
                })
                .ToListAsync(ct);
            var idsRutas = filas.Select(r => r.Id).ToList();

            var paradas = await db.RutaParadas.AsNoTracking()
                .Where(p => idsRutas.Contains(p.RutaId) && p.Tipo == "entrega")
                .Select(p => new { p.RutaId, p.Estado, p.LlegadaEn, p.SalidaEn })
                .ToListAsync(ct);

            var ingresos = await db.ParadaPedidos.AsNoTracking()
                .Where(pp => idsRutas.Contains(pp.Parada.RutaId) && pp.Pedido.Estado == EstadoPedido.Entregado)
                .GroupBy(pp => pp.Parada.RutaId)
                .Select(g => new { RutaId = g.Key, Total = g.Sum(pp => pp.Pedido.Total) ?? 0m })
                .ToDictionaryAsync(x => x.RutaId, x => x.Total, ct);

            var cerradas = filas.Where(r => r.Estado == "cerrada").ToList();
            rutasCerradas = cerradas.Count;
            var idsCerradas = cerradas.Select(r => r.Id).ToHashSet();
            var completadasCerradas = paradas.Count(p => idsCerradas.Contains(p.RutaId) && p.Estado == "completada");

            var km = cerradas.Where(r => r.KmInicial != null && r.KmFinal != null).Sum(r => (decimal)(r.KmFinal!.Value - r.KmInicial!.Value));
            var costos = cerradas.Sum(r => r.Costos);
            if (completadasCerradas > 0)
            {
                kmPorEntrega = Math.Round(km / completadasCerradas, 2);
                costoPorEntrega = Math.Round(costos / completadasCerradas, 2);
            }
            if (rutasCerradas > 0)
            {
                costoPorRuta = Math.Round(costos / rutasCerradas, 2);
                margenTotal = cerradas.Sum(r => ingresos.GetValueOrDefault(r.Id) - r.Costos);
                margenPorRuta = Math.Round(margenTotal.Value / rutasCerradas, 2);
                margenPorFecha = cerradas.GroupBy(r => r.Fecha)
                    .ToDictionary(g => g.Key, g => g.Sum(r => ingresos.GetValueOrDefault(r.Id) - r.Costos));
            }

            var tiempos = paradas
                .Where(p => p.Estado == "completada" && p.LlegadaEn != null && p.SalidaEn != null && p.SalidaEn >= p.LlegadaEn)
                .Select(p => (decimal)(p.SalidaEn!.Value - p.LlegadaEn!.Value).TotalMinutes)
                .ToList();
            if (tiempos.Count > 0) minutosPorEntrega = Math.Round(tiempos.Average(), 1);

            var capacidad = filas.Sum(r => r.CapacidadParadas);
            if (capacidad > 0) pctOcupacion = Math.Round(paradas.Count * 100m / capacidad, 1);
        }

        // Serie diaria completa (también los días sin nada), para que el eje de tiempo no mienta.
        var porDia = Enumerable.Range(0, hasta.DayNumber - desde.DayNumber + 1)
            .Select(i => desde.AddDays(i))
            .Select(d => new PuntoDia(d, entregasPorFecha.GetValueOrDefault(d),
                indicadoresDeRuta && margenPorFecha.TryGetValue(d, out var m) ? m : null))
            .ToList();

        var diasConEntregas = entregasPorFecha.Count(kv => kv.Value > 0);
        return Ok(new Tablero(
            desde, hasta, indicadoresDeRuta,
            entregas, diasConEntregas > 0 ? Math.Round((decimal)entregas / diasConEntregas, 1) : null,
            kmPorEntrega, minutosPorEntrega,
            facturadoPorCliente.Sum(f => f.Total), porCliente, porRango,
            cancelados, totalPedidos > 0 ? Math.Round(cancelados * 100m / totalPedidos, 1) : null,
            rutasCerradas, costoPorEntrega, costoPorRuta, margenTotal, margenPorRuta, pctOcupacion,
            porDia));
    }
}
