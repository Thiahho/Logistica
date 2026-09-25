using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>RF-30: exportación tabular de pedidos, rutas y resultados por rango de fechas.
/// "Reemplaza el módulo de reportes" (construccion_v1.md §4.1) — no hay tablero, solo CSV.</summary>
[ApiController]
[Route("api/exportar")]
[Authorize(Policy = "Administracion")]
public class ExportarController(LogisticaDbContext db) : ControllerBase
{
    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        if (RangoInvalido(desde, hasta)) return BadRequest($"El rango de fechas debe ser válido y de hasta {MaxDiasRango} días.");
        var filas = await db.Pedidos.AsNoTracking()
            .Where(p => p.FechaEntrega >= desde && p.FechaEntrega <= hasta)
            .OrderBy(p => p.FechaEntrega)
            .Select(p => new object?[]
            {
                p.Id, p.Cliente.RazonSocial, p.DestinatarioNombre, p.FechaEntrega,
                p.Estado, p.Tipo, p.Bultos, p.PrecioBase, p.RecargoUrgencia,
                p.DescuentoRuta, p.Peajes, p.Total, p.OrigenCarga,
            })
            .ToListAsync(ct);

        var csv = Csv.Escribir(
            ["id", "cliente", "destinatario", "fecha_entrega", "estado", "tipo", "bultos",
             "precio_base", "recargo_urgencia", "descuento_ruta", "peajes", "total", "origen_carga"],
            filas);

        return Csv.Archivo(csv, "pedidos");
    }

    [HttpGet("rutas")]
    public async Task<IActionResult> Rutas([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        if (RangoInvalido(desde, hasta)) return BadRequest($"El rango de fechas debe ser válido y de hasta {MaxDiasRango} días.");
        var filas = await db.Rutas.AsNoTracking()
            .Where(r => r.Fecha >= desde && r.Fecha <= hasta)
            .OrderBy(r => r.Fecha)
            .Select(r => new object?[]
            {
                r.Id, r.Fecha, r.Vehiculo != null ? r.Vehiculo.Patente : null, r.Repartidor != null ? r.Repartidor.Nombre : null,
                r.Estado, r.KmInicial, r.KmFinal, r.CombustibleMonto, r.PeajesMonto,
                r.OtrosCostos, r.PagoRepartidor, r.CerradaEn,
            })
            .ToListAsync(ct);

        var csv = Csv.Escribir(
            ["id", "fecha", "vehiculo", "repartidor", "estado", "km_inicial", "km_final",
             "combustible", "peajes", "otros_costos", "pago_repartidor", "cerrada_en"],
            filas);

        return Csv.Archivo(csv, "rutas");
    }

    [HttpGet("resultados")]
    public async Task<IActionResult> Resultados([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        if (RangoInvalido(desde, hasta)) return BadRequest($"El rango de fechas debe ser válido y de hasta {MaxDiasRango} días.");
        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.Fecha >= desde && r.Fecha <= hasta && r.Estado == "cerrada")
            .OrderBy(r => r.Fecha)
            .ToListAsync(ct);

        // Una sola consulta agrupada trae los ingresos de todas las rutas del rango a la vez. Filtra por
        // el rango (join), no con una lista IN de miles de ids: con 4.000 rutas eso tardaba 1,7 s.
        var ingresosPorRuta = await db.ParadaPedidos
            .Where(pp => pp.Parada.Ruta.Fecha >= desde && pp.Parada.Ruta.Fecha <= hasta
                && pp.Parada.Ruta.Estado == "cerrada" && pp.Pedido.Estado == EstadoPedido.Entregado)
            .GroupBy(pp => pp.Parada.RutaId)
            .Select(g => new { RutaId = g.Key, Ingresos = g.Sum(pp => pp.Pedido.Total) ?? 0m })
            .ToDictionaryAsync(x => x.RutaId, x => x.Ingresos, ct);

        var filas = new List<object?[]>();
        foreach (var r in rutas)
        {
            var ingresos = ingresosPorRuta.GetValueOrDefault(r.Id);
            var costos = (r.CombustibleMonto ?? 0) + (r.PeajesMonto ?? 0) + (r.OtrosCostos ?? 0) + (r.PagoRepartidor ?? 0);
            filas.Add([r.Id, r.Fecha, ingresos, costos, ingresos - costos]);
        }

        var csv = Csv.Escribir(["ruta_id", "fecha", "ingresos", "costos", "margen"], filas);
        return Csv.Archivo(csv, "resultados");
    }

    /// <summary>Un export arma todo el CSV en memoria: sin tope, un rango de décadas (o vacío al revés)
    /// podía traer la tabla entera. Un año alcanza para cualquier planilla de gestión.</summary>
    private const int MaxDiasRango = 366;

    private static bool RangoInvalido(DateOnly desde, DateOnly hasta) =>
        hasta < desde || hasta.DayNumber - desde.DayNumber > MaxDiasRango;
}
