using System.ComponentModel.DataAnnotations;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Servicios;
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
/// explícita que la pasa a 'en_curso' Y transiciona en bloque sus pedidos — desde acta changelog
/// 3.11, Borrador -> Confirmado (cotizando ahí, con el vehículo real ya conocido) y luego
/// Confirmado -> EnRuta (construccion_v1.md §5: "confirmado → en_ruta | Existe fila en
/// parada_pedidos") — recién ahí
/// la ruta aparece en /api/mis-paradas. Esto adelanta a esta fase la transición que el acta
/// asocia al retiro físico de las 07:30 (F3, sin construir): es una simplificación consciente,
/// no hay otro actor todavía que la dispare.
/// </summary>
[ApiController]
[Route("api/rutas")]
[Authorize(Policy = "BackOffice")]
public class RutasController(
    LogisticaDbContext db, OrigenRutaService origenes, PrecioService precios,
    DistanciaService distancias, JornadaService jornada) : ControllerBase
{
    public record RutaResumen(long Id, DateOnly Fecha, string? VehiculoPatente, string? RepartidorNombre, string Estado, int CantidadParadas);

    public record RutaDetalle(
        long Id, DateOnly Fecha, long? VehiculoId, string? VehiculoPatente, Guid? RepartidorId, string? RepartidorNombre,
        int CapacidadParadas, string Estado, int CantidadParadas, int? KmInicial, int? KmFinal,
        decimal? CombustibleMonto, decimal? PeajesMonto, decimal? OtrosCostos, decimal? PagoRepartidor,
        string? NotasCierre, DateTimeOffset? CerradaEn,
        long? OrigenUbicacionId, OrigenRuta? Origen);

    public record CerrarRutaRequest(
        [Range(0, int.MaxValue, ErrorMessage = "El km inicial no puede ser negativo.")] int KmInicial,
        [Range(0, int.MaxValue, ErrorMessage = "El km final no puede ser negativo.")] int KmFinal,
        [Range(0, double.MaxValue, ErrorMessage = "El combustible no puede ser negativo.")] decimal CombustibleMonto,
        [Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")] decimal PeajesMonto,
        [Range(0, double.MaxValue, ErrorMessage = "Otros costos no pueden ser negativos.")] decimal OtrosCostos,
        [Range(0, double.MaxValue, ErrorMessage = "El pago al repartidor no puede ser negativo.")] decimal PagoRepartidor,
        string? NotasCierre);

    public record ResultadoRuta(decimal Ingresos, decimal Costos, decimal Margen, int Efectivas, int Fallidas, int Reprogramadas);

    public record CrearRutaRequest(DateOnly Fecha);
    public record ActualizarRutaRequest(
        long? VehiculoId, Guid? RepartidorId,
        [Range(1, int.MaxValue, ErrorMessage = "La capacidad de paradas debe ser mayor a cero.")] int CapacidadParadas,
        long? OrigenUbicacionId);
    public record ParadaArmadoRequest(long UbicacionId, bool Anclada, List<long> PedidoIds);
    public record GuardarParadasRequest(List<ParadaArmadoRequest> Paradas);
    public record ParadaArmada(
        long Id, long UbicacionId, string CalleNumero, string? Localidad, decimal? Lat, decimal? Lng,
        bool Anclada, int Orden, string Estado, DateTimeOffset? LlegadaEn, DateTimeOffset? SalidaEn,
        List<long> PedidoIds);

    public record ReasignarRepartidorRequest(Guid RepartidorId);
    public record ReordenarParadasRequest(List<long> ParadaIds);

    /// <summary>
    /// Listado paginado y filtrable (RF-10 y ss. — mismo patrón que PedidosController.Listar).
    /// `pagina`/`tamanioPagina` opcionales: sin ellos devuelve todo sin recortar. Orden por
    /// defecto: fecha descendente con `id` descendente como desempate (antes el desempate entre
    /// rutas de la misma fecha quedaba librado al orden físico de la tabla).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? fecha,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta,
        [FromQuery] string? estado,
        [FromQuery] string? q,
        [FromQuery] Guid? repartidorId,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanioPagina,
        CancellationToken ct)
    {
        var query = db.Rutas.AsNoTracking().AsQueryable();

        if (fecha is not null) query = query.Where(r => r.Fecha == fecha.Value);
        if (fechaDesde is not null) query = query.Where(r => r.Fecha >= fechaDesde.Value);
        if (fechaHasta is not null) query = query.Where(r => r.Fecha <= fechaHasta.Value);
        if (!string.IsNullOrWhiteSpace(estado)) query = query.Where(r => r.Estado == estado);
        if (repartidorId is not null) query = query.Where(r => r.RepartidorId == repartidorId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var texto = q.Trim();
            query = int.TryParse(texto, out var idBuscado)
                ? query.Where(r => r.Id == idBuscado
                    || (r.Vehiculo != null && r.Vehiculo.Patente.ToLower().Contains(texto.ToLower()))
                    || (r.Repartidor != null && r.Repartidor.Nombre.ToLower().Contains(texto.ToLower())))
                : query.Where(r => (r.Vehiculo != null && r.Vehiculo.Patente.ToLower().Contains(texto.ToLower()))
                    || (r.Repartidor != null && r.Repartidor.Nombre.ToLower().Contains(texto.ToLower())));
        }

        var total = await query.CountAsync(ct);

        query = orden switch
        {
            "fecha" => query.OrderBy(r => r.Fecha).ThenBy(r => r.Id),
            "-fecha" => query.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id),
            "id" => query.OrderBy(r => r.Id),
            "-id" => query.OrderByDescending(r => r.Id),
            "estado" => query.OrderBy(r => r.Estado).ThenByDescending(r => r.Id),
            "-estado" => query.OrderByDescending(r => r.Estado).ThenByDescending(r => r.Id),
            "paradas" => query.OrderBy(r => db.RutaParadas.Count(p => p.RutaId == r.Id)).ThenByDescending(r => r.Id),
            "-paradas" => query.OrderByDescending(r => db.RutaParadas.Count(p => p.RutaId == r.Id)).ThenByDescending(r => r.Id),
            _ => query.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id),
        };

        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? pagina.Value : 1;
            query = query.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value);
        }

        var filas = await query
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

        var resultado = filas.Select(r => new RutaResumen(r.Id, r.Fecha, r.VehiculoPatente, r.RepartidorNombre, r.Estado, r.CantidadParadas)).ToList();

        return Ok(new ListaPaginada<RutaResumen>(resultado, total));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking()
            .Include(r => r.Vehiculo)
            .Include(r => r.Repartidor)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();

        var cantidadParadas = await db.RutaParadas.CountAsync(p => p.RutaId == id, ct);
        var origen = await origenes.ResolverAsync(ruta, ct);

        return Ok(new RutaDetalle(
            ruta.Id, ruta.Fecha, ruta.VehiculoId, ruta.Vehiculo?.Patente,
            ruta.RepartidorId, ruta.Repartidor?.Nombre,
            ruta.CapacidadParadas, ruta.Estado, cantidadParadas, ruta.KmInicial, ruta.KmFinal,
            ruta.CombustibleMonto, ruta.PeajesMonto, ruta.OtrosCostos, ruta.PagoRepartidor,
            ruta.NotasCierre, ruta.CerradaEn, ruta.OrigenUbicacionId, origen));
    }

    /// <summary>Bundle de paradas con estado/horarios/pedidos para el detalle de ruta del
    /// back-office (jornada §9.3) — funciona en los tres estados, a diferencia de
    /// ListarParadas (pensado solo para reabrir el armado). Mismo motor que
    /// /api/mis-paradas/dia (JornadaService), sin la config propia del repartidor.</summary>
    [HttpGet("{id:long}/jornada")]
    public async Task<IActionResult> Jornada(long id, CancellationToken ct)
    {
        var bundle = await jornada.ArmarAsync(id, ct);
        if (bundle is null) return NotFound();
        return Ok(bundle);
    }

    /// <summary>Reasignar repartidor sin volver a armar la ruta — plan de contingencia por
    /// ausencia del repartidor (acta §11.2-5, todavía abierta como decisión de negocio; esto
    /// da la herramienta, no la resuelve). Permitido con la ruta ya en_curso a propósito: no
    /// toca el vehículo (que define la tarifa ya congelada, acta 3.11) ni el precio.</summary>
    [HttpPut("{id:long}/repartidor")]
    public async Task<IActionResult> ReasignarRepartidor(long id, ReasignarRepartidorRequest req, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado == "cerrada") return Conflict("La ruta ya está cerrada.");

        var repartidor = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == req.RepartidorId, ct);
        if (repartidor is null || repartidor.Rol != Roles.Repartidor || !repartidor.Activo)
            return BadRequest("El usuario elegido no es un repartidor activo.");

        // MisParadasController.Dia toma la primera ruta en_curso del repartidor (FirstOrDefault):
        // una segunda no daría error, le escondería una de las dos en silencio.
        var yaTieneRutaEnCurso = await db.Rutas.AnyAsync(
            r => r.Id != id && r.RepartidorId == req.RepartidorId && r.Estado == "en_curso", ct);
        if (yaTieneRutaEnCurso)
            return BadRequest("Ese repartidor ya tiene otra ruta en curso.");

        ruta.RepartidorId = req.RepartidorId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reordena las paradas todavía `pendiente` de una ruta ya en curso (RF-12: el
    /// reordenamiento manual está siempre disponible). Las `completada`/`fallida` conservan su
    /// `Orden` — se reasigna la secuencia recibida sobre las posiciones que hoy ocupan las
    /// pendientes, no sobre 1..N. Misma transacción y mismo motivo que GuardarParadas: el unique
    /// (ruta_id, orden) es DEFERRABLE justamente para esto.</summary>
    [HttpPut("{id:long}/paradas/orden")]
    public async Task<IActionResult> ReordenarParadas(long id, ReordenarParadasRequest req, CancellationToken ct)
    {
        var ruta = await db.Rutas.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado == "cerrada") return Conflict("La ruta ya está cerrada.");

        var pendientes = await db.RutaParadas
            .Where(rp => rp.RutaId == id && rp.Estado == "pendiente")
            .ToListAsync(ct);

        var idsRecibidos = req.ParadaIds;
        if (idsRecibidos.Count != idsRecibidos.Distinct().Count())
            return BadRequest("Una parada no puede aparecer más de una vez.");
        if (!idsRecibidos.ToHashSet().SetEquals(pendientes.Select(p => p.Id)))
            return BadRequest("La lista debe contener exactamente las paradas pendientes de esta ruta, sin repetidos ni faltantes.");

        // Las posiciones que hoy ocupan las pendientes (pueden estar intercaladas con
        // completadas/fallidas, p. ej. una fallida en el orden 5 con la 4 todavía pendiente):
        // se conservan esas posiciones, la secuencia recibida se vuelca sobre ellas en orden.
        var posiciones = pendientes.Select(p => p.Orden).OrderBy(o => o).ToList();
        var porId = pendientes.ToDictionary(p => p.Id);

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            for (var i = 0; i < idsRecibidos.Count; i++)
                porId[idsRecibidos[i]].Orden = posiciones[i];
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return NoContent();
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

        // null = depósito (OrigenRutaService); con valor, tiene que existir: una FK rota acá se
        // manifestaría recién al abrir la jornada del repartidor. No se exige ubicacion_apta —
        // igual que el alta de pedido, una dirección dudosa no bloquea el guardado.
        if (req.OrigenUbicacionId is { } origenId && !await db.Ubicaciones.AnyAsync(u => u.Id == origenId, ct))
            return BadRequest("La ubicación de partida no existe.");

        ruta.VehiculoId = req.VehiculoId;
        ruta.RepartidorId = req.RepartidorId;
        ruta.CapacidadParadas = req.CapacidadParadas;
        ruta.OrigenUbicacionId = req.OrigenUbicacionId;
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
                rp.Orden,
                rp.Estado,
                rp.LlegadaEn,
                rp.SalidaEn,
            })
            .ToListAsync(ct);

        var paradaIds = paradas.Select(p => p.Id).ToList();
        var pedidosPorParada = await db.ParadaPedidos.AsNoTracking()
            .Where(pp => paradaIds.Contains(pp.ParadaId))
            .Select(pp => new { pp.ParadaId, pp.PedidoId })
            .ToListAsync(ct);

        var resultado = paradas.Select(p => new ParadaArmada(
            p.Id, p.UbicacionId, p.CalleNumero, p.Localidad, p.Lat, p.Lng, p.Anclada,
            p.Orden, p.Estado, p.LlegadaEn, p.SalidaEn,
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
            // Acta changelog 3.11: el precio (y Confirmado) recién se fija en CerrarPlanificacion,
            // cuando se conoce el vehículo — un pedido llega acá siempre en Borrador.
            if (pedidos.Any(p => p.Estado != EstadoPedido.Borrador || p.FechaEntrega != ruta.Fecha))
                return BadRequest("Todos los pedidos deben estar en borrador (sin rutear todavía) y ser de la fecha de la ruta.");

            // Nada a nivel de base impide todavía que el mismo pedido termine en dos rutas: la PK
            // de parada_pedidos es compuesta (parada_id, pedido_id), no hay unique sobre pedido_id.
            var asignadosOtraRuta = await db.ParadaPedidos
                .Where(pp => pp.Parada.RutaId != id && pedidoIds.Contains(pp.PedidoId))
                .Select(pp => pp.PedidoId)
                .ToListAsync(ct);
            if (asignadosOtraRuta.Count > 0)
                return Conflict($"El/los pedido(s) {string.Join(", ", asignadosOtraRuta)} ya están asignados a otra ruta.");
        }

        // CreateExecutionStrategy().ExecuteAsync envuelve la transacción manual, exigido por
        // EnableRetryOnFailure (RegistroDatos.cs). El delegate se reejecuta entero en cada
        // reintento: paradasExistentes/nuevasParadas se consultan y arman DENTRO a propósito,
        // para que cada intento parta de cero (nada tracked de un intento fallido anterior).
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
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
        });

        return NoContent();
    }

    /// <summary>
    /// RF-17: cierra el armado y lo hace disponible para el repartidor. Desde acta changelog
    /// 3.11, este es también el momento en que se cotiza y confirma cada pedido de la ruta que
    /// todavía esté en Borrador: recién acá se conoce el vehículo real (camioneta o moto), y el
    /// precio depende de eso. Dos SaveChangesAsync bajo la misma transacción y el mismo actor
    /// (PublicarActorAsync, no dos GuardarComoAsync — esto abriría dos transacciones separadas y
    /// dejaría una ventana de fallo parcial): el primero fija precio y pasa Borrador -> Confirmado,
    /// el segundo pasa todo a EnRuta — así el historial (RF-28, "sin huecos") registra ambas
    /// transiciones por separado en vez de saltar directo de Borrador a EnRuta.
    /// </summary>
    [HttpPost("{id:long}/cerrar-planificacion")]
    public async Task<IActionResult> CerrarPlanificacion(long id, CancellationToken ct)
    {
        var ruta = await db.Rutas.Include(r => r.Vehiculo).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (ruta is null) return NotFound();
        if (ruta.Estado != "planificada") return Conflict("La ruta ya no está en planificación.");
        if (ruta.VehiculoId is null || ruta.RepartidorId is null)
            return BadRequest("Faltan vehículo y/o repartidor.");
        // Acta changelog 3.8: ya no hay un depósito único que resolver por default — el
        // planificador siempre elige el punto de partida a mano. Esto garantiza que ninguna ruta
        // llegue a "en_curso" (y por lo tanto a Cerrar) sin origen resuelto.
        if (ruta.OrigenUbicacionId is null)
            return BadRequest("Falta elegir el punto de partida.");

        var pedidos = await db.Pedidos
            .Include(p => p.OrigenUbicacion)
            .Include(p => p.DestinoUbicacion)
            .Where(p => db.ParadaPedidos.Any(pp => pp.Parada.RutaId == id && pp.PedidoId == p.Id))
            .ToListAsync(ct);
        if (pedidos.Count == 0) return BadRequest("La ruta no tiene paradas.");
        if (pedidos.Any(p => p.Estado != EstadoPedido.Borrador && p.Estado != EstadoPedido.Confirmado))
            return Conflict("Alguno de los pedidos de la ruta cambió de estado; volvé a armar la ruta.");

        var tipoVehiculo = ruta.Vehiculo!.Tipo;
        var pedidosACotizar = pedidos.Where(p => p.Estado == EstadoPedido.Borrador).ToList();

        // Cotizar todo ANTES de escribir nada: si un pedido no tiene tarifa cargada para su zona
        // en este tipo de vehículo, la ruta entera se rechaza sin tocar la base — no puede quedar
        // una ruta a medio confirmar.
        var desglosesPorPedido = new Dictionary<long, DesglosePrecio>();
        foreach (var pedido in pedidosACotizar)
        {
            if (pedido.ZonaId is null)
                return BadRequest($"El pedido {pedido.Id} no tiene zona resuelta; no se puede cotizar.");
            try
            {
                var distancia = await distancias.ResolverAsync(
                    pedido.OrigenUbicacion.Lat, pedido.OrigenUbicacion.Lng,
                    pedido.DestinoUbicacion.Lat, pedido.DestinoUbicacion.Lng,
                    pedido.KmManual, ct);
                desglosesPorPedido[pedido.Id] = await precios.CotizarAsync(
                    pedido.ClienteId, pedido.ZonaId.Value, pedido.FechaEntrega, pedido.Urgente,
                    pedido.Peajes, descuentoRuta: false, tipoVehiculo, pedido.PrecioManual, distancia, ct);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest($"Pedido {pedido.Id}: {ex.Message}");
            }
        }

        // Igual que GuardarParadas: execution strategy exigido por EnableRetryOnFailure. Acá no
        // hay entidades NUEVAS (Add) dentro del delegate, solo reasignación de propiedades a
        // valores fijos (PrecioBase, Estado, ...) sobre `ruta`/`pedidos` ya cargados afuera — es
        // idempotente aunque el delegate se reejecute: asignar el mismo valor dos veces no
        // duplica nada.
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.PublicarActorAsync(User.UsuarioId(), "Cierre de planificación de ruta.", ct);

            foreach (var pedido in pedidosACotizar)
            {
                var desglose = desglosesPorPedido[pedido.Id];
                pedido.PrecioBase = desglose.PrecioBase;
                pedido.RecargoKm = desglose.RecargoKm;
                pedido.KmCobrados = desglose.KmCobrados;
                pedido.KmFuente = desglose.KmFuente;
                pedido.RecargoUrgencia = desglose.RecargoUrgencia;
                pedido.DescuentoRuta = desglose.DescuentoRuta;
                pedido.Total = desglose.Total;
                pedido.PrecioCongeladoEn = DateTimeOffset.UtcNow;
                pedido.Estado = EstadoPedido.Confirmado;
            }
            await db.SaveChangesAsync(ct);

            ruta.Estado = "en_curso";
            foreach (var pedido in pedidos)
                pedido.Estado = EstadoPedido.EnRuta;
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

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
        // Regla cruzada, no expresable con [Range]: el km final no puede ser anterior al inicial.
        if (req.KmFinal < req.KmInicial)
            return BadRequest("El km final no puede ser menor al km inicial.");

        // Sin freeze que hacer acá: CerrarPlanificacion (RF-17) ya exige el origen elegido antes
        // de pasar a en_curso (acta changelog 3.8), así que si esta ruta llegó hasta acá, su
        // origen_ubicacion_id ya es un id concreto — nunca null.

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

        // Antes eran 5 round-trips en serie (ingresos, 3 conteos, la ruta). Los 4 agregados de
        // pedidos y los costos de la ruta van como subconsultas correlacionadas de una sola
        // proyección anclada en la ruta — EF Core las traduce a una única sentencia SQL.
        var resultado = await db.Rutas.AsNoTracking()
            .Where(r => r.Id == rutaId)
            .Select(r => new
            {
                Ingresos = pedidosDeLaRuta.Where(p => p.Estado == EstadoPedido.Entregado).Sum(p => (decimal?)p.Total) ?? 0m,
                Efectivas = pedidosDeLaRuta.Count(p => p.Estado == EstadoPedido.Entregado),
                Fallidas = pedidosDeLaRuta.Count(p => p.Estado == EstadoPedido.Fallido),
                Reprogramadas = pedidosDeLaRuta.Count(p => p.Estado == EstadoPedido.Reprogramado),
                Costos = (r.CombustibleMonto ?? 0) + (r.PeajesMonto ?? 0) + (r.OtrosCostos ?? 0) + (r.PagoRepartidor ?? 0),
            })
            .SingleAsync(ct);

        return new ResultadoRuta(
            resultado.Ingresos, resultado.Costos, resultado.Ingresos - resultado.Costos,
            resultado.Efectivas, resultado.Fallidas, resultado.Reprogramadas);
    }
}
