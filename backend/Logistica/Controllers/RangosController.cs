using System.ComponentModel.DataAnnotations;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// B3 — rangos de cliente (acta RF-42, changelog 4.21; diseño_e2_rangos_liquidacion.md §3). Solo
/// Administración: los números con que se calcula un rango son internos (RF-33); el cliente ve su rango
/// y su descuento desde "Mi plan" (MiCuentaController), nunca esto.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "Administracion")]
public class RangosController(LogisticaDbContext db, RangoClienteService rangos) : ControllerBase
{
    public record RangoConfig(
        string Codigo, string Nombre, int Orden, int? MinEnviosTrimestre, decimal? MinFacturacionTrimestre,
        int? MinAntiguedadMeses, int? MinSemanasActivas, decimal? MinPctPagosEnTermino,
        decimal DescuentoPct, decimal? LimiteCredito, int Prioridad);

    public record ActualizarRangoRequest(
        [Range(0, 1_000_000, ErrorMessage = "El mínimo de envíos debe estar entre 0 y 1.000.000.")] int? MinEnviosTrimestre,
        [Range(0, 1_000_000_000_000, ErrorMessage = "El mínimo de facturación no puede ser negativo.")] decimal? MinFacturacionTrimestre,
        [Range(0, 1200, ErrorMessage = "La antigüedad mínima debe estar entre 0 y 1200 meses.")] int? MinAntiguedadMeses,
        [Range(0, 14, ErrorMessage = "Las semanas activas de un trimestre van de 0 a 14.")] int? MinSemanasActivas,
        [Range(0, 100, ErrorMessage = "El mínimo de pagos en término es un porcentaje entre 0 y 100.")] decimal? MinPctPagosEnTermino,
        [Range(0, 100, ErrorMessage = "El descuento es un porcentaje entre 0 y 100.")] decimal DescuentoPct,
        [Range(0, 1_000_000_000_000, ErrorMessage = "El límite de crédito no puede ser negativo.")] decimal? LimiteCredito,
        [Range(0, 100, ErrorMessage = "La prioridad va de 0 a 100.")] int Prioridad);

    public record RecalculoRequest(string Trimestre);

    public record AjusteRangoRequest(short Ajuste, [StringLength(2000)] string? Motivo, DateOnly? Vence);

    public record HistorialRango(
        string? RangoAnterior, string RangoNuevo, string Origen, string? Trimestre, string? Criterios,
        string? Motivo, string RegistradoPor, DateTimeOffset RegistradoEn);

    public record RangoCliente(
        int ClienteId, string Calculado, string Efectivo, DateTimeOffset? CalculadoEn, short Ajuste,
        string? AjusteMotivo, DateOnly? AjusteVence, bool AjusteVigente, decimal DescuentoPct, decimal? LimiteCredito,
        List<HistorialRango> Historial);

    [HttpGet("rangos")]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Rangos.AsNoTracking().OrderBy(r => r.Orden)
            .Select(r => new RangoConfig(r.Codigo, r.Nombre, r.Orden, r.MinEnviosTrimestre, r.MinFacturacionTrimestre,
                r.MinAntiguedadMeses, r.MinSemanasActivas, r.MinPctPagosEnTermino, r.DescuentoPct, r.LimiteCredito, r.Prioridad))
            .ToListAsync(ct));

    /// <summary>Umbrales y efectos de un rango (Anexo I §6: configuración de la Empresa). "Sin rango" no
    /// lleva umbrales: es donde cae quien no alcanza ninguno. Cambiar un umbral no mueve a nadie hasta el
    /// próximo recálculo; cambiar el descuento rige para lo que se cotice desde ahora (P1).</summary>
    [HttpPut("rangos/{codigo}")]
    public async Task<IActionResult> Actualizar(string codigo, ActualizarRangoRequest req, CancellationToken ct)
    {
        var rango = await db.Rangos.SingleOrDefaultAsync(r => r.Codigo == codigo, ct);
        if (rango is null) return NotFound();
        var conUmbrales = req.MinEnviosTrimestre is not null || req.MinFacturacionTrimestre is not null
            || req.MinAntiguedadMeses is not null || req.MinSemanasActivas is not null || req.MinPctPagosEnTermino is not null;
        if (codigo == RangosCliente.SinRango && conUmbrales)
            return BadRequest("\"Sin rango\" no lleva umbrales: es donde queda el cliente que no alcanza ningún otro.");

        rango.MinEnviosTrimestre = req.MinEnviosTrimestre;
        rango.MinFacturacionTrimestre = req.MinFacturacionTrimestre;
        rango.MinAntiguedadMeses = req.MinAntiguedadMeses;
        rango.MinSemanasActivas = req.MinSemanasActivas;
        rango.MinPctPagosEnTermino = req.MinPctPagosEnTermino;
        rango.DescuentoPct = req.DescuentoPct;
        rango.LimiteCredito = req.LimiteCredito;
        rango.Prioridad = req.Prioridad;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Qué rango le tocaría a cada cliente activo con lo medido en el trimestre, sin escribir.</summary>
    [HttpPost("rangos/recalculo/previsualizacion")]
    public async Task<IActionResult> Previsualizar(RecalculoRequest req, CancellationToken ct)
    {
        if (Validar(req.Trimestre) is { } error) return BadRequest(error);
        var (desde, hasta) = RangosCliente.Trimestre(req.Trimestre)!.Value;
        return Ok(await rangos.MedirAsync(desde, hasta, ct));
    }

    /// <summary>Recálculo trimestral (D4). Lo dispara administración: no hay scheduler.</summary>
    [HttpPost("rangos/recalculo")]
    public async Task<IActionResult> Recalcular(RecalculoRequest req, CancellationToken ct)
    {
        if (Validar(req.Trimestre) is { } error) return BadRequest(error);
        var (desde, hasta) = RangosCliente.Trimestre(req.Trimestre)!.Value;
        return Ok(await rangos.AplicarAsync(req.Trimestre, desde, hasta, User.UsuarioId(), ct));
    }

    [HttpGet("clientes/{id:int}/rango")]
    public async Task<IActionResult> DeCliente(int id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.RangoCalculado, c.RangoCalculadoEn, c.RangoAjuste, c.RangoAjusteMotivo, c.RangoAjusteVence })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return NotFound();

        var lista = await db.Rangos.AsNoTracking().ToListAsync(ct);
        var hoy = Reloj.HoyLocal();
        var efectivo = RangosCliente.Efectivo(cliente.RangoCalculado, cliente.RangoAjuste, cliente.RangoAjusteVence, hoy, lista);
        var rango = lista.Single(r => r.Codigo == efectivo);

        var historial = await db.ClienteRangos.AsNoTracking()
            .Where(h => h.ClienteId == id)
            .OrderByDescending(h => h.RegistradoEn).ThenByDescending(h => h.Id)
            .Take(50)
            .Select(h => new HistorialRango(h.RangoAnterior, h.RangoNuevo, h.Origen, h.Trimestre, h.Criterios, h.Motivo,
                db.Usuarios.Where(u => u.Id == h.RegistradoPor).Select(u => u.Nombre).FirstOrDefault() ?? "",
                h.RegistradoEn))
            .ToListAsync(ct);

        return Ok(new RangoCliente(id, cliente.RangoCalculado, efectivo, cliente.RangoCalculadoEn, cliente.RangoAjuste,
            cliente.RangoAjusteMotivo, cliente.RangoAjusteVence,
            cliente.RangoAjuste != 0 && cliente.RangoAjusteVence >= hoy, rango.DescuentoPct, rango.LimiteCredito, historial));
    }

    /// <summary>Definición F: ajuste manual de ±1 rango con motivo y vencimiento, o 0 para quitarlo.</summary>
    [HttpPut("clientes/{id:int}/rango/ajuste")]
    public async Task<IActionResult> Ajustar(int id, AjusteRangoRequest req, CancellationToken ct)
    {
        var error = await rangos.AjustarAsync(id, req.Ajuste, req.Motivo, req.Vence, User.UsuarioId(), ct);
        return error switch
        {
            null => NoContent(),
            "El cliente no existe." => NotFound(error),
            _ => BadRequest(error),
        };
    }

    /// <summary>Un trimestre ya cerrado: medir uno en curso daría un rango con datos incompletos.</summary>
    private static string? Validar(string? trimestre)
    {
        if (string.IsNullOrWhiteSpace(trimestre) || RangosCliente.Trimestre(trimestre) is not { } rango)
            return "Trimestre inválido. Formato: 2026-T3.";
        return rango.Hasta >= Reloj.HoyLocal() ? "Ese trimestre todavía no terminó: se recalcula un trimestre cerrado." : null;
    }
}
