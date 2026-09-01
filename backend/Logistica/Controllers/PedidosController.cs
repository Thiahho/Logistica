using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Controllers;

/// <summary>
/// BackOffice ve todos los pedidos; 'cliente' solo los suyos, filtrado por el claim cliente_id.
/// El repartidor no entra acá (403): consulta v_paradas_repartidor vía /api/mis-paradas, que
/// nunca trae importes.
/// </summary>
[ApiController]
[Route("api/pedidos")]
[Authorize(Roles = "administracion,operacion,cliente")]
public class PedidosController(
    LogisticaDbContext db, PrecioService precios, IOptions<OpcionesPruebaEntrega> opcionesPruebaEntrega,
    OrigenRutaService origenes) : ControllerBase
{
    public record PedidoResumen(
        long Id, string DestinatarioNombre, string Estado, decimal? Total,
        DateOnly FechaEntrega, int ClienteId, string ClienteRazonSocial, bool DireccionDudosa);


    public record CotizarRequest(int ClienteId, int LocalidadId, DateOnly FechaEntrega, bool Urgente, decimal Peajes = 0m);

    /// <summary>Estimado informativo (acta changelog 3.11): null en un tipo = todavía no se cargó
    /// tarifa para esa zona en ese tipo de vehículo, no un error.</summary>
    public record CotizacionEstimada(DesglosePrecio? Camioneta, DesglosePrecio? Moto);

    public record CrearPedidoRequest(
        int ClienteId,
        string? ReferenciaCliente,
        string DestinatarioNombre,
        string DestinatarioTelefono,
        long DestinoUbicacionId,
        int Bultos,
        decimal? PesoKg,
        decimal? ValorDeclarado,
        DateOnly FechaEntrega,
        bool Urgente,
        decimal Peajes,
        string? Observaciones);

    public record HistorialEvento(
        long Id, string? EstadoAnterior, string EstadoNuevo, string? Motivo,
        string ActorTipo, string? ActorNombre, DateTimeOffset OcurridoEn);

    public record PedidoDetalle(
        long Id, int ClienteId, string ClienteRazonSocial, string? ReferenciaCliente,
        string Tipo, long? PedidoOrigenId,
        string DestinoCalleNumero, string? DestinoLocalidad,
        string DestinatarioNombre, string DestinatarioTelefono,
        int Bultos, decimal? PesoKg, decimal? ValorDeclarado,
        DateOnly FechaEntrega, bool Urgente,
        decimal? PrecioBase, decimal? RecargoUrgencia, decimal? DescuentoRuta, decimal Peajes, decimal? Total,
        DateTimeOffset? PrecioCongeladoEn,
        string Estado, string OrigenCarga, string? Observaciones, DateTimeOffset CreadoEn,
        bool DireccionDudosa, List<HistorialEvento> Historial);

    public record CambiarEstadoRequest(string EstadoNuevo, string? Motivo, DateOnly? NuevaFechaEntrega);

    public record PruebaEntregaResumen(
        long Id, string Resultado, string? MotivoFallo, string? ReceptorNombre, bool IdentidadVerificada,
        bool TieneFoto, decimal? Lat, decimal? Lng, int? DesvioMetros, bool DesvioAlto,
        DateTimeOffset CapturadaEn, DateTimeOffset SincronizadaEn);

    public record CandidatoRuta(
        long PedidoId, string ClienteRazonSocial, string DestinatarioNombre, int Bultos, bool Urgente,
        string? ZonaCodigo, long DestinoUbicacionId, string DestinoCalleNumero, string? DestinoLocalidad,
        decimal? Lat, decimal? Lng, bool DireccionApta, bool YaEnEstaRuta);

    /// <summary>Destinatario ya usado por este cliente, con la dirección que se le entregó. Los
    /// campos de ubicación tienen el mismo shape que UbicacionesController.UbicacionResuelta: el
    /// alta reusa el id sin volver a llamar a POST /api/ubicaciones.</summary>
    public record DestinatarioFrecuente(
        string DestinatarioNombre, string DestinatarioTelefono,
        long DestinoUbicacionId, string DestinoCalleNumero,
        int LocalidadId, string LocalidadNombre,
        decimal? Lat, decimal? Lng, string? GeoConfianza,
        int Veces, DateOnly UltimaFechaEntrega);

    /// <summary>
    /// Listado paginado y filtrable (RF-10 y ss.). `pagina`/`tamanioPagina` son opcionales — sin
    /// ellos devuelve todo sin recortar, para no romper /mis-envios (rol 'cliente', que hoy pide
    /// GET /api/pedidos sin ningún query param y espera su historial completo).
    /// Orden por defecto: fecha de entrega descendente, con `id` descendente como desempate — antes
    /// el desempate quedaba librado al orden físico de la tabla, que no es estable ni predecible.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? fecha,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta,
        [FromQuery] string? estado,
        [FromQuery] int? clienteId,
        [FromQuery] string? q,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanioPagina,
        CancellationToken ct)
    {
        var query = db.Pedidos.AsNoTracking().AsQueryable();

        // El claim de cliente (rol 'cliente') manda siempre; ?clienteId= es para que
        // administración/operación filtren la lista general por cliente, no para que un cliente
        // vea pedidos ajenos.
        var clienteIdClaim = User.ClienteId();
        if (clienteIdClaim is not null) query = query.Where(p => p.ClienteId == clienteIdClaim);
        else if (clienteId is not null) query = query.Where(p => p.ClienteId == clienteId.Value);

        if (fecha is not null) query = query.Where(p => p.FechaEntrega == fecha.Value);
        if (fechaDesde is not null) query = query.Where(p => p.FechaEntrega >= fechaDesde.Value);
        if (fechaHasta is not null) query = query.Where(p => p.FechaEntrega <= fechaHasta.Value);

        if (estado is not null)
        {
            if (!Enum.TryParse<EstadoPedido>(estado, ignoreCase: true, out var estadoParsed))
                return BadRequest("Estado inválido.");
            query = query.Where(p => p.Estado == estadoParsed);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var texto = q.Trim();
            query = int.TryParse(texto, out var idBuscado)
                ? query.Where(p => p.Id == idBuscado || p.DestinatarioNombre.ToLower().Contains(texto.ToLower()))
                : query.Where(p => p.DestinatarioNombre.ToLower().Contains(texto.ToLower()));
        }

        var total = await query.CountAsync(ct);

        query = orden switch
        {
            "fecha" => query.OrderBy(p => p.FechaEntrega).ThenBy(p => p.Id),
            "-fecha" => query.OrderByDescending(p => p.FechaEntrega).ThenByDescending(p => p.Id),
            "id" => query.OrderBy(p => p.Id),
            "-id" => query.OrderByDescending(p => p.Id),
            "total" => query.OrderBy(p => p.Total).ThenByDescending(p => p.Id),
            "-total" => query.OrderByDescending(p => p.Total).ThenByDescending(p => p.Id),
            "estado" => query.OrderBy(p => p.Estado).ThenByDescending(p => p.Id),
            "-estado" => query.OrderByDescending(p => p.Estado).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.FechaEntrega).ThenByDescending(p => p.Id),
        };

        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? pagina.Value : 1;
            query = query.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value);
        }

        // DireccionDudosa reusa ubicacion_apta (registrada como DbFunction en LogisticaDbContext)
        // en vez de reimplementar el criterio en C# — construccion_v1.md §3 regla 3.
        var filas = await query
            .Select(p => new
            {
                p.Id,
                p.DestinatarioNombre,
                p.Estado,
                p.Total,
                p.FechaEntrega,
                p.ClienteId,
                ClienteRazonSocial = p.Cliente.RazonSocial,
                DireccionDudosa = !LogisticaDbContext.UbicacionApta(p.DestinoUbicacionId),
            })
            .ToListAsync(ct);

        var resultado = filas.Select(p => new PedidoResumen(
            p.Id, p.DestinatarioNombre, p.Estado.ToString(), p.Total, p.FechaEntrega,
            p.ClienteId, p.ClienteRazonSocial, p.DireccionDudosa)).ToList();

        return Ok(new ListaPaginada<PedidoResumen>(resultado, total));
    }

    /// <summary>
    /// Pedidos de una fecha todavía en Borrador, disponibles para armar una ruta (RF-10,
    /// agrupables por ZonaCodigo en el cliente; RF-14, se consolidan por DestinoUbicacionId en
    /// el cliente). Ya no son "confirmados" (acta changelog 3.11): confirmar es cotizar, y eso
    /// recién pasa cuando la ruta cierra su planificación y se conoce el vehículo.
    /// Si rutaId viene, incluye también los ya asignados a esa ruta (para re-editar un armado
    /// existente), marcados YaEnEstaRuta.
    /// </summary>
    [HttpGet("candidatos-ruta")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> CandidatosRuta([FromQuery] DateOnly fecha, [FromQuery] long? rutaId, CancellationToken ct)
    {
        // Un pedido asignado a OTRA ruta no es candidato: nada a nivel de base lo impide todavía
        // (la PK de parada_pedidos es compuesta (parada_id, pedido_id), no hay unique sobre
        // pedido_id solo) — RutasController valida esto de nuevo al guardar (double-booking).
        var asignadosOtraRuta = db.ParadaPedidos
            .Where(pp => rutaId == null || pp.Parada.RutaId != rutaId)
            .Select(pp => pp.PedidoId);

        var idsEnEstaRuta = rutaId is null
            ? []
            : (await db.ParadaPedidos.Where(pp => pp.Parada.RutaId == rutaId)
                .Select(pp => pp.PedidoId).ToListAsync(ct)).ToHashSet();

        var filas = await db.Pedidos.AsNoTracking()
            // Acta changelog 3.11: el precio (y por lo tanto Confirmado) recién se fija cuando la
            // ruta cierra su planificación y se conoce el vehículo — el pool de candidatos a
            // rutear es ahora el de pedidos en Borrador, no Confirmado.
            .Where(p => p.FechaEntrega == fecha && p.Estado == EstadoPedido.Borrador)
            .Where(p => !asignadosOtraRuta.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                ClienteRazonSocial = p.Cliente.RazonSocial,
                p.DestinatarioNombre,
                p.Bultos,
                p.Urgente,
                ZonaCodigo = p.Zona != null ? p.Zona.Codigo : null,
                p.DestinoUbicacionId,
                p.DestinoUbicacion.CalleNumero,
                DestinoLocalidad = p.DestinoUbicacion.Localidad != null ? p.DestinoUbicacion.Localidad.Nombre : null,
                p.DestinoUbicacion.Lat,
                p.DestinoUbicacion.Lng,
                DireccionApta = LogisticaDbContext.UbicacionApta(p.DestinoUbicacionId),
            })
            .ToListAsync(ct);

        var resultado = filas.Select(p => new CandidatoRuta(
            p.Id, p.ClienteRazonSocial, p.DestinatarioNombre, p.Bultos, p.Urgente, p.ZonaCodigo,
            p.DestinoUbicacionId, p.CalleNumero, p.DestinoLocalidad, p.Lat, p.Lng, p.DireccionApta,
            idsEnEstaRuta.Contains(p.Id)));

        return Ok(resultado);
    }

    /// <summary>
    /// Destinatarios ya usados por este cliente, con su dirección ya geocodificada, para
    /// autocompletar el alta (acta changelog 3.5). Capa de lectura sobre pedidos existentes —
    /// no persiste nada nuevo, no crea una entidad Destinatario.
    ///
    /// Excluye tipo != "entrega" a propósito: CambiarEstado genera pedidos "retorno" que copian
    /// el destinatario pero apuntan al DEPÓSITO como destino (OrigenUbicacionId del pedido
    /// original). Sin este filtro, la sugerencia ofrecería "destinatario → depósito" y elegirla
    /// crearía, sin fricción, un pedido dirigido a la propia empresa.
    /// </summary>
    [HttpGet("destinatarios-frecuentes")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> DestinatariosFrecuentes(
        [FromQuery] int clienteId, [FromQuery] string? q, [FromQuery] int limite, CancellationToken ct)
    {
        if (clienteId <= 0) return Ok(Array.Empty<DestinatarioFrecuente>());
        var tope = limite is > 0 and <= 50 ? limite : 20;
        var texto = q?.Trim().ToLower();

        var grupos = await db.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .Where(p => p.Tipo == "entrega")
            .Where(p => p.Estado != EstadoPedido.Cancelado)
            // Sin localidad no se puede cotizar: la sugerencia no serviría para completar el alta.
            .Where(p => p.DestinoUbicacion.LocalidadId != null)
            .Where(p => texto == null || p.DestinatarioNombre.ToLower().Contains(texto))
            .GroupBy(p => new
            {
                p.DestinatarioNombre,
                p.DestinatarioTelefono,
                p.DestinoUbicacionId,
                p.DestinoUbicacion.CalleNumero,
                LocalidadId = p.DestinoUbicacion.LocalidadId!.Value,
                LocalidadNombre = p.DestinoUbicacion.Localidad!.Nombre,
                p.DestinoUbicacion.Lat,
                p.DestinoUbicacion.Lng,
                p.DestinoUbicacion.GeoConfianza,
            })
            .Select(g => new
            {
                g.Key,
                Veces = g.Count(),
                UltimaFechaEntrega = g.Max(p => p.FechaEntrega),
            })
            // Reciente primero, frecuencia como desempate: el operador que repite la entrega de
            // ayer la quiere arriba, no la dirección que más veces se usó hace seis meses.
            .OrderByDescending(x => x.UltimaFechaEntrega)
            .ThenByDescending(x => x.Veces)
            .Take(tope)
            .ToListAsync(ct);

        var resultado = grupos.Select(g => new DestinatarioFrecuente(
            g.Key.DestinatarioNombre, g.Key.DestinatarioTelefono, g.Key.DestinoUbicacionId,
            g.Key.CalleNumero, g.Key.LocalidadId, g.Key.LocalidadNombre,
            g.Key.Lat, g.Key.Lng, g.Key.GeoConfianza, g.Veces, g.UltimaFechaEntrega));

        return Ok(resultado);
    }

    /// <summary>
    /// Estimado en vivo mientras se completa el alta (construccion_v1.md §4.1) — nunca se
    /// persiste. Deja de ser "el precio": desde acta changelog 3.11 el precio real depende del
    /// tipo de vehículo que termine llevando el pedido, que recién se conoce al armar la ruta,
    /// así que acá se cotizan los dos tipos a la vez para mostrar un rango. Si a alguno todavía
    /// no se le cargó tarifa para esa zona, esa mitad viene null — no es un error del alta.
    /// </summary>
    [HttpPost("cotizar")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Cotizar(CotizarRequest req, CancellationToken ct)
    {
        var zonaId = await db.Localidades.Where(l => l.Id == req.LocalidadId).Select(l => l.ZonaId).SingleOrDefaultAsync(ct);
        if (zonaId is null)
            return BadRequest("La localidad no tiene zona asignada; no se puede cotizar.");

        async Task<DesglosePrecio?> Intentar(string tipoVehiculo)
        {
            try
            {
                return await precios.CotizarAsync(
                    req.ClienteId, zonaId.Value, req.FechaEntrega, req.Urgente, req.Peajes, descuentoRuta: false, tipoVehiculo, ct);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return Ok(new CotizacionEstimada(await Intentar("camioneta"), await Intentar("moto")));
    }

    /// <summary>
    /// Alta de pedido. El body no trae total ni precio_base (regla §3.1: el precio se calcula en
    /// el servidor). Desde acta changelog 3.11 el precio depende del tipo de vehículo (camioneta o
    /// moto), que recién se conoce cuando la ruta que lo lleva cierra su planificación — el pedido
    /// nace en 'borrador', sin precio, y `RutasController.CerrarPlanificacion` es quien lo cotiza
    /// y confirma. Una dirección sin geolocalizar no bloquea el alta —queda marcada y
    /// trg_bloquear_direccion_dudosa la frena recién al rutear.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Crear(CrearPedidoRequest req, CancellationToken ct)
    {
        var destino = await db.Ubicaciones.Include(u => u.Localidad)
            .SingleOrDefaultAsync(u => u.Id == req.DestinoUbicacionId, ct);
        if (destino is null) return BadRequest("La ubicación de destino no existe.");

        var zonaId = destino.Localidad?.ZonaId;
        if (zonaId is null)
            return BadRequest("La localidad de destino no tiene zona asignada; no se puede cotizar.");

        // Alta de pedido no elige depósito explícito todavía (Fase 2 del retiro por pedido, sin
        // diseñar) — usa el más antiguo del catálogo como origen por defecto.
        var origen = await origenes.PrincipalParaPedidosAsync(ct);

        var pedido = new Pedido
        {
            ClienteId = req.ClienteId,
            ReferenciaCliente = req.ReferenciaCliente,
            OrigenUbicacionId = origen.UbicacionId,
            DestinoUbicacionId = destino.Id,
            // Trim: sin esto, "Juan Prueba" y "Juan Prueba " (un espacio de más al tipear)
            // aparecen como dos destinatarios distintos en /api/pedidos/destinatarios-frecuentes.
            DestinatarioNombre = req.DestinatarioNombre.Trim(),
            DestinatarioTelefono = req.DestinatarioTelefono.Trim(),
            Bultos = req.Bultos,
            PesoKg = req.PesoKg,
            ValorDeclarado = req.ValorDeclarado,
            FechaEntrega = req.FechaEntrega,
            Urgente = req.Urgente,
            ZonaId = zonaId,
            // Peajes es la excepción entre los campos de precio: el cliente ya lo conoce al
            // cargar el pedido (no depende del tipo de vehículo), así que se guarda de una — el
            // resto (precio_base, recargo, descuento, total, congelado_en) queda null hasta que
            // CerrarPlanificacion (RutasController) los fije junto con el tipo de vehículo real.
            Peajes = req.Peajes,
            Estado = EstadoPedido.Borrador,
            Observaciones = req.Observaciones,
            CreadoEn = DateTimeOffset.UtcNow,
        };

        db.Pedidos.Add(pedido);
        await db.GuardarComoAsync(User.UsuarioId(), ct: ct);

        var clienteRazonSocial = await db.Clientes.Where(c => c.Id == req.ClienteId)
            .Select(c => c.RazonSocial).SingleAsync(ct);

        return CreatedAtAction(nameof(Listar), new { }, new PedidoResumen(
            pedido.Id, pedido.DestinatarioNombre, pedido.Estado.ToString(), pedido.Total,
            pedido.FechaEntrega, pedido.ClienteId, clienteRazonSocial,
            DireccionDudosa: destino.GeoConfianza is not ("alta" or "media") && !destino.Verificada));
    }

    /// <summary>Detalle completo + historial de pedido_eventos (RF-28, criterio de aceptación 5:
    /// "auditar un pedido cualquiera → historia completa de estados, sin huecos"). Mismo filtro
    /// por cliente_id que Listar para el rol 'cliente'.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var clienteId = User.ClienteId();

        // Se materializa primero y se hace ToString() en memoria (igual que Listar arriba):
        // el enum de Postgres y las navegaciones opcionales no traducen bien un ternario/ToString
        // dentro del mismo Select que arma la consulta.
        var fila = await db.Pedidos.AsNoTracking()
            .Where(p => p.Id == id)
            .Where(p => clienteId == null || p.ClienteId == clienteId)
            .Select(p => new
            {
                p.Id,
                p.ClienteId,
                ClienteRazonSocial = p.Cliente.RazonSocial,
                p.ReferenciaCliente,
                p.Tipo,
                p.PedidoOrigenId,
                p.DestinoUbicacion.CalleNumero,
                LocalidadNombre = p.DestinoUbicacion.Localidad != null ? p.DestinoUbicacion.Localidad.Nombre : null,
                p.DestinatarioNombre,
                p.DestinatarioTelefono,
                p.Bultos,
                p.PesoKg,
                p.ValorDeclarado,
                p.FechaEntrega,
                p.Urgente,
                p.PrecioBase,
                p.RecargoUrgencia,
                p.DescuentoRuta,
                p.Peajes,
                p.Total,
                p.PrecioCongeladoEn,
                p.Estado,
                p.OrigenCarga,
                p.Observaciones,
                p.CreadoEn,
                DireccionDudosa = !LogisticaDbContext.UbicacionApta(p.DestinoUbicacionId),
            })
            .SingleOrDefaultAsync(ct);
        if (fila is null) return NotFound();

        var eventos = await db.PedidoEventos.AsNoTracking()
            .Where(e => e.PedidoId == id)
            .OrderBy(e => e.OcurridoEn)
            .Select(e => new
            {
                e.Id,
                e.EstadoAnterior,
                e.EstadoNuevo,
                e.Motivo,
                e.ActorTipo,
                ActorNombre = e.ActorUsuario != null ? e.ActorUsuario.Nombre : null,
                e.ActorTexto,
                e.OcurridoEn,
            })
            .ToListAsync(ct);

        var historial = eventos.Select(e => new HistorialEvento(
            e.Id, e.EstadoAnterior?.ToString(), e.EstadoNuevo.ToString(), e.Motivo,
            e.ActorTipo, e.ActorNombre ?? e.ActorTexto, e.OcurridoEn)).ToList();

        return Ok(new PedidoDetalle(
            fila.Id, fila.ClienteId, fila.ClienteRazonSocial, fila.ReferenciaCliente,
            fila.Tipo, fila.PedidoOrigenId,
            fila.CalleNumero, fila.LocalidadNombre,
            fila.DestinatarioNombre, fila.DestinatarioTelefono,
            fila.Bultos, fila.PesoKg, fila.ValorDeclarado,
            fila.FechaEntrega, fila.Urgente,
            fila.PrecioBase, fila.RecargoUrgencia, fila.DescuentoRuta, fila.Peajes, fila.Total, fila.PrecioCongeladoEn,
            fila.Estado.ToString(), fila.OrigenCarga, fila.Observaciones, fila.CreadoEn,
            fila.DireccionDudosa, historial));
    }

    /// <summary>
    /// Transición de estado validada contra TransicionesPedido (construccion_v1.md §5). Escribe
    /// con GuardarComoAsync para que fn_log_estado_pedido registre actor y motivo — igual que
    /// Crear más arriba, nunca SaveChangesAsync directo para un evento de dominio.
    /// </summary>
    [HttpPost("{id:long}/estado")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> CambiarEstado(long id, CambiarEstadoRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<EstadoPedido>(req.EstadoNuevo, ignoreCase: true, out var nuevo))
            return BadRequest("Estado inválido.");

        var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null) return NotFound();

        if (!TransicionesPedido.Permitida(pedido.Estado, nuevo))
            return BadRequest($"No se puede pasar de {pedido.Estado} a {nuevo}.");

        if (TransicionesPedido.MotivoObligatorio(nuevo) && string.IsNullOrWhiteSpace(req.Motivo))
            return BadRequest("El motivo es obligatorio para esta transición.");

        if (nuevo == EstadoPedido.Reprogramado)
        {
            // Primer reintento por ausente: sin cargo, misma fila, nueva fecha (acta §7).
            if (req.NuevaFechaEntrega is null) return BadRequest("Falta la nueva fecha de entrega.");
            pedido.FechaEntrega = req.NuevaFechaEntrega.Value;
        }

        if (nuevo == EstadoPedido.Devuelto)
        {
            // El bulto no entregado vuelve y el retorno se registra como servicio (acta §7): es
            // un pedido nuevo encadenado (ck_pedidos_origen_coherente), no una reescritura del
            // original — P1 lo impediría igual una vez confirmado. Nace en Borrador igual que
            // cualquier alta (acta changelog 3.11): su vehículo tampoco se conoce todavía — va a
            // una fecha futura, a rutear después — así que el precio se difiere lo mismo que el
            // de cualquier pedido nuevo, hasta que esa ruta cierre su planificación.
            var zonaRetorno = await db.Ubicaciones.Where(u => u.Id == pedido.OrigenUbicacionId)
                .Select(u => u.Localidad!.ZonaId).SingleOrDefaultAsync(ct);
            if (zonaRetorno is null)
                return BadRequest("No se puede cotizar el retorno: el depósito no tiene zona asignada.");

            db.Pedidos.Add(new Pedido
            {
                ClienteId = pedido.ClienteId,
                ReferenciaCliente = pedido.ReferenciaCliente,
                Tipo = "retorno",
                PedidoOrigenId = pedido.Id,
                OrigenUbicacionId = pedido.DestinoUbicacionId,
                DestinoUbicacionId = pedido.OrigenUbicacionId,
                // Trim por consistencia con Crear() — pedidos cargados antes de este fix pueden
                // traer espacios sueltos en la fila original.
                DestinatarioNombre = pedido.DestinatarioNombre.Trim(),
                DestinatarioTelefono = pedido.DestinatarioTelefono.Trim(),
                Bultos = pedido.Bultos,
                FechaEntrega = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                ZonaId = zonaRetorno,
                Estado = EstadoPedido.Borrador,
                Observaciones = $"Retorno del pedido #{pedido.Id}.",
                CreadoEn = DateTimeOffset.UtcNow,
            });
        }

        pedido.Estado = nuevo;
        await db.GuardarComoAsync(User.UsuarioId(), req.Motivo, ct);

        return NoContent();
    }

    /// <summary>Prueba de entrega del pedido (H2). Sin esto no hay forma de verificar el cierre de
    /// una parada desde el back-office sin abrir psql. Más estricta que la clase (Administracion
    /// sobre administracion+operacion+cliente) — combinación AND, regla 8.</summary>
    [HttpGet("{id:long}/prueba-entrega")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> PruebaEntrega(long id, CancellationToken ct)
    {
        var umbral = opcionesPruebaEntrega.Value.UmbralDesvioMetros;

        var prueba = await db.PruebasEntrega.AsNoTracking()
            .Where(pe => pe.PedidoId == id)
            .OrderByDescending(pe => pe.CapturadaEn)
            .Select(pe => new PruebaEntregaResumen(
                pe.Id, pe.Resultado, pe.MotivoFallo, pe.ReceptorNombre, pe.IdentidadVerificada,
                pe.FotoPath != null, pe.Lat, pe.Lng, pe.DesvioMetros,
                pe.DesvioMetros != null && pe.DesvioMetros > umbral,
                pe.CapturadaEn, pe.SincronizadaEn))
            .FirstOrDefaultAsync(ct);

        return prueba is null ? NotFound() : Ok(prueba);
    }
}
