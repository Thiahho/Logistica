using System.ComponentModel.DataAnnotations;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// B7 — costos fijos y rentabilidad mensual contra la estructura objetivo (acta RF-43, changelog 4.21;
/// diseño_e2_rangos_liquidacion.md §5). Solo Administración: son importes y márgenes (RNF-08). No es el
/// tablero de indicadores (B2/E3): es el resultado de un mes, en plata, contra los tramos que la Empresa
/// defina.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "Administracion")]
public class RentabilidadController(LogisticaDbContext db) : ControllerBase
{
    public record CostoFijoDto(long Id, DateOnly Mes, string Categoria, string? Descripcion, decimal Monto);

    public record GuardarCostoFijoRequest(
        [Required(AllowEmptyStrings = false, ErrorMessage = "El mes es obligatorio (formato 2026-09).")] string Mes,
        [Required(AllowEmptyStrings = false, ErrorMessage = "La categoría es obligatoria."), StringLength(100)] string Categoria,
        [StringLength(500)] string? Descripcion,
        [Range(0, 1_000_000_000_000, ErrorMessage = "El monto no puede ser negativo.")] decimal Monto);

    public record CopiarMesRequest(string Mes);

    public record ObjetivoDto(
        [Required(AllowEmptyStrings = false, ErrorMessage = "Cada tramo necesita un nombre."), StringLength(100)] string Nombre,
        [Range(0, 100, ErrorMessage = "Los porcentajes van de 0 a 100.")] decimal PctMin,
        [Range(0, 100, ErrorMessage = "Los porcentajes van de 0 a 100.")] decimal PctMax,
        string[] Fuentes);

    public record ResultadoMes(
        DateOnly Mes, decimal Ingresos, decimal PagoRepartidor, decimal Combustible, decimal Peajes, decimal OtrosCostos,
        decimal Variables, decimal Fijos, decimal Margen, decimal? PctMargen, int RutasCerradas,
        Dictionary<string, decimal> FijosPorCategoria, List<TramoEvaluado> Tramos, List<CostoFijoDto> CostosFijos);

    /// <summary>
    /// Resultado del mes. Ingresos: lo facturable que nació en el mes (factura_items aprobados), se haya
    /// emitido la factura o no — mismo criterio que la facturación de los rangos. Costos variables: los de
    /// las rutas cerradas con fecha en el mes. Fijos: los cargados para el mes.
    /// </summary>
    [HttpGet("rentabilidad")]
    public async Task<IActionResult> Resultado([FromQuery] string mes, CancellationToken ct)
    {
        if (Rentabilidad.Mes(mes) is not { } inicio) return BadRequest("Mes inválido. Formato: 2026-09.");
        var fin = inicio.AddMonths(1).AddDays(-1);
        var (desdeUtc, _) = Reloj.RangoLocalUtc(inicio);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(fin);

        var ingresos = await db.FacturaItems.AsNoTracking()
            .Where(i => i.Estado == "aprobado" && i.Monto != null && i.CreadoEn >= desdeUtc && i.CreadoEn < hastaUtc)
            .SumAsync(i => i.Monto!.Value, ct);

        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.Estado == "cerrada" && r.Fecha >= inicio && r.Fecha <= fin)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Cantidad = g.Count(),
                Pago = g.Sum(r => r.PagoRepartidor ?? 0),
                Combustible = g.Sum(r => r.CombustibleMonto ?? 0),
                Peajes = g.Sum(r => r.PeajesMonto ?? 0),
                Otros = g.Sum(r => r.OtrosCostos ?? 0),
            })
            .SingleOrDefaultAsync(ct);

        var costos = await CostosDelMes(inicio).ToListAsync(ct);
        var fijos = costos.GroupBy(c => c.Categoria).ToDictionary(g => g.Key, g => g.Sum(c => c.Monto));

        var datos = new DatosMes(ingresos, rutas?.Pago ?? 0, rutas?.Combustible ?? 0, rutas?.Peajes ?? 0, rutas?.Otros ?? 0, fijos);
        var objetivos = await db.ObjetivosRentabilidad.AsNoTracking().OrderBy(o => o.Orden).ToListAsync(ct);

        return Ok(new ResultadoMes(
            inicio, datos.Ingresos, datos.PagoRepartidor, datos.Combustible, datos.Peajes, datos.OtrosCostos,
            datos.Variables, datos.Fijos, datos.Margen,
            datos.Ingresos > 0 ? Math.Round(datos.Margen * 100m / datos.Ingresos, 2) : null,
            rutas?.Cantidad ?? 0, fijos,
            objetivos.Select(o => Rentabilidad.Evaluar(datos, o)).ToList(), costos));
    }

    [HttpPost("costos-fijos")]
    public async Task<IActionResult> Agregar(GuardarCostoFijoRequest req, CancellationToken ct)
    {
        if (Rentabilidad.Mes(req.Mes) is not { } mes) return BadRequest("Mes inválido. Formato: 2026-09.");
        var costo = new CostoFijo
        {
            Mes = mes,
            Categoria = req.Categoria.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim(),
            Monto = req.Monto,
            CreadoPor = User.UsuarioId(),
        };
        db.CostosFijos.Add(costo);
        await db.SaveChangesAsync(ct);
        return Ok(new CostoFijoDto(costo.Id, costo.Mes, costo.Categoria, costo.Descripcion, costo.Monto));
    }

    [HttpPut("costos-fijos/{id:long}")]
    public async Task<IActionResult> Actualizar(long id, GuardarCostoFijoRequest req, CancellationToken ct)
    {
        if (Rentabilidad.Mes(req.Mes) is not { } mes) return BadRequest("Mes inválido. Formato: 2026-09.");
        var costo = await db.CostosFijos.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (costo is null) return NotFound();
        costo.Mes = mes;
        costo.Categoria = req.Categoria.Trim();
        costo.Descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        costo.Monto = req.Monto;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("costos-fijos/{id:long}")]
    public async Task<IActionResult> Borrar(long id, CancellationToken ct)
    {
        var borrados = await db.CostosFijos.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return borrados == 0 ? NotFound() : NoContent();
    }

    /// <summary>Copia al mes indicado los costos fijos del mes anterior, para no retipear lo que de verdad
    /// es fijo. Solo si el mes todavía no tiene ninguno: no duplica.</summary>
    [HttpPost("costos-fijos/copiar-mes-anterior")]
    public async Task<IActionResult> CopiarMesAnterior(CopiarMesRequest req, CancellationToken ct)
    {
        if (Rentabilidad.Mes(req.Mes) is not { } mes) return BadRequest("Mes inválido. Formato: 2026-09.");
        if (await db.CostosFijos.AnyAsync(c => c.Mes == mes, ct))
            return Conflict("Ese mes ya tiene costos fijos cargados; copiar duplicaría lo que ya está.");

        var anteriores = await CostosDelMes(mes.AddMonths(-1)).ToListAsync(ct);
        if (anteriores.Count == 0) return Conflict("El mes anterior no tiene costos fijos para copiar.");

        var actor = User.UsuarioId();
        db.CostosFijos.AddRange(anteriores.Select(c => new CostoFijo
        {
            Mes = mes, Categoria = c.Categoria, Descripcion = c.Descripcion, Monto = c.Monto, CreadoPor = actor,
        }));
        await db.SaveChangesAsync(ct);
        return Ok(new { Copiados = anteriores.Count });
    }

    [HttpGet("objetivos-rentabilidad")]
    public async Task<IActionResult> Objetivos(CancellationToken ct) =>
        Ok(await db.ObjetivosRentabilidad.AsNoTracking().OrderBy(o => o.Orden)
            .Select(o => new ObjetivoDto(o.Nombre, o.PctMin, o.PctMax, o.Fuentes))
            .ToListAsync(ct));

    /// <summary>Reemplaza la estructura objetivo entera: son pocos tramos que se piensan juntos, no filas
    /// sueltas. Lista vacía = sin estructura objetivo.</summary>
    [HttpPut("objetivos-rentabilidad")]
    public async Task<IActionResult> GuardarObjetivos(List<ObjetivoDto> tramos, CancellationToken ct)
    {
        if (tramos.Count > 20) return BadRequest("La estructura objetivo admite hasta 20 tramos.");
        foreach (var t in tramos)
        {
            if (t.PctMin > t.PctMax) return BadRequest($"\"{t.Nombre}\": el mínimo no puede ser mayor que el máximo.");
            if (t.Fuentes is null || t.Fuentes.Length == 0) return BadRequest($"\"{t.Nombre}\": elegí al menos una fuente.");
            var invalidas = t.Fuentes.Where(f => !Rentabilidad.FuenteValida(f)).ToList();
            if (invalidas.Count > 0) return BadRequest($"\"{t.Nombre}\": fuentes desconocidas: {string.Join(", ", invalidas)}.");
        }

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.ObjetivosRentabilidad.ExecuteDeleteAsync(ct);
            db.ObjetivosRentabilidad.AddRange(tramos.Select((t, i) => new ObjetivoRentabilidad
            {
                Nombre = t.Nombre.Trim(), PctMin = t.PctMin, PctMax = t.PctMax,
                Fuentes = t.Fuentes.Select(f => f.Trim()).Distinct().ToArray(), Orden = i,
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return NoContent();
    }

    private IQueryable<CostoFijoDto> CostosDelMes(DateOnly mes) =>
        db.CostosFijos.AsNoTracking()
            .Where(c => c.Mes == mes)
            .OrderBy(c => c.Categoria).ThenBy(c => c.Id)
            .Select(c => new CostoFijoDto(c.Id, c.Mes, c.Categoria, c.Descripcion, c.Monto));
}
