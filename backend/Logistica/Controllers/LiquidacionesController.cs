using System.ComponentModel.DataAnnotations;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Controllers;

/// <summary>
/// B4 — liquidación al repartidor (acta RF-41, changelog 4.21; diseño_e2_rangos_liquidacion.md §2).
/// Solo Administración: son importes (RNF-08). Los parámetros son configuración de la Empresa
/// (Anexo I §6); la liquidación es el comprobante por repartidor y período, inmutable una vez emitido
/// (trg_liquidaciones_inmutable, trg_rutas_liquidada).
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "Administracion")]
public class LiquidacionesController(LogisticaDbContext db, IOptions<OpcionesPruebaEntrega> pruebaEntrega) : ControllerBase
{
    /// <summary>Un período se liquida en semanas o meses (acta §9.3); el tope evita armar en memoria
    /// años de rutas por un error de fecha, mismo criterio que los exportes.</summary>
    private const int MaxDiasPeriodo = 366;

    public record ParametroVigente(
        long Id, string TipoVehiculo, decimal PagoPorEntrega, decimal BonoRuta, decimal PctMinimoExitosas,
        string[] MotivosImputables, DateOnly VigenteDesde, DateOnly? VigenteHasta);

    public record ParametrosResponse(List<ParametroVigente> Parametros, string[] MotivosFallo);

    public record FijarParametrosRequest(
        [Range(0, 100_000_000, ErrorMessage = "El pago por entrega debe estar entre 0 y 100.000.000.")] decimal PagoPorEntrega,
        [Range(0, 100_000_000, ErrorMessage = "El bono debe estar entre 0 y 100.000.000.")] decimal BonoRuta,
        [Range(0, 100, ErrorMessage = "El mínimo de entregas exitosas es un porcentaje entre 0 y 100.")] decimal PctMinimoExitosas,
        string[] MotivosImputables);

    public record RutaLiquidable(
        long Id, DateOnly Fecha, string? VehiculoPatente, int? Entregas, int? FallidasImputables, decimal? PctExito,
        decimal? PagoEntregas, decimal? Bono, decimal PagoRepartidor, string? PagoAjusteMotivo);

    public record Previsualizacion(Guid RepartidorId, string RepartidorNombre, DateOnly Desde, DateOnly Hasta,
        List<RutaLiquidable> Rutas, decimal Total);

    public record EmitirRequest(Guid RepartidorId, DateOnly Desde, DateOnly Hasta,
        [StringLength(2000)] string? Nota);

    public record LiquidacionResumen(long Id, Guid RepartidorId, string RepartidorNombre, DateOnly Desde, DateOnly Hasta,
        int CantidadRutas, decimal Total, DateTimeOffset EmitidaEn);

    public record LiquidacionDetalle(long Id, Guid RepartidorId, string RepartidorNombre, DateOnly Desde, DateOnly Hasta,
        int CantidadRutas, decimal Total, string? Nota, DateTimeOffset EmitidaEn, string EmitidaPorNombre,
        List<RutaLiquidable> Rutas);

    // ── Parámetros ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Los vigentes hoy por tipo de vehículo, y la lista de motivos de fallo para elegir los
    /// imputables. Un tipo sin fila vigente no aparece: su pago se tipea a mano.</summary>
    [HttpGet("parametros-liquidacion")]
    public async Task<IActionResult> Parametros(CancellationToken ct)
    {
        var hoy = Reloj.HoyLocal();
        var vigentes = await db.ParametrosLiquidacion.AsNoTracking()
            .Where(p => p.VigenteDesde <= hoy && (p.VigenteHasta == null || p.VigenteHasta >= hoy))
            .OrderBy(p => p.TipoVehiculo)
            .Select(p => new ParametroVigente(p.Id, p.TipoVehiculo, p.PagoPorEntrega, p.BonoRuta, p.PctMinimoExitosas,
                p.MotivosImputables, p.VigenteDesde, p.VigenteHasta))
            .ToListAsync(ct);
        return Ok(new ParametrosResponse(vigentes, pruebaEntrega.Value.MotivosFallo));
    }

    /// <summary>Fija los valores desde hoy para un tipo de vehículo, igual que una tarifa: la fila
    /// vigente se cierra ayer y nace una nueva. Si la vigente ya arrancó hoy, se corrige en el lugar —
    /// ninguna ruta cerró todavía con un día de vigencia que no existía. Las rutas ya cerradas conservan
    /// el desglose con que se cerraron (columnas liq_* de rutas).</summary>
    [HttpPut("parametros-liquidacion/{tipoVehiculo}")]
    public async Task<IActionResult> FijarParametros(string tipoVehiculo, FijarParametrosRequest req, CancellationToken ct)
    {
        if (tipoVehiculo is not ("camioneta" or "moto")) return BadRequest("Tipo de vehículo inválido.");

        var motivos = (req.MotivosImputables ?? []).Select(m => m.Trim()).Distinct().ToArray();
        var desconocidos = motivos.Except(pruebaEntrega.Value.MotivosFallo).ToList();
        if (desconocidos.Count > 0)
            return BadRequest($"Motivos de fallo desconocidos: {string.Join(", ", desconocidos)}.");

        var hoy = Reloj.HoyLocal();
        var vigente = await db.ParametrosLiquidacion
            .SingleOrDefaultAsync(p => p.TipoVehiculo == tipoVehiculo && p.VigenteHasta == null, ct);

        if (vigente is not null && vigente.VigenteDesde >= hoy)
        {
            vigente.PagoPorEntrega = req.PagoPorEntrega;
            vigente.BonoRuta = req.BonoRuta;
            vigente.PctMinimoExitosas = req.PctMinimoExitosas;
            vigente.MotivosImputables = motivos;
        }
        else
        {
            if (vigente is not null) vigente.VigenteHasta = hoy.AddDays(-1);
            db.ParametrosLiquidacion.Add(new ParametroLiquidacion
            {
                TipoVehiculo = tipoVehiculo,
                PagoPorEntrega = req.PagoPorEntrega,
                BonoRuta = req.BonoRuta,
                PctMinimoExitosas = req.PctMinimoExitosas,
                MotivosImputables = motivos,
                VigenteDesde = hoy,
                CreadoPor = User.UsuarioId(),
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Liquidaciones ──────────────────────────────────────────────────────────────────────────

    [HttpGet("liquidaciones/previsualizacion")]
    public async Task<IActionResult> Previsualizar(
        [FromQuery] Guid repartidorId, [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        if (PeriodoInvalido(desde, hasta) is { } error) return BadRequest(error);
        var repartidor = await RepartidorAsync(repartidorId, ct);
        if (repartidor is null) return NotFound("El repartidor no existe.");

        var rutas = await Liquidables(repartidorId, desde, hasta).ToListAsync(ct);
        return Ok(new Previsualizacion(repartidorId, repartidor, desde, hasta, rutas, rutas.Sum(r => r.PagoRepartidor)));
    }

    /// <summary>Emite el comprobante: vincula las rutas cerradas del período que no estaban en otra
    /// liquidación. La vinculación es condicional (liquidacion_id is null) y se controla la cantidad de
    /// filas: si otra emisión se llevó alguna ruta en el medio, no se emite nada (409).</summary>
    [HttpPost("liquidaciones")]
    public async Task<IActionResult> Emitir(EmitirRequest req, CancellationToken ct)
    {
        if (PeriodoInvalido(req.Desde, req.Hasta) is { } error) return BadRequest(error);
        if (await RepartidorAsync(req.RepartidorId, ct) is null) return NotFound("El repartidor no existe.");

        var rutas = await Liquidables(req.RepartidorId, req.Desde, req.Hasta).ToListAsync(ct);
        if (rutas.Count == 0) return Conflict("No hay rutas cerradas sin liquidar para ese repartidor en el período.");

        var ids = rutas.Select(r => r.Id).ToList();
        var liquidacion = new Liquidacion
        {
            RepartidorId = req.RepartidorId,
            Desde = req.Desde,
            Hasta = req.Hasta,
            CantidadRutas = rutas.Count,
            Total = rutas.Sum(r => r.PagoRepartidor),
            Nota = string.IsNullOrWhiteSpace(req.Nota) ? null : req.Nota.Trim(),
            EmitidaPor = User.UsuarioId(),
        };

        var estrategia = db.Database.CreateExecutionStrategy();
        var emitida = await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Liquidaciones.Add(liquidacion);
            await db.SaveChangesAsync(ct);

            var vinculadas = await db.Rutas
                .Where(r => ids.Contains(r.Id) && r.LiquidacionId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LiquidacionId, liquidacion.Id), ct);
            if (vinculadas != ids.Count)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            await tx.CommitAsync(ct);
            return true;
        });

        if (!emitida)
        {
            db.ChangeTracker.Clear();
            return Conflict("Otra liquidación tomó alguna de estas rutas mientras se emitía. Volvé a previsualizar.");
        }
        return CreatedAtAction(nameof(Detalle), new { id = liquidacion.Id }, new { liquidacion.Id });
    }

    [HttpGet("liquidaciones")]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? repartidorId, [FromQuery] int? pagina, [FromQuery] int? tamanioPagina, CancellationToken ct)
    {
        var query = db.Liquidaciones.AsNoTracking();
        if (repartidorId is not null) query = query.Where(l => l.RepartidorId == repartidorId);

        var total = await query.CountAsync(ct);
        var tamanio = Paginacion.TamanioEfectivo(tamanioPagina);
        var numero = Math.Max(pagina ?? 1, 1);
        var items = await query
            .OrderByDescending(l => l.EmitidaEn).ThenByDescending(l => l.Id)
            .Skip((numero - 1) * tamanio).Take(tamanio)
            .Select(l => new LiquidacionResumen(l.Id, l.RepartidorId, l.Repartidor.Nombre, l.Desde, l.Hasta,
                l.CantidadRutas, l.Total, l.EmitidaEn))
            .ToListAsync(ct);
        return Ok(new ListaPaginada<LiquidacionResumen>(items, total));
    }

    [HttpGet("liquidaciones/{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var detalle = await DetalleAsync(id, ct);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    /// <summary>Comprobante en CSV, una fila por ruta más el total.</summary>
    [HttpGet("liquidaciones/{id:long}/csv")]
    public async Task<IActionResult> Comprobante(long id, CancellationToken ct)
    {
        var l = await DetalleAsync(id, ct);
        if (l is null) return NotFound();

        var filas = l.Rutas.Select(r => new object?[]
        {
            r.Id, r.Fecha, r.VehiculoPatente, r.Entregas, r.FallidasImputables, r.PctExito,
            r.PagoEntregas, r.Bono, r.PagoRepartidor, r.PagoAjusteMotivo,
        }).Append([null, null, null, null, null, null, null, "total", l.Total, null]);

        var csv = Csv.Escribir(
            ["ruta_id", "fecha", "vehiculo", "entregas", "fallidas_imputables", "pct_exito",
             "pago_entregas", "bono", "pago", "motivo_ajuste"],
            filas);
        return Csv.Archivo(csv, $"liquidacion-{l.Id}-{l.RepartidorNombre}-{l.Desde:yyyyMMdd}-{l.Hasta:yyyyMMdd}");
    }

    private async Task<LiquidacionDetalle?> DetalleAsync(long id, CancellationToken ct)
    {
        var l = await db.Liquidaciones.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.RepartidorId, Repartidor = x.Repartidor.Nombre, x.Desde, x.Hasta, x.CantidadRutas, x.Total,
                x.Nota, x.EmitidaEn,
                EmitidaPor = db.Usuarios.Where(u => u.Id == x.EmitidaPor).Select(u => u.Nombre).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        if (l is null) return null;

        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.LiquidacionId == id)
            .OrderBy(r => r.Fecha).ThenBy(r => r.Id)
            .Select(ALiquidable)
            .ToListAsync(ct);

        return new LiquidacionDetalle(l.Id, l.RepartidorId, l.Repartidor, l.Desde, l.Hasta, l.CantidadRutas, l.Total,
            l.Nota, l.EmitidaEn, l.EmitidaPor ?? "", rutas);
    }

    private IQueryable<RutaLiquidable> Liquidables(Guid repartidorId, DateOnly desde, DateOnly hasta) =>
        db.Rutas.AsNoTracking()
            .Where(r => r.RepartidorId == repartidorId && r.Estado == "cerrada" && r.LiquidacionId == null
                        && r.Fecha >= desde && r.Fecha <= hasta && r.PagoRepartidor != null)
            .OrderBy(r => r.Fecha).ThenBy(r => r.Id)
            .Select(ALiquidable);

    private static readonly System.Linq.Expressions.Expression<Func<Ruta, RutaLiquidable>> ALiquidable = r =>
        new RutaLiquidable(r.Id, r.Fecha, r.Vehiculo != null ? r.Vehiculo.Patente : null, r.LiqEntregas,
            r.LiqFallidasImputables, r.LiqPctExito, r.LiqPagoEntregas, r.LiqBono, r.PagoRepartidor!.Value,
            r.PagoAjusteMotivo);

    private Task<string?> RepartidorAsync(Guid id, CancellationToken ct) =>
        db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Rol == Roles.Repartidor)
            .Select(u => u.Nombre)
            .SingleOrDefaultAsync(ct);

    private static string? PeriodoInvalido(DateOnly desde, DateOnly hasta) =>
        hasta < desde ? "La fecha hasta no puede ser anterior a la fecha desde."
        : hasta.DayNumber - desde.DayNumber > MaxDiasPeriodo ? $"El período puede tener hasta {MaxDiasPeriodo} días."
        : null;
}
