using System.ComponentModel.DataAnnotations;
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
    LogisticaDbContext db, PrecioService precios, DistanciaService distancias,
    IOptions<OpcionesPruebaEntrega> opcionesPruebaEntrega,
    OrigenRutaService origenes, CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    public record PedidoResumen(
        long Id, string DestinatarioNombre, string Estado, decimal? Total,
        DateOnly FechaEntrega, int ClienteId, string ClienteRazonSocial, bool DireccionDudosa,
        bool RequiereCotizacion = false);

    public record PrecioManualInfo(decimal Precio, string? FijadoPor, DateTimeOffset FijadoEn);


    /// <summary>DestinoUbicacionId y KmManual son opcionales: sin ellos, la cotización queda
    /// idéntica a la de siempre (recargo_km = 0). El formulario de alta ya resuelve la dirección
    /// antes de cotizar (geocodificación de app/pedidos/nuevo), así que puede pasar el id apenas
    /// lo tenga para ver el km real desde el depósito principal.</summary>
    public record CotizarRequest(
        int ClienteId, int LocalidadId, DateOnly FechaEntrega, bool Urgente,
        [Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")] decimal Peajes = 0m,
        long? DestinoUbicacionId = null,
        [Range(0, double.MaxValue, ErrorMessage = "El km manual no puede ser negativo.")] decimal? KmManual = null);

    /// <summary>Estimado informativo (acta changelog 3.11): null en un tipo = todavía no se cargó
    /// tarifa para esa zona en ese tipo de vehículo, no un error. RequiereCotizacion (Anexo I B9):
    /// true cuando la zona no resuelve tarifa en NINGÚN tipo de vehículo — el alta se completa
    /// igual, pero el pedido va a necesitar un precio manual antes de poder rutearse.</summary>
    public record CotizacionEstimada(DesglosePrecio? Camioneta, DesglosePrecio? Moto, bool RequiereCotizacion);

    /// <summary>null = no se toca ese campo. Observaciones en blanco lo borra. Destino y precio no están
    /// acá a propósito: fn_congelar_pedido los protege desde que el pedido sale de Borrador (P1).</summary>
    public record EditarContactoRequest(string? DestinatarioTelefono, string? DestinatarioNombre, string? Observaciones);

    public record FijarPrecioManualRequest(
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")] decimal Precio);

    public record CrearPedidoRequest(
        int ClienteId,
        string? ReferenciaCliente,
        string DestinatarioNombre,
        string DestinatarioTelefono,
        long DestinoUbicacionId,
        [Range(1, int.MaxValue, ErrorMessage = "Los bultos deben ser al menos 1.")] int Bultos,
        [Range(0, double.MaxValue, ErrorMessage = "El peso no puede ser negativo.")] decimal? PesoKg,
        [Range(0, double.MaxValue, ErrorMessage = "El valor declarado no puede ser negativo.")] decimal? ValorDeclarado,
        DateOnly FechaEntrega,
        bool Urgente,
        [Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")] decimal Peajes,
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
        decimal? PrecioBase, decimal? RecargoKm, decimal? KmCobrados, string? KmFuente,
        decimal? RecargoUrgencia, decimal? DescuentoRuta, decimal Peajes, decimal? Total,
        DateTimeOffset? PrecioCongeladoEn,
        string Estado, string OrigenCarga, string? Observaciones, DateTimeOffset CreadoEn,
        bool DireccionDudosa, bool RequiereCotizacion, PrecioManualInfo? PrecioManual,
        int VecesReprogramado, bool Facturado, List<HistorialEvento> Historial);

    public record CambiarEstadoRequest(string EstadoNuevo, string? Motivo, DateOnly? NuevaFechaEntrega);

    /// <summary>E1/§10.2-M: se devuelve cuando la 4ta reprogramación redirige a un pedido nuevo
    /// tipo='reintento' en vez de reprogramar la fila original (que queda en Fallido).</summary>
    public record ReintentoCreado(long PedidoOriginalId, long ReintentoId, int Reprogramaciones);

    // ---- E1 / B16: ajustes de un pedido (bultos reales vs. declarados al retiro físico) ----

    public record AjusteResumen(
        long Id, string Tipo, string Descripcion, decimal? Monto, string Estado,
        string? CreadoPorNombre, DateTimeOffset CreadoEn,
        string? ResueltoPorNombre, DateTimeOffset? ResueltoEn);

    public record SolicitarAjusteRequest(int BultosReales, string Descripcion);
    public record AprobarAjusteRequest(
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero.")] decimal Monto,
        [Range(0.01, double.MaxValue, ErrorMessage = "El cargo de gestión debe ser mayor a cero.")] decimal? CargoGestion);
    public record RechazarAjusteRequest(string Motivo);

    public record PruebaEntregaResumen(
        long Id, string Resultado, string? MotivoFallo, string? ReceptorNombre, bool IdentidadVerificada,
        bool TieneFoto, decimal? Lat, decimal? Lng, int? DesvioMetros, bool DesvioAlto,
        DateTimeOffset CapturadaEn, DateTimeOffset SincronizadaEn);

    public record CandidatoRuta(
        long PedidoId, string ClienteRazonSocial, string DestinatarioNombre, int Bultos, bool Urgente,
        string? ZonaCodigo, long DestinoUbicacionId, string DestinoCalleNumero, string? DestinoLocalidad,
        decimal? Lat, decimal? Lng, bool DireccionApta, bool YaEnEstaRuta,
        bool RequiereCotizacion, decimal? PrecioManual);

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
    /// ellos devuelve todo sin recortar. Todos los callers del frontend (incluido /mis-envios,
    /// rol 'cliente') ya paginan vía useListadoPaginado; se deja la paginación opcional en vez de
    /// obligatoria por compatibilidad con otros consumidores del endpoint.
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
                p.ZonaId,
                p.PrecioManual,
            })
            .ToListAsync(ct);

        // B9 (Anexo I §4): mismo criterio que CandidatosRuta — zonas activas sin ninguna tarifa
        // cargada (ni camioneta ni moto). Se calcula una sola vez por página, no por fila.
        var zonasSinTarifa = (await db.Zonas.AsNoTracking()
            .Where(z => !db.Tarifas.Any(t => t.ZonaId == z.Id && t.VigenteHasta == null))
            .Select(z => z.Id)
            .ToListAsync(ct)).ToHashSet();

        var resultado = filas.Select(p => new PedidoResumen(
            p.Id, p.DestinatarioNombre, p.Estado.ToString(), p.Total, p.FechaEntrega,
            p.ClienteId, p.ClienteRazonSocial, p.DireccionDudosa,
            RequiereCotizacion: p.Estado == EstadoPedido.Borrador && p.PrecioManual is null
                && p.ZonaId is not null && zonasSinTarifa.Contains(p.ZonaId.Value))).ToList();

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
                p.ZonaId,
                ZonaCodigo = p.Zona != null ? p.Zona.Codigo : null,
                p.DestinoUbicacionId,
                p.DestinoUbicacion.CalleNumero,
                DestinoLocalidad = p.DestinoUbicacion.Localidad != null ? p.DestinoUbicacion.Localidad.Nombre : null,
                p.DestinoUbicacion.Lat,
                p.DestinoUbicacion.Lng,
                DireccionApta = LogisticaDbContext.UbicacionApta(p.DestinoUbicacionId),
                p.PrecioManual,
            })
            .ToListAsync(ct);

        // Zonas sin ninguna tarifa cargada (ni camioneta ni moto) — B9: el candidato va a
        // necesitar precio manual para poder cerrar la planificación, y conviene que el
        // planificador lo vea acá, antes de armar, no recién con el 400 al cerrar.
        var zonasSinTarifa = (await db.Zonas.AsNoTracking()
            .Where(z => !db.Tarifas.Any(t => t.ZonaId == z.Id && t.VigenteHasta == null))
            .Select(z => z.Id)
            .ToListAsync(ct)).ToHashSet();

        var resultado = filas.Select(p => new CandidatoRuta(
            p.Id, p.ClienteRazonSocial, p.DestinatarioNombre, p.Bultos, p.Urgente, p.ZonaCodigo,
            p.DestinoUbicacionId, p.CalleNumero, p.DestinoLocalidad, p.Lat, p.Lng, p.DireccionApta,
            idsEnEstaRuta.Contains(p.Id),
            RequiereCotizacion: p.PrecioManual is null && p.ZonaId is not null && zonasSinTarifa.Contains(p.ZonaId.Value),
            p.PrecioManual));

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

        // Anexo I §10.2-N: recargo por km, encima del precio de zona. Sin DestinoUbicacionId (la
        // dirección todavía no se geocodificó en el formulario) o sin depósito cargado, queda
        // null y recargo_km = 0 — el estimado no cambia respecto de antes; un depósito faltante
        // no puede tumbar un estimado que hasta ahora no lo necesitaba.
        DistanciaResuelta? distancia = null;
        if (req.DestinoUbicacionId is not null)
        {
            var destino = await db.Ubicaciones.AsNoTracking()
                .Where(u => u.Id == req.DestinoUbicacionId.Value)
                .Select(u => new { u.Lat, u.Lng })
                .SingleOrDefaultAsync(ct);
            if (destino is not null)
            {
                try
                {
                    var origen = await origenes.PrincipalParaPedidosAsync(ct);
                    distancia = await distancias.ResolverAsync(origen.Lat, origen.Lng, destino.Lat, destino.Lng, req.KmManual, ct);
                }
                catch (InvalidOperationException)
                {
                    distancia = null;
                }
            }
        }

        async Task<DesglosePrecio?> Intentar(string tipoVehiculo)
        {
            try
            {
                return await precios.CotizarAsync(
                    req.ClienteId, zonaId.Value, req.FechaEntrega, req.Urgente, req.Peajes,
                    descuentoRuta: false, tipoVehiculo, distancia: distancia, ct: ct);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var camioneta = await Intentar("camioneta");
        var moto = await Intentar("moto");
        return Ok(new CotizacionEstimada(camioneta, moto, RequiereCotizacion: camioneta is null && moto is null));
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
        // E1 (§10.2-L1/L3): el corte de servicio por deuda vencida solo bloquea altas nuevas —
        // se chequea primero porque es lo más barato y evita geocodificar para un cliente
        // bloqueado. De paso corrige que Crear nunca validó que el cliente existiera ni
        // estuviera activo: antes de esto, un ClienteId inexistente reventaba como FK violation
        // traducida a 409 con el texto de Postgres en inglés.
        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == req.ClienteId)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return BadRequest("El cliente no existe.");
        if (!cliente.Activo) return Conflict($"{cliente.RazonSocial} está inactivo; no admite pedidos nuevos.");

        var hoy = Reloj.HoyLocal();
        var suspendido = cliente.CorteSuspendidoHasta is { } h && h >= hoy;
        if (!suspendido)
        {
            var deuda = await cuentaCorriente.DeudaVencidaAsync(req.ClienteId, hoy, ct);
            if (deuda > 0)
                return Conflict(
                    $"Servicio cortado por deuda vencida: {cliente.RazonSocial} adeuda ${deuda:N2} de " +
                    "comprobantes vencidos. Los pedidos ya confirmados o en ruta no se ven afectados " +
                    "(§10.2-L1). Se rehabilita solo al pagar el total (§10.2-L5) o con un plan de cuotas.");
        }

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

        // B9: no hay tarifa para esta zona en ningún tipo de vehículo — mismo chequeo puntual que
        // Cotizar(), acá sobre una sola zona en vez de precalcular el set completo (Listar,
        // CandidatosRuta), que no tendría sentido para un solo pedido recién creado.
        var zonaSinTarifa = !await db.Tarifas.AnyAsync(t => t.ZonaId == zonaId && t.VigenteHasta == null, ct);

        return CreatedAtAction(nameof(Listar), new { }, new PedidoResumen(
            pedido.Id, pedido.DestinatarioNombre, pedido.Estado.ToString(), pedido.Total,
            pedido.FechaEntrega, pedido.ClienteId, cliente.RazonSocial,
            DireccionDudosa: destino.GeoConfianza is not ("alta" or "media") && !destino.Verificada,
            RequiereCotizacion: zonaSinTarifa));
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
                p.RecargoKm,
                p.KmCobrados,
                p.KmFuente,
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
                p.PrecioManual,
                PrecioManualPorNombre = p.PrecioManualPorUsuario != null ? p.PrecioManualPorUsuario.Nombre : null,
                p.PrecioManualEn,
                ZonaTieneTarifa = p.ZonaId != null && db.Tarifas.Any(t => t.ZonaId == p.ZonaId && t.VigenteHasta == null),
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

        var precioManualInfo = fila.PrecioManual is not null && fila.PrecioManualEn is not null
            ? new PrecioManualInfo(fila.PrecioManual.Value, fila.PrecioManualPorNombre, fila.PrecioManualEn.Value)
            : null;

        // E1: derivado del log inmutable (pedido_eventos), no una columna — mismo criterio que
        // el tope de 3 en CambiarEstado. "Facturado" es barato de chequear (índice único
        // ux_factura_items_pedido) y le evita al frontend pedir /ajustes solo para saber esto.
        var vecesReprogramado = eventos.Count(e => e.EstadoNuevo.ToString() == "Reprogramado");
        var facturado = await db.FacturaItems.AnyAsync(i => i.PedidoId == id && i.Tipo == "pedido", ct);

        return Ok(new PedidoDetalle(
            fila.Id, fila.ClienteId, fila.ClienteRazonSocial, fila.ReferenciaCliente,
            fila.Tipo, fila.PedidoOrigenId,
            fila.CalleNumero, fila.LocalidadNombre,
            fila.DestinatarioNombre, fila.DestinatarioTelefono,
            fila.Bultos, fila.PesoKg, fila.ValorDeclarado,
            fila.FechaEntrega, fila.Urgente,
            fila.PrecioBase, fila.RecargoKm, fila.KmCobrados, fila.KmFuente,
            fila.RecargoUrgencia, fila.DescuentoRuta, fila.Peajes, fila.Total, fila.PrecioCongeladoEn,
            fila.Estado.ToString(), fila.OrigenCarga, fila.Observaciones, fila.CreadoEn,
            fila.DireccionDudosa, RequiereCotizacion: fila.PrecioManual is null && !fila.ZonaTieneTarifa,
            precioManualInfo, vecesReprogramado, facturado, historial));
    }

    /// <summary>
    /// B9 (Anexo I §4, "+40 km → Cotización"): fija a mano el precio_base de un pedido cuya zona
    /// no tiene tarifa cargada. Más estricta que la clase (Administracion sobre BackOffice) —
    /// combinación AND, construccion_v1.md §3 regla 8, mismo criterio que ZonasController.
    /// ActualizarKm y el ABM de depósitos: fijar precio es decisión de empresa, no del día a día
    /// de operación. Solo en Borrador — una vez confirmado, fn_congelar_pedido lo protege igual
    /// que el resto del precio (P1).
    /// </summary>
    [HttpPut("{id:long}/precio-manual")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> FijarPrecioManual(long id, FijarPrecioManualRequest req, CancellationToken ct)
    {
        var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null) return NotFound();
        if (pedido.Estado != EstadoPedido.Borrador)
            return Conflict("El pedido ya no está en borrador; el precio se congeló al confirmarse (P1).");

        pedido.PrecioManual = req.Precio;
        pedido.PrecioManualPor = User.UsuarioId();
        pedido.PrecioManualEn = DateTimeOffset.UtcNow;

        await db.GuardarComoAsync(User.UsuarioId(), "Precio manual fijado (zona sin tarifa).", ct);
        return NoContent();
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

        // E1 (§10.2-I): cancelar es gratis en Borrador (nunca tuvo precio). Un Confirmado ya
        // consumió planificación y tiene precio congelado (P1) — cancelarlo se factura al 100%,
        // igual que si se hubiera entregado.
        if (nuevo == EstadoPedido.Cancelado && pedido.Estado != EstadoPedido.Borrador)
        {
            if (pedido.Total is null)
                return Conflict($"El pedido {pedido.Id} no tiene precio congelado; no se puede facturar su cancelación.");

            cuentaCorriente.AgregarItemDePedido(
                pedido,
                $"Cancelación del pedido #{pedido.Id} ya confirmado — 100% del servicio (§10.2-I).",
                pedido.Total.Value,
                User.UsuarioId());
            // sigue el flujo normal: cae a pedido.Estado = nuevo más abajo, un único GuardarComoAsync.
        }

        if (nuevo == EstadoPedido.Reprogramado)
        {
            if (req.NuevaFechaEntrega is null) return BadRequest("Falta la nueva fecha de entrega.");

            // E1/D13 (§10.2-M): 3 reprogramaciones gratis por pedido — antes era 1. Derivado de
            // pedido_eventos (insert-only, trg_log_inmutable), no una columna que pueda
            // desincronizarse de lo que el trigger ya registró.
            var reprogramaciones = await db.PedidoEventos.CountAsync(
                e => e.PedidoId == pedido.Id && e.EstadoNuevo == EstadoPedido.Reprogramado, ct);

            if (reprogramaciones >= 3)
            {
                // Agotadas las 3 gratis: en vez de reprogramar una 4ta vez, genera un pedido
                // nuevo tipo='reintento' con precio propio — mismo patrón que la rama Devuelto
                // (tipo='retorno') de abajo, pero SIN invertir origen/destino: es un segundo
                // intento al mismo domicilio, no una vuelta al depósito. Nace en Borrador, sin
                // precio (acta changelog 3.11): su vehículo no se conoce hasta que se arme su
                // propia ruta.
                var reintento = new Pedido
                {
                    ClienteId = pedido.ClienteId,
                    ReferenciaCliente = pedido.ReferenciaCliente,
                    Tipo = "reintento",
                    PedidoOrigenId = pedido.Id,
                    OrigenUbicacionId = pedido.OrigenUbicacionId,
                    DestinoUbicacionId = pedido.DestinoUbicacionId,
                    DestinatarioNombre = pedido.DestinatarioNombre.Trim(),
                    DestinatarioTelefono = pedido.DestinatarioTelefono.Trim(),
                    Bultos = pedido.Bultos,
                    PesoKg = pedido.PesoKg,
                    ValorDeclarado = pedido.ValorDeclarado,
                    FechaEntrega = req.NuevaFechaEntrega.Value,
                    ZonaId = pedido.ZonaId,
                    Estado = EstadoPedido.Borrador,
                    Observaciones = $"Reintento del pedido #{pedido.Id}: agotadas las 3 reprogramaciones sin cargo (D13).",
                    CreadoEn = DateTimeOffset.UtcNow,
                };
                db.Pedidos.Add(reintento);

                // El original QUEDA EN Fallido — no pasa a Reprogramado. Por eso se corta acá con
                // un return explícito en vez de caer al pedido.Estado = nuevo del final del método.
                await db.GuardarComoAsync(User.UsuarioId(),
                    req.Motivo ?? $"4ta reprogramación del pedido #{pedido.Id}: se generó un reintento con cargo.", ct);

                return Ok(new ReintentoCreado(pedido.Id, reintento.Id, reprogramaciones));
            }

            pedido.FechaEntrega = req.NuevaFechaEntrega.Value; // 1ra, 2da o 3ra: sin cargo, misma fila
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

        // Acta changelog 4.8: cancelar un pedido que el repartidor ya lleva no puede pasar en silencio.
        if (nuevo == EstadoPedido.Cancelado)
            await AvisarCancelacionAlRepartidorAsync(pedido, req.Motivo, ct);

        await db.GuardarComoAsync(User.UsuarioId(), req.Motivo, ct);

        return NoContent();
    }

    /// <summary>
    /// Si el pedido va en una ruta en curso, deja un aviso para el repartidor y, cuando la cancelación
    /// deja a la parada sin ningún pedido activo, la pasa a `cancelada` (terminal, sin prueba de
    /// entrega). Sin esto el repartidor seguiría yendo a una dirección donde ya no hay nada para
    /// entregar y el cierre de esa parada terminaría en un 409. Se agrega al mismo SaveChanges que la
    /// transición del pedido: o quedan las dos cosas o ninguna.
    /// </summary>
    private async Task AvisarCancelacionAlRepartidorAsync(Pedido pedido, string? motivo, CancellationToken ct)
    {
        var enRuta = await db.ParadaPedidos
            .Where(pp => pp.PedidoId == pedido.Id && pp.Parada.Ruta.Estado == "en_curso")
            .Select(pp => new { pp.ParadaId, pp.Parada.RutaId })
            .FirstOrDefaultAsync(ct);
        if (enRuta is null) return;

        var quedanActivos = await db.ParadaPedidos.AnyAsync(pp =>
            pp.ParadaId == enRuta.ParadaId && pp.PedidoId != pedido.Id && pp.Pedido.Estado != EstadoPedido.Cancelado, ct);
        if (!quedanActivos)
        {
            var parada = await db.RutaParadas.SingleAsync(p => p.Id == enRuta.ParadaId, ct);
            if (parada.Estado == "pendiente") parada.Estado = "cancelada";
        }

        db.Novedades.Add(new Novedad
        {
            RutaId = enRuta.RutaId,
            ParadaId = enRuta.ParadaId,
            PedidoId = pedido.Id,
            Tipo = "cancelacion",
            Origen = "operacion",
            Descripcion = $"Operación canceló el pedido #{pedido.Id} ({pedido.DestinatarioNombre})."
                + (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}"),
            CreadaPor = User.UsuarioId(),
            CreadaEn = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>
    /// Corrección de datos de contacto de un pedido (RF-37). Solo lo que P1 no congela: teléfono, nombre del
    /// destinatario y observaciones. Con el pedido ya en una ruta en curso, el cambio le llega al
    /// repartidor como aviso — sin eso él seguiría llamando al teléfono viejo. Más estricta que la clase
    /// (BackOffice sobre administracion+operacion+cliente): combinación AND, regla 8 — el cliente no edita.
    /// </summary>
    [HttpPut("{id:long}/contacto")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> EditarContacto(long id, EditarContactoRequest req, CancellationToken ct)
    {
        if (req.DestinatarioTelefono is null && req.DestinatarioNombre is null && req.Observaciones is null)
            return BadRequest("No hay nada para cambiar.");
        if (req.DestinatarioTelefono is not null && string.IsNullOrWhiteSpace(req.DestinatarioTelefono))
            return BadRequest("El teléfono no puede quedar en blanco.");
        if (req.DestinatarioNombre is not null && string.IsNullOrWhiteSpace(req.DestinatarioNombre))
            return BadRequest("El nombre del destinatario no puede quedar en blanco.");

        var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null) return NotFound();

        if (pedido.Estado is not (EstadoPedido.Borrador or EstadoPedido.Confirmado or EstadoPedido.EnRuta))
            return Conflict($"El pedido ya está {pedido.Estado}: sus datos de contacto no se editan.");

        var cambios = new List<string>();
        if (req.DestinatarioTelefono is not null && req.DestinatarioTelefono.Trim() != pedido.DestinatarioTelefono)
        {
            cambios.Add($"teléfono {pedido.DestinatarioTelefono} → {req.DestinatarioTelefono.Trim()}");
            pedido.DestinatarioTelefono = req.DestinatarioTelefono.Trim();
        }
        if (req.DestinatarioNombre is not null && req.DestinatarioNombre.Trim() != pedido.DestinatarioNombre)
        {
            cambios.Add($"destinatario {pedido.DestinatarioNombre} → {req.DestinatarioNombre.Trim()}");
            pedido.DestinatarioNombre = req.DestinatarioNombre.Trim();
        }
        if (req.Observaciones is not null)
        {
            var nuevas = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim();
            if (nuevas != pedido.Observaciones)
            {
                cambios.Add(nuevas is null ? "observaciones borradas" : $"observaciones: {nuevas}");
                pedido.Observaciones = nuevas;
            }
        }
        if (cambios.Count == 0) return NoContent();

        var enRuta = await db.ParadaPedidos
            .Where(pp => pp.PedidoId == id && pp.Parada.Ruta.Estado == "en_curso")
            .Select(pp => new { pp.ParadaId, pp.Parada.RutaId })
            .FirstOrDefaultAsync(ct);
        if (enRuta is not null)
        {
            db.Novedades.Add(new Novedad
            {
                RutaId = enRuta.RutaId,
                ParadaId = enRuta.ParadaId,
                PedidoId = id,
                Tipo = "cambio_operacion",
                Origen = "operacion",
                Descripcion = $"Operación cambió el pedido #{id}: {string.Join("; ", cambios)}.",
                CreadaPor = User.UsuarioId(),
                CreadaEn = DateTimeOffset.UtcNow,
            });
        }

        await db.GuardarComoAsync(User.UsuarioId(), null, ct);
        return NoContent();
    }

    /// <summary>E1 / B16: historial de ajustes de un pedido — pendientes, aprobados y
    /// rechazados, más antiguo primero (igual que el Historial de estados).</summary>
    [HttpGet("{id:long}/ajustes")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> ListarAjustes(long id, CancellationToken ct)
    {
        if (!await db.Pedidos.AnyAsync(p => p.Id == id, ct)) return NotFound();

        var ajustes = await db.FacturaItems.AsNoTracking()
            .Where(i => i.PedidoId == id && i.Tipo != "pedido")
            .OrderBy(i => i.CreadoEn)
            .Select(i => new AjusteResumen(
                i.Id, i.Tipo, i.Descripcion, i.Monto, i.Estado,
                i.CreadoPorUsuario != null ? i.CreadoPorUsuario.Nombre : null, i.CreadoEn,
                i.ResueltoPorUsuario != null ? i.ResueltoPorUsuario.Nombre : null, i.ResueltoEn))
            .ToListAsync(ct);

        return Ok(ajustes);
    }

    /// <summary>
    /// E1 / B16 (sumado por decisión del usuario): al retiro físico, operación marca la cantidad
    /// real de bultos cuando difiere de lo declarado. Queda pendiente hasta que un admin lo
    /// apruebe con un monto — no toca `pedidos` (frozen por P1), el ajuste vive aparte en
    /// factura_items y entra en la SIGUIENTE factura.
    /// </summary>
    [HttpPost("{id:long}/ajustes")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> SolicitarAjuste(long id, SolicitarAjusteRequest req, CancellationToken ct)
    {
        var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null) return NotFound();

        db.FacturaItems.Add(new FacturaItem
        {
            PedidoId = id,
            Tipo = "ajuste",
            Descripcion = $"Bultos declarados: {pedido.Bultos}. Bultos reales: {req.BultosReales}. {req.Descripcion}".Trim(),
            Monto = null,
            Estado = "pendiente",
            CreadoPor = User.UsuarioId(),
            CreadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Más estricta que la clase (Administracion sobre BackOffice) — combinación AND,
    /// mismo criterio que precio-manual (B9): fijar un monto que afecta lo que se cobra es
    /// decisión de empresa. Tope de 3 ajustes/notas por pedido sin cargo extra (§10.2 sumado a
    /// E1); a partir del 4to, `cargoGestion` es obligatorio y entra como un ítem SEPARADO — sin
    /// porcentaje inventado, el admin lo tipea a mano, igual que precio_manual.</summary>
    [HttpPut("{id:long}/ajustes/{ajusteId:long}/aprobar")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> AprobarAjuste(long id, long ajusteId, AprobarAjusteRequest req, CancellationToken ct)
    {
        var ajuste = await db.FacturaItems.SingleOrDefaultAsync(i => i.Id == ajusteId && i.PedidoId == id, ct);
        if (ajuste is null) return NotFound();
        if (ajuste.Estado != "pendiente") return Conflict("Este ajuste ya fue resuelto.");

        var previos = await db.FacturaItems.CountAsync(i =>
            i.PedidoId == id && i.Tipo != "pedido" && i.Estado == "aprobado", ct);

        if (previos >= 3 && req.CargoGestion is null)
            return BadRequest("Este pedido ya tiene 3 ajustes aprobados; el 4to exige que fijes también un cargo de gestión.");

        var actor = User.UsuarioId();
        var ahora = DateTimeOffset.UtcNow;

        ajuste.Monto = req.Monto;
        ajuste.Estado = "aprobado";
        ajuste.ResueltoPor = actor;
        ajuste.ResueltoEn = ahora;

        if (previos >= 3 && req.CargoGestion is not null)
        {
            db.FacturaItems.Add(new FacturaItem
            {
                PedidoId = id,
                Tipo = "ajuste",
                Descripcion = $"Cargo de gestión por 4º ajuste sobre el pedido #{id}.",
                Monto = req.CargoGestion.Value,
                Estado = "aprobado",
                CreadoPor = actor,
                CreadoEn = ahora,
                ResueltoPor = actor,
                ResueltoEn = ahora,
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:long}/ajustes/{ajusteId:long}/rechazar")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> RechazarAjuste(long id, long ajusteId, RechazarAjusteRequest req, CancellationToken ct)
    {
        var ajuste = await db.FacturaItems.SingleOrDefaultAsync(i => i.Id == ajusteId && i.PedidoId == id, ct);
        if (ajuste is null) return NotFound();
        if (ajuste.Estado != "pendiente") return Conflict("Este ajuste ya fue resuelto.");

        ajuste.Estado = "rechazado";
        ajuste.Descripcion += $"\nRechazado: {req.Motivo}";
        ajuste.ResueltoPor = User.UsuarioId();
        ajuste.ResueltoEn = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
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
