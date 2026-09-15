using System.Globalization;
using System.Text;
using Logistica.Datos;
using Logistica.Entidades;
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

        var csv = EscribirCsv(
            ["id", "cliente", "destinatario", "fecha_entrega", "estado", "tipo", "bultos",
             "precio_base", "recargo_urgencia", "descuento_ruta", "peajes", "total", "origen_carga"],
            filas);

        return Csv(csv, "pedidos");
    }

    [HttpGet("rutas")]
    public async Task<IActionResult> Rutas([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
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

        var csv = EscribirCsv(
            ["id", "fecha", "vehiculo", "repartidor", "estado", "km_inicial", "km_final",
             "combustible", "peajes", "otros_costos", "pago_repartidor", "cerrada_en"],
            filas);

        return Csv(csv, "rutas");
    }

    [HttpGet("resultados")]
    public async Task<IActionResult> Resultados([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.Fecha >= desde && r.Fecha <= hasta && r.Estado == "cerrada")
            .OrderBy(r => r.Fecha)
            .ToListAsync(ct);

        // Antes era un SumAsync por ruta dentro del foreach. Una sola consulta agrupada trae los
        // ingresos de todas las rutas del rango a la vez.
        var rutaIds = rutas.Select(r => r.Id).ToList();
        var ingresosPorRuta = await db.ParadaPedidos
            .Where(pp => rutaIds.Contains(pp.Parada.RutaId) && pp.Pedido.Estado == EstadoPedido.Entregado)
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

        var csv = EscribirCsv(["ruta_id", "fecha", "ingresos", "costos", "margen"], filas);
        return Csv(csv, "resultados");
    }

    private static FileContentResult Csv(string contenido, string nombre)
    {
        // BOM UTF-8: sin esto Excel en Windows interpreta acentos como caracteres sueltos.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(contenido)).ToArray();
        return new FileContentResult(bytes, "text/csv") { FileDownloadName = $"{nombre}.csv" };
    }

    // Escritor propio, sin dependencia nueva (~20 líneas): separador ';' (planilla en es-AR) y
    // comillas dobles escapadas duplicándolas, solo cuando el valor las necesita.
    private static string EscribirCsv(string[] encabezados, IEnumerable<object?[]> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', encabezados));
        foreach (var fila in filas)
            sb.AppendLine(string.Join(';', fila.Select(EscaparCampo)));
        return sb.ToString();
    }

    // Campos de texto libre (destinatario, razón social) terminan en un CSV que un admin abre en
    // Excel/Sheets: si empiezan con =, +, -, @ el programa los interpreta como fórmula (CSV
    // injection). Anteponer un apóstrofo neutraliza esa interpretación sin alterar el dato.
    private static readonly char[] PrefijosFormula = ['=', '+', '-', '@'];

    private static string EscaparCampo(object? valor)
    {
        var texto = valor switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTimeOffset dt => dt.ToString("O"),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? "",
        };
        if (texto.Length > 0 && PrefijosFormula.Contains(texto[0]))
            texto = "'" + texto;

        return texto.Contains(';') || texto.Contains('"') || texto.Contains('\n')
            ? $"\"{texto.Replace("\"", "\"\"")}\""
            : texto;
    }
}
