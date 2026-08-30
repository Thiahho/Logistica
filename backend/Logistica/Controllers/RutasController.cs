using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Listado, armado (RF-10 a RF-17) y cierre económico (RF-26/RF-27) de rutas.
///
/// Ruta.Estado solo tiene tres valores (planificada|en_curso|cerrada, ver el check constraint;
/// no hay migración nueva en esta fase). Mientras está 'planificada' las paradas son totalmente
/// reeditables (GuardarParadas reemplaza todo). CerrarPlanificacion (RF-17) es la acción
/// explícita que la pasa a 'en_curso' Y transiciona en bloque sus pedidos Confirmado -> EnRuta
/// (construccion_v1.md §5: "confirmado → en_ruta | Existe fila en parada_pedidos") — recién ahí
/// la ruta aparece en /api/mis-paradas. Esto adelanta a esta fase la transición que el acta
/// asocia al retiro físico de las 07:30 (F3, sin construir): es una simplificación consciente,
/// no hay otro actor todavía que la dispare.
/// </summary>
[ApiController]
[Route("api/rutas")]
[Authorize(Policy = "BackOffice")]
public class RutasController(LogisticaDbContext db) : ControllerBase
{
    public record RutaResumen(long Id, DateOnly Fecha, string? VehiculoPatente, string? RepartidorNombre, string Estado, int CantidadParadas);

    public record RutaDetalle(
        long Id, DateOnly Fecha, long? VehiculoId, string? VehiculoPatente, Guid? RepartidorId, string? RepartidorNombre,
        int CapacidadParadas, string Estado, int CantidadParadas, int? KmInicial, int? KmFinal,
        decimal? CombustibleMonto, decimal? PeajesMonto, decimal? OtrosCostos, decimal? PagoRepartidor,
        string? NotasCierre, DateTimeOffset? CerradaEn);

    public record CerrarRutaRequest(
        int KmInicial, int KmFinal, decimal CombustibleMonto, decimal PeajesMonto,
        decimal OtrosCostos, decimal PagoRepartidor, string? NotasCierre);

    public record ResultadoRuta(decimal Ingresos, decimal Costos, decimal Margen, int Efectivas, int Fallidas, int Reprogramadas);

    public record CrearRutaRequest(DateOnly Fecha);
    public record ActualizarRutaRequest(long? VehiculoId, Guid? RepartidorId, int CapacidadParadas);
    public record ParadaArmadoRequest(long UbicacionId, bool Anclada, List<long> PedidoIds);
    public record GuardarParadasRequest(List<ParadaArmadoRequest> Paradas);
    public record ParadaArmada(
        long UbicacionId, string CalleNumero, string? Localidad, decimal? Lat, decimal? Lng,
        bool Anclada, List<long> PedidoIds);

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] DateOnly? fecha, CancellationToken ct)
    {
        var query = db.Rutas.AsNoTracking().AsQueryable();
        if (fecha is not null) query = query.Where(r => r.Fecha == fecha.Value);

        var filas = await query
            .OrderByDescending(r => r.Fecha)
            .Select(r => new
            {
                r.Id,
                r.Fecha,
                VehiculoPatente = r.Vehiculo != null ? r.Vehiculo.Patente : null,
                RepartidorNombre = r.Repartidor != null ? r.Repartidor.Nombre : null,
                r.Estado,
                CantidadParadas = db.RutaParadas.Count(p => p.RutaId == r.Id),
            })
            .ToListAsync(ct);

        return Ok(filas.Select(r => new RutaResumen(r.Id, r.Fecha, r.VehiculoPatente, r.RepartidorNombre, r.Estado, r.CantidadParadas)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RutaDetalle(
                r.Id, r.Fecha, r.VehiculoId, r.Vehiculo != null ? r.Vehiculo.Patente : null,
                r.RepartidorId, r.Repartidor != null ? r.Repartidor.Nombre : null,
                r.CapacidadParadas, r.Estado, db.RutaParadas.Count(p => p.RutaId == r.Id),
                r.KmInicial, r.KmFinal, r.CombustibleMonto,
                r.PeajesMonto, r.OtrosCostos, r.PagoRepartidor, r.NotasCierre, r.CerradaEn))
            .SingleOrDefaultAsync(ct);

        return ruta is null ? NotFound() : Ok(ruta);
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearRutaRequest req, CancellationToken ct)
    {
        var ruta = new Ruta { Fecha = req.Fecha, CreadaEn = DateTimeOffset.UtcNow };
        db.Rutas.Add(ruta);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Detalle), new { id = ruta.Id }, new { ruta.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Actualizar(long id, ActualizarRutaRequest req, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado != "planificada") return Conflict("La ruta ya no está en planificación.");

        ruta.VehiculoId = req.VehiculoId;
        ruta.RepartidorId = req.RepartidorId;
        ruta.CapacidadParadas = req.CapacidadParadas;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Limpieza de un armado creado por error. Cascada ya configurada
    /// (RutaParadaConfiguration/ParadaPedidoConfiguration) se lleva paradas y filas puente.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Eliminar(long id, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado != "planificada")
            return Conflict("Solo se puede eliminar una ruta que todavía está en planificación.");

        db.Rutas.Remove(ruta);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Estado actual del armado (para reabrir /rutas/{id}/armar y seguir editando).</summary>
    [HttpGet("{id:long}/paradas")]
    public async Task<IActionResult> ListarParadas(long id, CancellationToken ct)
    {
        if (!await db.Rutas.AnyAsync(r => r.Id == id, ct)) return NotFound();

        var paradas = await db.RutaParadas.AsNoTracking()
            .Where(rp => rp.RutaId == id)
            .OrderBy(rp => rp.Orden)
            .Select(rp => new
            {
                rp.Id,
                rp.UbicacionId,
                rp.Ubicacion.CalleNumero,
                Localidad = rp.Ubicacion.Localidad != null ? rp.Ubicacion.Localidad.Nombre : null,
                rp.Ubicacion.Lat,
                rp.Ubicacion.Lng,
                rp.Anclada,
            })
            .ToListAsync(ct);

        var paradaIds = paradas.Select(p => p.Id).ToList();
        var pedidosPorParada = await db.ParadaPedidos.AsNoTracking()
            .Where(pp => paradaIds.Contains(pp.ParadaId))
            .Select(pp => new { pp.ParadaId, pp.PedidoId })
            .ToListAsync(ct);

        var resultado = paradas.Select(p => new ParadaArmada(
            p.UbicacionId, p.CalleNumero, p.Localidad, p.Lat, p.Lng, p.Anclada,
            pedidosPorParada.Where(pp => pp.ParadaId == p.Id).Select(pp => pp.PedidoId).ToList()));

        return Ok(resultado);
    }

    /// <summary>
    /// Reemplazo total de las paradas de la ruta (RF-11 a RF-14): más simple y más robusto que
    /// endpoints incrementales de agregar/quitar/reordenar que haya que mantener sincronizados
    /// con el estado de edición del cliente. Todo en una transacción explícita: si el trigger
    /// trg_bloquear_direccion_dudosa frena la segunda tanda de inserts (parada_pedidos), la
    /// primera (ruta_paradas) también se revierte — sin esto quedaría un armado a medio guardar.
    /// </summary>
    [HttpPut("{id:long}/paradas")]
    public async Task<IActionResult> GuardarParadas(long id, GuardarParadasRequest req, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado != "planificada") return Conflict("La ruta ya no está en planificación.");

        var pedidoIds = req.Paradas.SelectMany(p => p.PedidoIds).ToList();
        if (pedidoIds.Count != pedidoIds.Distinct().Count())
            return BadRequest("Un pedido no puede aparecer en más de una parada.");

        if (pedidoIds.Count > 0)
        {
            var pedidos = await db.Pedidos.Where(p => pedidoIds.Contains(p.Id)).ToListAsync(ct);
            if (pedidos.Count != pedidoIds.Count)
                return BadRequest("Alguno de los pedidos no existe.");
            if (pedidos.Any(p => p.Estado != EstadoPedido.Confirmado || p.FechaEntrega != ruta.Fecha))
                return BadRequest("Todos los pedidos deben estar confirmados y ser de la fecha de la ruta.");

            // Nada a nivel de base impide todavía que el mismo pedido termine en dos rutas: la PK
            // de parada_pedidos es compuesta (parada_id, pedido_id), no hay unique sobre pedido_id.
            var asignadosOtraRuta = await db.ParadaPedidos
                .Where(pp => pp.Parada.RutaId != id && pedidoIds.Contains(pp.PedidoId))
                .Select(pp => pp.PedidoId)
                .ToListAsync(ct);
            if (asignadosOtraRuta.Count > 0)
                return Conflict($"El/los pedido(s) {string.Join(", ", asignadosOtraRuta)} ya están asignados a otra ruta.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var paradasExistentes = await db.RutaParadas.Where(rp => rp.RutaId == id).ToListAsync(ct);
        db.RutaParadas.RemoveRange(paradasExistentes);

        var nuevasParadas = req.Paradas.Select((item, i) => new RutaParada
        {
            RutaId = id,
            UbicacionId = item.UbicacionId,
            Tipo = "entrega",
            Orden = i + 1,
            Anclada = item.Anclada,
        }).ToList();
        db.RutaParadas.AddRange(nuevasParadas);
        await db.SaveChangesAsync(ct); // aplica los deletes y genera el Id de las paradas nuevas

        for (var i = 0; i < req.Paradas.Count; i++)
            foreach (var pedidoId in req.Paradas[i].PedidoIds)
                db.ParadaPedidos.Add(new ParadaPedido { ParadaId = nuevasParadas[i].Id, PedidoId = pedidoId });

        await db.SaveChangesAsync(ct); // acá frena trg_bloquear_direccion_dudosa si corresponde
        await tx.CommitAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// RF-17: cierra el armado y lo hace disponible para el repartidor. Transiciona la ruta y,
    /// en la misma escritura, todos sus pedidos Confirmado -> EnRuta (un solo GuardarComoAsync:
    /// fn_log_estado_pedido corre una vez por fila, mismo actor y motivo para todas).
    /// </summary>
    [HttpPost("{id:long}/cerrar-planificacion")]
    public async Task<IActionResult> CerrarPlanificacion(long id, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado != "planificada") return Conflict("La ruta ya no está en planificación.");
        if (ruta.VehiculoId is null || ruta.RepartidorId is null)
            return BadRequest("Faltan vehículo y/o repartidor.");

        var pedidos = await db.Pedidos
            .Where(p => db.ParadaPedidos.Any(pp => pp.Parada.RutaId == id && pp.PedidoId == p.Id))
            .ToListAsync(ct);
        if (pedidos.Count == 0) return BadRequest("La ruta no tiene paradas.");
        if (pedidos.Any(p => !TransicionesPedido.Permitida(p.Estado, EstadoPedido.EnRuta)))
            return Conflict("Alguno de los pedidos de la ruta cambió de estado; volvé a armar la ruta.");

        ruta.Estado = "en_curso";
        foreach (var pedido in pedidos)
            pedido.Estado = EstadoPedido.EnRuta;

        await db.GuardarComoAsync(User.UsuarioId(), "Cierre de planificación de ruta.", ct);

        return NoContent();
    }

    [HttpGet("{id:long}/resultado")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Resultado(long id, CancellationToken ct)
    {
        if (!await db.Rutas.AnyAsync(r => r.Id == id, ct)) return NotFound();
        return Ok(await CalcularResultadoAsync(id, ct));
    }

    /// <summary>RF-26/27: costos reales de la jornada y resultado económico disponible el mismo
    /// día (criterio de aceptación 4 del acta).</summary>
    [HttpPost("{id:long}/cierre")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Cerrar(long id, CerrarRutaRequest req, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado == "cerrada") return Conflict("La ruta ya está cerrada.");
        if (ruta.Estado != "en_curso")
            return Conflict("La ruta todavía no cerró su planificación (RF-17); no se puede cerrar económicamente.");

        ruta.KmInicial = req.KmInicial;
        ruta.KmFinal = req.KmFinal;
        ruta.CombustibleMonto = req.CombustibleMonto;
        ruta.PeajesMonto = req.PeajesMonto;
        ruta.OtrosCostos = req.OtrosCostos;
        ruta.PagoRepartidor = req.PagoRepartidor;
        ruta.NotasCierre = req.NotasCierre;
        ruta.Estado = "cerrada";
        ruta.CerradaEn = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(await CalcularResultadoAsync(id, ct));
    }

    private async Task<ResultadoRuta> CalcularResultadoAsync(long rutaId, CancellationToken ct)
    {
        // Ingresos: pedidos de la ruta efectivamente entregados. parada_pedidos es la tabla
        // puente (RF-14: N retiros en la misma dirección = una sola parada).
        var pedidosDeLaRuta = db.ParadaPedidos
            .Where(pp => pp.Parada.RutaId == rutaId)
            .Select(pp => pp.Pedido);

        var ingresos = await pedidosDeLaRuta
            .Where(p => p.Estado == EstadoPedido.Entregado)
            .SumAsync(p => (decimal?)p.Total, ct) ?? 0m;

        var efectivas = await pedidosDeLaRuta.CountAsync(p => p.Estado == EstadoPedido.Entregado, ct);
        var fallidas = await pedidosDeLaRuta.CountAsync(p => p.Estado == EstadoPedido.Fallido, ct);
        var reprogramadas = await pedidosDeLaRuta.CountAsync(p => p.Estado == EstadoPedido.Reprogramado, ct);

        var ruta = await db.Rutas.AsNoTracking().SingleAsync(r => r.Id == rutaId, ct);
        var costos = (ruta.CombustibleMonto ?? 0) + (ruta.PeajesMonto ?? 0) + (ruta.OtrosCostos ?? 0) + (ruta.PagoRepartidor ?? 0);

        return new ResultadoRuta(ingresos, costos, ingresos - costos, efectivas, fallidas, reprogramadas);
    }
}
