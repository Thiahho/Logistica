using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Logistica.Web;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Logistica.Servicios;

namespace Logistica.Controllers;

/// <summary>
/// E1: cuenta corriente para el rol 'cliente' — resuelve el cliente desde el claim de sesión,
/// nunca desde la URL, así que no puede filtrar por id ajeno. Controller propio, no una acción de
/// FacturasController (que es Administracion de clase) — mismo motivo que separa
/// MisParadasController del resto: construccion_v1.md §3 regla 8, `[Authorize]` de clase y de
/// acción se combinan con AND, no se reemplazan. Nunca expone color_pago/color_trato/color_oper
/// ni corte_suspendido_motivo (RF-33: los semáforos internos no se exponen al cliente).
///
/// B5 (diseño_b5_portal_carga.md) agrega el alta de pedido desde el portal: mismo controller, no
/// uno nuevo, porque comparte exactamente el mismo criterio de alcance ("resuelve del claim,
/// nunca del body ni de la URL").
/// </summary>
[ApiController]
[Route("api/mi-cuenta")]
[Authorize(Policy = "Cliente")]
public class MiCuentaController(
    LogisticaDbContext db, CuentaCorrienteService cuentaCorriente, PrecioService precios,
    DistanciaService distancias, OrigenRutaService origenes, IOptions<OpcionesPortal> opcionesPortal,
    UbicacionService ubicaciones, GeocodificacionService geocodificador, ZonaLocalidadService zonasLocalidad,
    DireccionDesdeMapaService desdeMapa, RangoClienteService rangos, AlmacenamientoFotos almacenamiento,
    ViajeService viajes)
    : ControllerBase
{
    public record FacturaPropia(
        long Id, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
        DateOnly FechaVencimiento, decimal Total, decimal Saldo, string Estado);

    public record CuentaPropia(
        decimal Saldo, decimal DeudaVencida, bool ServicioCortado,
        DateOnly? ProximoVencimiento, List<FacturaPropia> Facturas,
        // B3 (acta RF-42): el cliente ve su rango y su descuento, nunca los números con que se calculó (RF-33).
        string RangoNombre = "", decimal DescuentoPct = 0m);

    public record CrearPedidoPortalRequest(
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre del destinatario es obligatorio.")] string DestinatarioNombre,
        [Required(AllowEmptyStrings = false, ErrorMessage = "El teléfono del destinatario es obligatorio.")] string DestinatarioTelefono,
        long DestinoUbicacionId,
        [Range(1, 999, ErrorMessage = "Los bultos deben estar entre 1 y 999.")] int Bultos,
        [Range(0, 100000, ErrorMessage = "El peso debe estar entre 0 y 100000 kg.")] decimal? PesoKg,
        DateOnly FechaEntrega,
        bool Urgente,
        string TipoVehiculo,
        string? Observaciones);

    /// <summary>Precio null + RequiereCotizacion true = B9 (zona sin tarifa): el pedido se creó
    /// igual, pero todavía no tiene precio vinculante — diseño_b5_portal_carga.md §5.</summary>
    public record PedidoPortalCreado(
        long Id, DateOnly FechaEntrega, decimal? Precio, bool RequiereCotizacion);

    // Los tres records/acciones que siguen (Localidades*, Ubicaciones) son un espejo deliberado
    // de LocalidadesController/UbicacionesController, no una reexposición de esos controllers:
    // ambos son de clase [Authorize(Policy = "BackOffice")] (construccion_v1.md §3 regla 8, class-
    // y method-level Authorize se combinan con AND, nunca se reemplazan), así que 'cliente' no
    // puede llegar a ellos sin ensanchar esa policy para el resto del back-office. Mismo criterio
    // que ya usa este controller entero frente a FacturasController: una rebanada propia del
    // mismo servicio compartido, en vez de tocar el alcance de un controller ajeno.
    public record LocalidadResumen(int Id, string Nombre, string? Partido, int? ZonaId);
    public record ResultadoBusquedaLocalidad(
        IReadOnlyList<LocalidadResumen> Existentes, IReadOnlyList<SugerenciaLocalidad> Sugeridas);
    public record CrearLocalidadRequest(string Nombre, string? Partido);
    public record DesdeMapaRequest(string UrlMapa);
    public record ResolverUbicacionRequest(string CalleNumero, int LocalidadId, string? Referencia, string? UrlMapa = null);
    public record UbicacionResuelta(
        long Id, decimal? Lat, decimal? Lng, string? GeoConfianza,
        bool UrlMapaAplicada = false, string? UrlMapaError = null);

    /// <summary>"Mis clientes" (registro explícito de destinatarios, reversión de la decisión de
    /// acta 3.5 — ver acta_sistema.md changelog 4.10). Misma forma que
    /// PedidosController.DestinatarioFrecuente menos Veces/UltimaFechaEntrega, más Id/Observaciones:
    /// el frontend arma un DireccionResuelta directo al elegir un contacto, sin geocodificar de
    /// nuevo.</summary>
    public record ClienteDestinatarioResumen(
        Guid Id, string Nombre, string Telefono,
        long DestinoUbicacionId, string DestinoCalleNumero,
        int LocalidadId, string? LocalidadNombre,
        decimal? Lat, decimal? Lng, string? GeoConfianza, string? Observaciones);

    public record GuardarClienteDestinatarioRequest(
        string Nombre, string Telefono, long DestinoUbicacionId, string? Observaciones);

    public record EventoEstado(string Estado, string? Motivo, DateTimeOffset OcurridoEn);

    /// <summary>Un envío del día con su historial de estados, para la línea de tiempo de "Mi plan".
    /// Sin nombre de actor a propósito: nunca visible para el cliente (RF-33).</summary>
    public record PedidoDelDia(
        long Id, string DestinatarioNombre, string Estado,
        string DestinoCalleNumero, string? DestinoLocalidad, List<EventoEstado> Eventos);

    /// <summary>Si a esta sesión se le manda el precio de un envío (acta changelog 4.30).</summary>
    private bool VePrecios => User.VePreciosDeEnvio(opcionesPortal.Value.MostrarPrecios);

    private static string EstadoDe(decimal saldo, decimal total, DateOnly vencimiento, DateOnly hoy)
    {
        if (saldo <= 0) return "pagada";
        if (vencimiento < hoy) return "vencida";
        return saldo < total ? "parcial" : "pendiente";
    }

    /// <summary>Envíos de hoy del cliente (fecha de entrega = hoy, día local), con la línea de tiempo
    /// de estados de cada uno. El cliente sale del claim, nunca de la URL. Una sola consulta de
    /// eventos para todo el lote.</summary>
    [HttpGet("plan-del-dia")]
    public async Task<IActionResult> PlanDelDia(CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var hoy = Reloj.HoyLocal();
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId.Value && p.FechaEntrega == hoy)
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.DestinatarioNombre,
                p.Estado,
                p.DestinoUbicacion.CalleNumero,
                Localidad = p.DestinoUbicacion.Localidad != null ? p.DestinoUbicacion.Localidad.Nombre : null,
            })
            .ToListAsync(ct);

        var ids = pedidos.Select(p => p.Id).ToList();
        var eventos = (await db.PedidoEventos.AsNoTracking()
                .Where(e => ids.Contains(e.PedidoId))
                .OrderBy(e => e.OcurridoEn)
                .Select(e => new { e.PedidoId, e.EstadoNuevo, e.Motivo, e.OcurridoEn })
                .ToListAsync(ct))
            .ToLookup(e => e.PedidoId);

        return Ok(pedidos.Select(p => new PedidoDelDia(
            p.Id, p.DestinatarioNombre, p.Estado.ToString(), p.CalleNumero, p.Localidad,
            eventos[p.Id].Select(e => new EventoEstado(e.EstadoNuevo.ToString(), e.Motivo, e.OcurridoEn)).ToList())));
    }

    /// <summary>Cuenta corriente: solo el dueño. Un empleado carga y sigue envíos, no ve plata.</summary>
    [HttpGet]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> Detalle(CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var hoy = Reloj.HoyLocal();
        var saldo = await cuentaCorriente.SaldoAsync(clienteId.Value, ct);
        var deudaVencida = await cuentaCorriente.DeudaVencidaAsync(clienteId.Value, hoy, ct);
        var servicioCortado = await cuentaCorriente.ServicioCortadoAsync(clienteId.Value, hoy, deudaVencida, ct);

        var facturas = (await db.Set<FacturaSaldo>().AsNoTracking()
            .Where(f => f.ClienteId == clienteId.Value && f.Saldo > 0)
            .OrderBy(f => f.FechaVencimiento)
            .ToListAsync(ct))
            .Select(f => new FacturaPropia(
                f.Id, f.PeriodoDesde, f.PeriodoHasta, f.FechaVencimiento, f.Total, f.Saldo,
                EstadoDe(f.Saldo, f.Total, f.FechaVencimiento, hoy)))
            .ToList();

        var proximoVencimiento = facturas.Count > 0 ? facturas.Min(f => f.FechaVencimiento) : (DateOnly?)null;
        var rango = await rangos.EfectivoAsync(clienteId.Value, ct);

        return Ok(new CuentaPropia(saldo, deudaVencida, servicioCortado, proximoVencimiento, facturas,
            rango?.Nombre ?? "", rango?.DescuentoPct ?? 0m));
    }

    /// <summary>Espejo de LocalidadesController.Buscar (ver comentario arriba de estos records) —
    /// necesario para que SelectorLocalidad funcione dentro de /mis-envios/nuevo.</summary>
    [HttpGet("localidades/buscar")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> BuscarLocalidad([FromQuery] string q, CancellationToken ct)
    {
        q = q.Trim();
        if (q.Length < 2) return Ok(new ResultadoBusquedaLocalidad([], []));

        var tareaExistentes = db.Localidades.AsNoTracking()
            .Where(l => EF.Functions.ILike(l.Nombre, $"%{q}%"))
            .OrderBy(l => l.Nombre)
            .Select(l => new LocalidadResumen(l.Id, l.Nombre, l.Partido, l.ZonaId))
            .ToListAsync(ct);
        var tareaSugerencias = geocodificador.BuscarLocalidadesAsync(q, ct);
        await Task.WhenAll(tareaExistentes, tareaSugerencias);

        var existentes = tareaExistentes.Result;
        var nombresExistentes = new HashSet<string>(existentes.Select(l => l.Nombre), StringComparer.OrdinalIgnoreCase);
        var sugeridas = tareaSugerencias.Result
            .Where(s => !nombresExistentes.Contains(s.Nombre))
            .DistinctBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return Ok(new ResultadoBusquedaLocalidad(existentes, sugeridas));
    }

    /// <summary>Espejo de LocalidadesController.Crear. La zona la decide el servidor midiendo contra
    /// el depósito (nunca el cliente: no acepta coordenadas ni zona en el body).</summary>
    [HttpPost("localidades")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> CrearLocalidad(CrearLocalidadRequest req, CancellationToken ct)
    {
        var nombre = req.Nombre.Trim();
        if (nombre.Length == 0) return BadRequest("El nombre de la localidad no puede estar vacío.");
        var partido = string.IsNullOrWhiteSpace(req.Partido) ? null : req.Partido.Trim();

        var localidad = await zonasLocalidad.CrearAsync(nombre, partido, ct);
        return Ok(new LocalidadResumen(localidad.Id, localidad.Nombre, localidad.Partido, localidad.ZonaId));
    }

    /// <summary>Precio orientativo al elegir la localidad: solo tarifa de zona, con la lista propia
    /// del cliente si la tiene (el cliente sale del claim, nunca de la URL).</summary>
    [HttpGet("localidades/{id:int}/precio-sugerido")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> PrecioSugerido(int id, CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();
        if (!VePrecios) return NotFound();
        var precio = await zonasLocalidad.PrecioSugeridoAsync(id, clienteId.Value, ct);
        return precio is null ? NotFound() : Ok(precio);
    }

    /// <summary>Espejo de UbicacionesController.DesdeMapa: propone dirección y localidad desde un link
    /// de Google Maps; el cliente las confirma o corrige antes de cargar el envío.</summary>
    [HttpPost("ubicaciones/desde-mapa")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> DesdeMapa(DesdeMapaRequest req, CancellationToken ct) =>
        Ok(await desdeMapa.LeerAsync(req.UrlMapa, ct));

    /// <summary>Espejo de UbicacionesController.Resolver — geocodifica al salir del campo
    /// dirección, mismo resolver-o-crear que el alta interna.</summary>
    [HttpPost("ubicaciones")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> ResolverUbicacion(ResolverUbicacionRequest req, CancellationToken ct)
    {
        var r = await ubicaciones.ResolverConMapaAsync(req.CalleNumero, req.LocalidadId, req.Referencia, req.UrlMapa, ct);
        return Ok(new UbicacionResuelta(
            r.Ubicacion.Id, r.Ubicacion.Lat, r.Ubicacion.Lng, r.Ubicacion.GeoConfianza, r.UrlMapaAplicada, r.UrlMapaError));
    }

    /// <summary>
    /// Alta de pedido desde el portal (B5, diseño_b5_portal_carga.md). Calca
    /// PedidosController.Crear (mismo gate de deuda, misma resolución de destino/zona/origen) y
    /// agrega lo propio de B5: corte horario, cotización inmediata con el tipo de vehículo que
    /// eligió el cliente, y esa cotización grabada como precio_manual vinculante (§1) en vez de
    /// quedar null hasta CerrarPlanificacion.
    ///
    /// GuardarComoAsync(usuarioId: null, ...) a propósito, no User.UsuarioId(): ese id es el de
    /// clientes_usuarios, que no existe en `usuarios` — fn_log_estado_pedido lo insertaría en
    /// pedido_eventos.actor_usuario_id y violaría FK_pedido_eventos_usuarios_actor_usuario_id. Con
    /// null, el trigger registra actor_tipo='sistema', mismo camino que ya usan otras escrituras
    /// sin actor de `usuarios` detrás.
    /// </summary>
    [HttpPost("pedidos")]
    public async Task<IActionResult> CrearPedido(CrearPedidoPortalRequest req, CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        if (await ValidarAltaPortalAsync(clienteId.Value, req.FechaEntrega, req.TipoVehiculo, ct) is { } rechazo)
            return rechazo;

        var destino = await db.Ubicaciones.Include(u => u.Localidad)
            .SingleOrDefaultAsync(u => u.Id == req.DestinoUbicacionId, ct);
        if (destino is null) return BadRequest("La ubicación de destino no existe.");

        var zonaId = destino.Localidad?.ZonaId;
        if (zonaId is null)
            return BadRequest("La localidad de destino no tiene zona asignada; no se puede cotizar.");

        var origen = await origenes.PrincipalParaPedidosAsync(ct);

        var pedido = new Pedido
        {
            ClienteId = clienteId.Value,
            OrigenUbicacionId = origen.UbicacionId,
            DestinoUbicacionId = destino.Id,
            DestinatarioNombre = req.DestinatarioNombre.Trim(),
            DestinatarioTelefono = req.DestinatarioTelefono.Trim(),
            Bultos = req.Bultos,
            BultosDeclaradoCliente = req.Bultos,
            PesoKg = req.PesoKg,
            FechaEntrega = req.FechaEntrega,
            Urgente = req.Urgente,
            ZonaId = zonaId,
            Estado = EstadoPedido.Borrador,
            OrigenCarga = "portal",
            Observaciones = req.Observaciones,
            CreadoEn = DateTimeOffset.UtcNow,
            CreadoPorClienteUsuarioId = User.UsuarioId(),
        };

        // B9 (Anexo I §4): si la zona no tiene tarifa cargada para ningún tipo de vehículo, no hay
        // nada que cotizar ni fijar como vinculante — el pedido se guarda igual, sin precio, para
        // que Administración lo fije a mano (diseño_b5_portal_carga.md §5).
        decimal? precioVinculante = null;
        if (await CotizarPortalAsync(clienteId.Value, zonaId.Value, origen, destino, req.FechaEntrega, req.Urgente, req.TipoVehiculo, ct) is { } desglose)
        {
            precioVinculante = desglose.Total;
            // El desglose se guarda ya (el pedido sigue en Borrador, fn_congelar_pedido no actúa todavía):
            // al cerrar la planificación el precio vinculante no se re-cotiza (RutasController.CerrarPlanificacion)
            // y así el detalle muestra de qué está hecho, no un único número.
            pedido.PrecioBase = desglose.PrecioBase;
            pedido.RecargoKm = desglose.RecargoKm;
            pedido.KmCobrados = desglose.KmCobrados;
            pedido.KmFuente = desglose.KmFuente;
            pedido.RecargoUrgencia = desglose.RecargoUrgencia;
            pedido.DescuentoRango = desglose.DescuentoRango;
        }

        if (precioVinculante is not null)
        {
            // Precio TOTAL (base+recargos+urgencia), no solo precio_base como en B9: para el
            // portal no hay un paso posterior que le sume nada más, es el precio final (§1).
            pedido.PrecioManual = precioVinculante;
            pedido.PrecioManualPor = User.UsuarioId();
            pedido.PrecioManualEn = DateTimeOffset.UtcNow;
        }

        db.Pedidos.Add(pedido);
        await db.GuardarComoAsync(usuarioId: null, motivo: "Alta desde portal del cliente", ct);

        // Después del alta: el id del pedido recién existe ahora. Si esto fallara, el pedido ya
        // quedó cargado (lo que importa) y su autor igual está en creado_por_cliente_usuario_id.
        ActividadPortal.Registrar(db, User, AccionesPortal.PedidoCargado, "pedido", pedido.Id.ToString(),
            $"{pedido.DestinatarioNombre} · {destino.CalleNumero} · entrega {pedido.FechaEntrega:dd/MM}");
        await db.SaveChangesAsync(ct);

        // El precio es solo para el dueño; el empleado ve si quedó pendiente de cotización, no el monto.
        return CreatedAtAction("Detalle", "Pedidos", new { id = pedido.Id }, new PedidoPortalCreado(
            pedido.Id, pedido.FechaEntrega, VePrecios ? precioVinculante : null,
            RequiereCotizacion: precioVinculante is null));
    }

    /// <summary>
    /// Lo que se valida antes de cualquier alta del portal (un envío o un viaje). null si pasa.
    /// §6: corte de las 16:00 (provisional, configurable — Opciones/OpcionesPortal.cs). Es el cierre
    /// del LOTE DE HOY (16:00-18:00 es la ventana de recepción, B13 §1), no un bloqueo general del
    /// portal — un pedido para mañana entra a cualquier hora. Se valida la hora del servidor al
    /// confirmar, no solo al mostrar el formulario, para que dejar la pantalla abierta no permita
    /// colarse en el lote de hoy después del corte.
    /// </summary>
    private async Task<IActionResult?> ValidarAltaPortalAsync(int clienteId, DateOnly fechaEntrega, string tipoVehiculo, CancellationToken ct)
    {
        if (tipoVehiculo is not ("camioneta" or "moto"))
            return BadRequest("Tipo de vehículo inválido.");

        var hoy = Reloj.HoyLocal();
        // Ni en el pasado (antes solo se frenaba pasada la hora de corte) ni a más de 90 días.
        var errorFecha = ValidacionFechas.FechaEntrega(fechaEntrega, hoy, diasAtras: 0, diasAdelante: 90);
        if (errorFecha is not null) return BadRequest(errorFecha);
        var horaCorte = opcionesPortal.Value.HoraCorte;
        if (fechaEntrega <= hoy && Reloj.HoraLocal() > horaCorte)
        {
            return Conflict(new
            {
                mensaje = $"La carga de hoy cerró a las {horaCorte:HH\\:mm}; esto se carga para mañana.",
                fechaEntregaSugerida = hoy.AddDays(1),
            });
        }

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null || !cliente.Activo) return Conflict("Tu cuenta está inactiva; no admite pedidos nuevos.");

        var suspendido = cliente.CorteSuspendidoHasta is { } h && h >= hoy;
        if (!suspendido)
        {
            var deuda = await cuentaCorriente.DeudaVencidaAsync(clienteId, hoy, ct);
            if (deuda > 0)
                return Conflict(
                    $"Servicio cortado por deuda vencida: adeudás ${deuda:N2} de comprobantes vencidos. " +
                    "Los pedidos ya confirmados o en ruta no se ven afectados. Se rehabilita solo al pagar " +
                    "el total o con un plan de cuotas.");
        }
        return null;
    }

    /// <summary>Precio vinculante de un envío del portal (B5 §1) con el vehículo que eligió el cliente.
    /// null = B9 (Anexo I §4): la zona no tiene tarifa para ningún vehículo; el envío se guarda igual,
    /// sin precio, para que Administración lo fije (diseño_b5_portal_carga.md §5).</summary>
    private async Task<DesglosePrecio?> CotizarPortalAsync(
        int clienteId, int zonaId, Deposito origen, Ubicacion destino, DateOnly fechaEntrega, bool urgente,
        string tipoVehiculo, CancellationToken ct)
    {
        try
        {
            var distancia = await distancias.ResolverAsync(origen.Lat, origen.Lng, destino.Lat, destino.Lng, kmManual: null, ct);
            return await precios.CotizarAsync(
                clienteId, zonaId, fechaEntrega, urgente,
                peajes: 0m, descuentoRuta: false, tipoVehiculo, precioManual: null, distancia, ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ---- Viajes: un envío con varias paradas y su ruta propuesta (dueño y empleado) ----

    public record ViajePortalRequest(
        DateOnly FechaEntrega, bool Urgente, string TipoVehiculo, string? Observaciones,
        List<ParadaViajeEntrada> Paradas);

    public record PrecioParadaPortal(int Indice, decimal? Precio);
    public record PrevisualizacionViajePortal(PrevisualizacionViaje Recorrido, List<PrecioParadaPortal>? Precios, decimal? Total);
    public record ViajePortalCreado(long Id, long? RutaId, int Paradas, int ParadasFueraDeRuta, decimal? Total);

    /// <summary>Validación, precios por parada (la regla provisoria: cada parada cotiza como un envío, acta
    /// changelog 4.29) y ubicaciones, comunes a previsualizar y crear.</summary>
    private async Task<(List<DesglosePrecio?>? Precios, IActionResult? Error)> PrepararViajePortalAsync(
        int clienteId, ViajePortalRequest req, int minimoParadas, CancellationToken ct)
    {
        if (ViajeService.ValidarParadas(req.Paradas, opcionesPortal.Value.MaxParadasViaje, minimoParadas) is { } error)
            return (null, BadRequest(error));
        if (await ValidarAltaPortalAsync(clienteId, req.FechaEntrega, req.TipoVehiculo, ct) is { } rechazo)
            return (null, rechazo);

        var ids = req.Paradas.Select(p => p.DestinoUbicacionId).Distinct().ToList();
        var destinos = await db.Ubicaciones.Include(u => u.Localidad).Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        if (destinos.Count != ids.Count) return (null, BadRequest("Alguna de las direcciones de destino no existe."));
        var sinZona = destinos.Values.FirstOrDefault(u => u.Localidad?.ZonaId is null);
        if (sinZona is not null)
            return (null, BadRequest($"La localidad de {sinZona.CalleNumero} no tiene zona asignada; no se puede cotizar."));

        var origen = await origenes.PrincipalParaPedidosAsync(ct);
        var cotizados = new List<DesglosePrecio?>();
        foreach (var p in req.Paradas)
        {
            var destino = destinos[p.DestinoUbicacionId];
            cotizados.Add(await CotizarPortalAsync(clienteId, destino.Localidad!.ZonaId!.Value, origen, destino,
                req.FechaEntrega, req.Urgente, req.TipoVehiculo, ct));
        }
        return (cotizados, null);
    }

    /// <summary>Orden sugerido, km y recorrido de un viaje, sin guardar nada. Precios solo para el dueño.</summary>
    [HttpPost("viajes/previsualizar")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> PrevisualizarViaje(ViajePortalRequest req, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var (cotizados, error) = await PrepararViajePortalAsync(clienteId, req, minimoParadas: 1, ct);
        if (cotizados is null) return error!;

        var (previa, errorPrevia) = await viajes.PrevisualizarAsync(req.Paradas, ct);
        if (previa is null) return BadRequest(errorPrevia);

        var dueno = VePrecios;
        return Ok(new PrevisualizacionViajePortal(
            previa,
            dueno ? cotizados.Select((d, i) => new PrecioParadaPortal(i, d?.Total)).ToList() : null,
            dueno && cotizados.All(d => d is not null) ? cotizados.Sum(d => d!.Total) : null));
    }

    /// <summary>Crea el viaje con las paradas en el orden recibido (el sugerido o el que el cliente
    /// ajustó) y su ruta propuesta, que Operación revisa antes de salir (acta §2).</summary>
    [HttpPost("viajes")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> CrearViaje(ViajePortalRequest req, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var (cotizados, error) = await PrepararViajePortalAsync(clienteId, req, minimoParadas: 2, ct);
        if (cotizados is null) return error!;

        var (previa, _) = await viajes.PrevisualizarAsync(req.Paradas, ct);
        var (creado, errorAlta) = await viajes.CrearAsync(
            new NuevoViaje(clienteId, req.FechaEntrega, req.Urgente, req.TipoVehiculo, req.Observaciones,
                ClienteUsuarioId: User.UsuarioId(), UsuarioId: null),
            req.Paradas, cotizados, previa?.Km, "portal", ct);
        if (creado is null) return BadRequest(errorAlta);

        ActividadPortal.Registrar(db, User, AccionesPortal.ViajeCargado, "viaje", creado.Id.ToString(),
            $"{req.Paradas.Count} paradas · entrega {req.FechaEntrega:dd/MM}");
        await db.SaveChangesAsync(ct);

        var total = VePrecios && cotizados.All(d => d is not null) ? cotizados.Sum(d => d!.Total) : (decimal?)null;
        return Ok(new ViajePortalCreado(creado.Id, creado.RutaId, creado.PedidoIds.Count, creado.ParadasFueraDeRuta, total));
    }

    [HttpGet("viajes")]
    public async Task<IActionResult> ListarViajes([FromQuery] int? pagina, [FromQuery] int? tamanioPagina, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();
        return Ok(await viajes.ListarAsync(clienteId, pagina, tamanioPagina, ct));
    }

    [HttpGet("viajes/{id:long}")]
    public async Task<IActionResult> Viaje(long id, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();
        var detalle = await viajes.DetalleAsync(id, clienteId, conPrecios: VePrecios, ct);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    /// <summary>El dueño cancela el viaje entero mientras no salió (ruta en planificación y todos los
    /// envíos en Borrador). Sin costo (§10.2-I).</summary>
    [HttpPost("viajes/{id:long}/cancelar")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> CancelarViaje(long id, CancelarPedidoPortalRequest req, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var motivo = string.IsNullOrWhiteSpace(req.Motivo) ? null : req.Motivo.Trim();
        var error = await viajes.CancelarAsync(id, clienteId, usuarioId: null,
            $"Viaje #{id} cancelado por el cliente desde el portal ({User.FindFirst("nombre")?.Value}){(motivo is null ? "" : $": {motivo}")}", ct);
        if (error == "no_existe") return NotFound();
        if (error is not null) return Conflict(error);

        ActividadPortal.Registrar(db, User, AccionesPortal.ViajeCancelado, "viaje", id.ToString(), motivo);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>"Mis clientes": libreta de destinatarios que el cliente registra a mano (a
    /// diferencia de destinatarios-frecuentes, derivado del historial — este es un registro
    /// explícito, reversión de la decisión de acta 3.5, a pedido del cliente).</summary>
    [HttpGet("destinatarios")]
    public async Task<IActionResult> ListarDestinatarios(CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var destinatarios = await db.ClientesDestinatarios.AsNoTracking()
            .Where(d => d.ClienteId == clienteId.Value)
            .OrderBy(d => d.Nombre)
            .Select(d => new ClienteDestinatarioResumen(
                d.Id, d.Nombre, d.Telefono,
                d.DestinoUbicacionId, d.DestinoUbicacion.CalleNumero,
                d.DestinoUbicacion.LocalidadId ?? 0,
                d.DestinoUbicacion.Localidad != null ? d.DestinoUbicacion.Localidad.Nombre : null,
                d.DestinoUbicacion.Lat, d.DestinoUbicacion.Lng, d.DestinoUbicacion.GeoConfianza,
                d.Observaciones))
            .ToListAsync(ct);

        return Ok(destinatarios);
    }

    [HttpPost("destinatarios")]
    public async Task<IActionResult> CrearDestinatario(GuardarClienteDestinatarioRequest req, CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var destino = await db.Ubicaciones.Include(u => u.Localidad)
            .SingleOrDefaultAsync(u => u.Id == req.DestinoUbicacionId, ct);
        if (destino is null) return BadRequest("La ubicación de destino no existe.");

        var nuevo = new ClienteDestinatario
        {
            ClienteId = clienteId.Value,
            Nombre = req.Nombre.Trim(),
            Telefono = req.Telefono.Trim(),
            DestinoUbicacionId = destino.Id,
            Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim(),
            CreadoEn = DateTimeOffset.UtcNow,
        };
        db.ClientesDestinatarios.Add(nuevo);
        ActividadPortal.Registrar(db, User, AccionesPortal.ContactoCreado, "contacto", nuevo.Id.ToString(), nuevo.Nombre);
        await db.SaveChangesAsync(ct);

        return Ok(new ClienteDestinatarioResumen(
            nuevo.Id, nuevo.Nombre, nuevo.Telefono, destino.Id, destino.CalleNumero,
            destino.LocalidadId ?? 0, destino.Localidad?.Nombre, destino.Lat, destino.Lng,
            destino.GeoConfianza, nuevo.Observaciones));
    }

    [HttpPut("destinatarios/{id:guid}")]
    public async Task<IActionResult> EditarDestinatario(Guid id, GuardarClienteDestinatarioRequest req, CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var existente = await db.ClientesDestinatarios.SingleOrDefaultAsync(d => d.Id == id && d.ClienteId == clienteId.Value, ct);
        if (existente is null) return NotFound();

        var destino = await db.Ubicaciones.Include(u => u.Localidad)
            .SingleOrDefaultAsync(u => u.Id == req.DestinoUbicacionId, ct);
        if (destino is null) return BadRequest("La ubicación de destino no existe.");

        existente.Nombre = req.Nombre.Trim();
        existente.Telefono = req.Telefono.Trim();
        existente.DestinoUbicacionId = destino.Id;
        existente.Observaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim();
        ActividadPortal.Registrar(db, User, AccionesPortal.ContactoEditado, "contacto", existente.Id.ToString(), existente.Nombre);
        await db.SaveChangesAsync(ct);

        return Ok(new ClienteDestinatarioResumen(
            existente.Id, existente.Nombre, existente.Telefono, destino.Id, destino.CalleNumero,
            destino.LocalidadId ?? 0, destino.Localidad?.Nombre, destino.Lat, destino.Lng,
            destino.GeoConfianza, existente.Observaciones));
    }

    [HttpDelete("destinatarios/{id:guid}")]
    public async Task<IActionResult> BorrarDestinatario(Guid id, CancellationToken ct)
    {
        var clienteId = User.ClienteId();
        if (clienteId is null) return Forbid();

        var existente = await db.ClientesDestinatarios.SingleOrDefaultAsync(d => d.Id == id && d.ClienteId == clienteId.Value, ct);
        if (existente is null) return NotFound();

        db.ClientesDestinatarios.Remove(existente);
        ActividadPortal.Registrar(db, User, AccionesPortal.ContactoEliminado, "contacto", existente.Id.ToString(), existente.Nombre);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Equipo: el dueño administra a sus empleados y ve cómo trabajan ----

    public record UsuarioEquipo(Guid Id, string Nombre, string Email, string Rol, bool Activo, DateTimeOffset CreadoEn);
    public record CrearUsuarioEquipoRequest(string Nombre, string Email, string Password);
    public record ActivoEquipoRequest(bool Activo);
    public record PasswordEquipoRequest(string Password);

    public record ActividadEquipo(
        long Id, Guid UsuarioId, string UsuarioNombre, string Accion,
        string? EntidadTipo, string? EntidadId, string? Detalle, DateTimeOffset OcurridoEn);

    [HttpGet("usuarios")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> ListarUsuarios(CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        return Ok(await db.ClientesUsuarios.AsNoTracking()
            .Where(u => u.ClienteId == clienteId)
            .OrderBy(u => u.Rol).ThenBy(u => u.Nombre)
            .Select(u => new UsuarioEquipo(u.Id, u.Nombre, u.Email, u.Rol, u.Activo, u.CreadoEn))
            .ToListAsync(ct));
    }

    /// <summary>El dueño solo da de alta empleados (rol "usuario"); otro dueño lo crea
    /// Administración desde el BackOffice.</summary>
    [HttpPost("usuarios")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> CrearUsuario(CrearUsuarioEquipoRequest req, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var (usuario, error) = AltaClienteUsuario.Crear(clienteId, req.Nombre, req.Email, req.Password, RolesCliente.Usuario);
        if (usuario is null) return BadRequest(error);

        db.ClientesUsuarios.Add(usuario);
        ActividadPortal.Registrar(db, User, AccionesPortal.UsuarioCreado, "usuario", usuario.Id.ToString(), usuario.Nombre);
        await db.SaveChangesAsync(ct);

        return Ok(new UsuarioEquipo(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.Activo, usuario.CreadoEn));
    }

    /// <summary>Desactivar corta la sesión del empleado en el próximo refresh (AuthService
    /// re-chequea Activo) y le impide volver a entrar.</summary>
    [HttpPut("usuarios/{id:guid}/activo")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> CambiarActivoUsuario(Guid id, ActivoEquipoRequest req, CancellationToken ct)
    {
        var (usuario, error) = await UsuarioGestionableAsync(id, ct);
        if (usuario is null) return error!;

        usuario.Activo = req.Activo;
        ActividadPortal.Registrar(db, User,
            req.Activo ? AccionesPortal.UsuarioActivado : AccionesPortal.UsuarioDesactivado,
            "usuario", usuario.Id.ToString(), usuario.Nombre);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("usuarios/{id:guid}/password")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> CambiarPasswordUsuario(Guid id, PasswordEquipoRequest req, CancellationToken ct)
    {
        var (usuario, error) = await UsuarioGestionableAsync(id, ct);
        if (usuario is null) return error!;
        if (PoliticaContrasena.Validar(req.Password, usuario.Email) is { } errorClave) return BadRequest(errorClave);

        usuario.PasswordHash = AuthService.HashearCliente(usuario, req.Password);
        ActividadPortal.Registrar(db, User, AccionesPortal.UsuarioContrasena, "usuario", usuario.Id.ToString(), usuario.Nombre);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>El login `id` si el dueño de la sesión puede administrarlo (EquipoCliente.ValidarGestion);
    /// si no, la respuesta de error. Uno de otra empresa responde 404, igual que si no existiera.</summary>
    private async Task<(ClienteUsuario? Usuario, IActionResult? Error)> UsuarioGestionableAsync(Guid id, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return (null, Forbid());

        var usuario = await db.ClientesUsuarios.SingleOrDefaultAsync(u => u.Id == id && u.ClienteId == clienteId, ct);
        if (usuario is null) return (null, NotFound());

        var motivo = EquipoCliente.ValidarGestion(User.UsuarioId(), clienteId,
            new EquipoCliente.LoginObjetivo(usuario.Id, usuario.ClienteId, usuario.Rol));
        return motivo is null ? (usuario, null) : (null, BadRequest(motivo));
    }

    /// <summary>Envíos cargados por cada login de la empresa entre `desde` y `hasta` (fecha local de
    /// carga, ambos inclusive; por defecto los últimos 30 días), con cómo terminaron.</summary>
    [HttpGet("equipo/resumen")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> ResumenEquipo([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var (inicio, fin) = RangoDeCarga(desde, hasta);
        if (inicio > fin) return BadRequest("La fecha desde no puede ser posterior a la fecha hasta.");

        var logins = await db.ClientesUsuarios.AsNoTracking()
            .Where(u => u.ClienteId == clienteId)
            .Select(u => new EquipoCliente.Login(u.Id, u.Nombre, u.Rol, u.Activo))
            .ToListAsync(ct);

        var (desdeUtc, _) = Reloj.RangoLocalUtc(inicio);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(fin);
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.CreadoPorClienteUsuarioId != null
                && p.CreadoEn >= desdeUtc && p.CreadoEn < hastaUtc)
            .Select(p => new EquipoCliente.PedidoCargado(p.CreadoPorClienteUsuarioId!.Value, p.Estado))
            .ToListAsync(ct);

        return Ok(new { desde = inicio, hasta = fin, filas = EquipoCliente.Resumir(logins, pedidos) });
    }

    /// <summary>Historial de lo que hizo cada login de la empresa, más nuevo primero.</summary>
    [HttpGet("equipo/actividad")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> ActividadDelEquipo(
        [FromQuery] Guid? usuarioId, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        [FromQuery] int? pagina, [FromQuery] int? tamanioPagina, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var (inicio, fin) = RangoDeCarga(desde, hasta);
        if (inicio > fin) return BadRequest("La fecha desde no puede ser posterior a la fecha hasta.");
        var (desdeUtc, _) = Reloj.RangoLocalUtc(inicio);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(fin);

        var query = db.ClientesUsuariosActividad.AsNoTracking()
            .Where(a => a.ClienteId == clienteId && a.OcurridoEn >= desdeUtc && a.OcurridoEn < hastaUtc);
        if (usuarioId is not null) query = query.Where(a => a.ClienteUsuarioId == usuarioId.Value);

        var total = await query.CountAsync(ct);

        var tamanio = Paginacion.TamanioEfectivo(tamanioPagina);
        var paginaActual = pagina is > 0 ? Math.Min(pagina.Value, 1_000_000) : 1;
        var items = await query
            .OrderByDescending(a => a.OcurridoEn).ThenByDescending(a => a.Id)
            .Skip((paginaActual - 1) * tamanio).Take(tamanio)
            .Select(a => new ActividadEquipo(
                a.Id, a.ClienteUsuarioId, a.ClienteUsuario.Nombre, a.Accion,
                a.EntidadTipo, a.EntidadId, a.Detalle, a.OcurridoEn))
            .ToListAsync(ct);

        return Ok(new ListaPaginada<ActividadEquipo>(items, total));
    }

    private static (DateOnly Desde, DateOnly Hasta) RangoDeCarga(DateOnly? desde, DateOnly? hasta)
    {
        var fin = hasta ?? Reloj.HoyLocal();
        return (desde ?? fin.AddDays(-29), fin);
    }

    // ---- Mi negocio: el dueño controla envíos, gasto, cuenta y pagos por día, semana y mes ----

    public record ResumenPeriodo(
        DateOnly Desde, DateOnly Hasta, NegocioCliente.ConteoEnvios Envios,
        decimal? Facturable, decimal? Comprometido, int SinPrecio);

    public record DiaNegocio(DateOnly Fecha, int Envios, int Entregados, decimal? Facturable);

    public record ResumenNegocio(string Periodo, ResumenPeriodo Actual, ResumenPeriodo Anterior, List<DiaNegocio> Dias);

    /// <summary>Control del período (día, semana de lunes a domingo o mes calendario) que contiene a
    /// `fecha`, contra el período anterior. Envíos por fecha de entrega. "Facturable" = cargos ya
    /// generados (entregas, cancelaciones cobradas, ajustes aprobados) en el período; "Comprometido"
    /// = precio vinculante de los envíos del período que todavía no terminaron.</summary>
    [HttpGet("negocio/resumen")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> ResumenDelNegocio([FromQuery] string? periodo, [FromQuery] DateOnly? fecha, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var tipo = periodo ?? NegocioCliente.Semana;
        if (NegocioCliente.Periodo(tipo, fecha ?? Reloj.HoyLocal()) is not { } periodos)
            return BadRequest("Período inválido: dia, semana o mes.");

        var actual = await ResumirPeriodoAsync(clienteId, periodos.Actual, conDias: true, ct);
        var anterior = await ResumirPeriodoAsync(clienteId, periodos.Anterior, conDias: false, ct);
        return Ok(new ResumenNegocio(tipo, actual.Resumen, anterior.Resumen, actual.Dias));
    }

    private async Task<(ResumenPeriodo Resumen, List<DiaNegocio> Dias)> ResumirPeriodoAsync(
        int clienteId, NegocioCliente.RangoFechas rango, bool conDias, CancellationToken ct)
    {
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.FechaEntrega >= rango.Desde && p.FechaEntrega <= rango.Hasta)
            .Select(p => new { p.Estado, p.FechaEntrega, Precio = p.PrecioManual ?? p.Total })
            .ToListAsync(ct);

        var (desdeUtc, _) = Reloj.RangoLocalUtc(rango.Desde);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(rango.Hasta);
        var cargos = (await db.FacturaItems.AsNoTracking()
                .Where(i => i.Estado == "aprobado" && i.Monto != null && i.Pedido!.ClienteId == clienteId
                    && i.CreadoEn >= desdeUtc && i.CreadoEn < hastaUtc)
                .Select(i => new { i.Monto, i.CreadoEn })
                .ToListAsync(ct))
            .Select(i => (Fecha: Reloj.ALaFechaLocal(i.CreadoEn), Monto: i.Monto!.Value))
            .ToList();

        var enCurso = pedidos.Where(p => NegocioCliente.Grupo(p.Estado) == NegocioCliente.GrupoEstado.EnCurso).ToList();
        var resumen = new ResumenPeriodo(
            rango.Desde, rango.Hasta,
            NegocioCliente.Contar(pedidos.Select(p => p.Estado)),
            // Acta changelog 4.30: son montos de envío; null si esta sesión no ve precios.
            Facturable: VePrecios ? cargos.Sum(c => c.Monto) : null,
            Comprometido: VePrecios ? enCurso.Sum(p => p.Precio ?? 0m) : null,
            SinPrecio: enCurso.Count(p => p.Precio is null));

        var dias = new List<DiaNegocio>();
        if (conDias)
        {
            for (var d = rango.Desde; d <= rango.Hasta; d = d.AddDays(1))
            {
                var delDia = pedidos.Where(p => p.FechaEntrega == d).ToList();
                dias.Add(new DiaNegocio(d, delDia.Count, delDia.Count(p => p.Estado == EstadoPedido.Entregado),
                    VePrecios ? cargos.Where(c => c.Fecha == d).Sum(c => c.Monto) : null));
            }
        }
        return (resumen, dias);
    }

    public record EstadoDeCuentaPropio(
        DateOnly Desde, DateOnly Hasta, decimal SaldoInicial, decimal SaldoFinal,
        decimal SaldoActual, decimal DeudaVencida, decimal PendienteDeFacturar, bool ServicioCortado,
        List<NegocioCliente.Movimiento> Movimientos);

    /// <summary>Resumen tipo extracto bancario: facturas (debe) y pagos imputados (haber) del período,
    /// con saldo acumulado. Por defecto, los últimos 90 días. Los pagos informados todavía sin
    /// confirmar no figuran: no mueven el saldo hasta que Administración los imputa.</summary>
    [HttpGet("estado-de-cuenta")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> EstadoDeCuenta([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var hoy = Reloj.HoyLocal();
        var fin = hasta ?? hoy;
        var inicio = desde ?? fin.AddDays(-89);
        if (inicio > fin) return BadRequest("La fecha desde no puede ser posterior a la fecha hasta.");

        var facturadoAntes = await db.Facturas.Where(f => f.ClienteId == clienteId && f.FechaEmision < inicio)
            .SumAsync(f => (decimal?)f.Total, ct) ?? 0m;
        var pagadoAntes = await db.Pagos.Where(p => p.ClienteId == clienteId && p.FechaPago < inicio)
            .SumAsync(p => (decimal?)p.Monto, ct) ?? 0m;
        var saldoInicial = facturadoAntes - pagadoAntes;

        var cargos = await db.Facturas.AsNoTracking()
            .Where(f => f.ClienteId == clienteId && f.FechaEmision >= inicio && f.FechaEmision <= fin)
            .Select(f => new NegocioCliente.Cargo(f.Id, f.FechaEmision, f.PeriodoDesde, f.PeriodoHasta, f.Total))
            .ToListAsync(ct);
        // Nota interna de Administración fuera: al cliente solo le llega el medio (y la de una corrección).
        var abonos = await db.Pagos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.FechaPago >= inicio && p.FechaPago <= fin)
            .Select(p => new NegocioCliente.Abono(p.Id, p.FechaPago, p.Medio, p.Monto < 0 ? p.Nota : null, p.Monto))
            .ToListAsync(ct);

        var movimientos = NegocioCliente.EstadoDeCuenta(saldoInicial, cargos, abonos);
        var deudaVencida = await cuentaCorriente.DeudaVencidaAsync(clienteId, hoy, ct);
        var pendiente = await db.FacturaItems.AsNoTracking()
            .Where(i => i.FacturaId == null && i.Estado == "aprobado" && i.Pedido!.ClienteId == clienteId)
            .SumAsync(i => (decimal?)i.Monto, ct) ?? 0m;

        return Ok(new EstadoDeCuentaPropio(
            inicio, fin, saldoInicial, movimientos.Count > 0 ? movimientos[^1].Saldo : saldoInicial,
            await cuentaCorriente.SaldoAsync(clienteId, ct), deudaVencida, pendiente,
            await cuentaCorriente.ServicioCortadoAsync(clienteId, hoy, deudaVencida, ct),
            movimientos));
    }

    public record FacturaItemPropio(long? PedidoId, string Tipo, string Descripcion, decimal? Monto);
    public record FacturaPropiaDetalle(
        long Id, DateOnly PeriodoDesde, DateOnly PeriodoHasta, DateOnly FechaEmision, DateOnly FechaVencimiento,
        decimal Total, decimal Pagado, decimal Saldo, string Estado, List<FacturaItemPropio> Items);

    /// <summary>Todas las facturas (pagadas incluidas), más nueva primero.</summary>
    [HttpGet("facturas")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> Facturas(CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var hoy = Reloj.HoyLocal();
        return Ok((await db.Set<FacturaSaldo>().AsNoTracking()
                .Where(f => f.ClienteId == clienteId)
                .OrderByDescending(f => f.FechaEmision).ThenByDescending(f => f.Id)
                .Take(Paginacion.TopeSinPaginar)
                .ToListAsync(ct))
            .Select(f => new FacturaPropia(
                f.Id, f.PeriodoDesde, f.PeriodoHasta, f.FechaVencimiento, f.Total, f.Saldo,
                EstadoDe(f.Saldo, f.Total, f.FechaVencimiento, hoy))));
    }

    /// <summary>Espejo acotado de FacturasController.Detalle (Administración de clase): solo facturas
    /// propias, y los ítems sin quién los creó.</summary>
    [HttpGet("facturas/{id:long}")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> Factura(long id, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var f = await db.Set<FacturaSaldo>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ClienteId == clienteId, ct);
        if (f is null) return NotFound();

        var items = await db.FacturaItems.AsNoTracking()
            .Where(i => i.FacturaId == id)
            .OrderBy(i => i.CreadoEn)
            .Select(i => new FacturaItemPropio(i.PedidoId, i.Tipo, i.Descripcion, i.Monto))
            .ToListAsync(ct);

        return Ok(new FacturaPropiaDetalle(
            f.Id, f.PeriodoDesde, f.PeriodoHasta, f.FechaEmision, f.FechaVencimiento,
            f.Total, f.Pagado, f.Saldo, EstadoDe(f.Saldo, f.Total, f.FechaVencimiento, Reloj.HoyLocal()), items));
    }

    public record PagoPropio(long Id, DateOnly FechaPago, decimal Monto, string Medio);
    public record PagoInformadoPropio(
        long Id, decimal Monto, DateOnly FechaPago, string Medio, string? Nota, bool TieneComprobante,
        string Estado, string? MotivoRechazo, string InformadoPor, DateTimeOffset CreadoEn, DateTimeOffset? RevisadoEn);
    public record PagosPropios(List<PagoPropio> Imputados, List<PagoInformadoPropio> Informados);

    [HttpGet("pagos")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> Pagos(CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var imputados = await db.Pagos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.FechaPago).ThenByDescending(p => p.Id)
            .Take(Paginacion.TopeSinPaginar)
            .Select(p => new PagoPropio(p.Id, p.FechaPago, p.Monto, p.Medio))
            .ToListAsync(ct);
        var informados = await db.PagosInformados.AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.CreadoEn)
            .Take(Paginacion.TopeSinPaginar)
            .Select(p => new PagoInformadoPropio(
                p.Id, p.Monto, p.FechaPago, p.Medio, p.Nota, p.ComprobantePath != null,
                p.Estado, p.MotivoRechazo, p.ClienteUsuario.Nombre, p.CreadoEn, p.RevisadoEn))
            .ToListAsync(ct);

        return Ok(new PagosPropios(imputados, informados));
    }

    /// <summary>Clase, no record posicional: [FromForm] + IFormFile (mismo motivo que
    /// MisParadasController.CerrarParadaRequest).</summary>
    public class InformarPagoRequest
    {
        public decimal Monto { get; set; }
        public DateOnly FechaPago { get; set; }
        public string Medio { get; set; } = null!;
        public string? Nota { get; set; }
        public IFormFile? Comprobante { get; set; }
    }

    /// <summary>El dueño avisa un pago. No toca el saldo: Administración lo confirma (y ahí se crea el
    /// Pago) o lo rechaza. El comprobante es una foto JPEG opcional.</summary>
    [HttpPost("pagos-informados")]
    [Authorize(Policy = "ClienteDueno")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> InformarPago([FromForm] InformarPagoRequest req, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        if (req.Monto <= 0) return BadRequest("El monto debe ser mayor a cero.");
        if (!NegocioCliente.MediosPago.Contains(req.Medio)) return BadRequest("Medio de pago inválido.");
        var hoy = Reloj.HoyLocal();
        if (req.FechaPago > hoy) return BadRequest("La fecha del pago no puede ser futura.");
        if (req.FechaPago < hoy.AddDays(-365)) return BadRequest("La fecha del pago es de hace más de un año.");

        // La foto se escribe antes de la base, igual que la prueba de entrega: si falla la base queda
        // un archivo huérfano inofensivo; al revés quedaría una fila apuntando a nada.
        string? comprobante = null;
        if (req.Comprobante is not null)
        {
            await using var stream = req.Comprobante.OpenReadStream();
            comprobante = await almacenamiento.GuardarAsync(
                clienteId, Guid.NewGuid().ToString(), stream, req.Comprobante.Length, ct, carpeta: "comprobantes");
        }

        var informado = new PagoInformado
        {
            ClienteId = clienteId,
            ClienteUsuarioId = User.UsuarioId(),
            Monto = req.Monto,
            FechaPago = req.FechaPago,
            Medio = req.Medio,
            Nota = string.IsNullOrWhiteSpace(req.Nota) ? null : req.Nota.Trim(),
            ComprobantePath = comprobante,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        db.PagosInformados.Add(informado);
        ActividadPortal.Registrar(db, User, AccionesPortal.PagoInformado, "pago_informado", null,
            $"${req.Monto:N2} · {req.Medio} · {req.FechaPago:dd/MM}");
        await db.SaveChangesAsync(ct);

        return Ok(new { informado.Id });
    }

    [HttpGet("pagos-informados/{id:long}/comprobante")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> ComprobantePropio(long id, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return Forbid();

        var path = await db.PagosInformados.Where(p => p.Id == id && p.ClienteId == clienteId)
            .Select(p => p.ComprobantePath).SingleOrDefaultAsync(ct);
        if (path is null) return NotFound();
        var stream = await almacenamiento.AbrirAsync(path, ct);
        return stream is null ? NotFound() : File(stream, "image/jpeg");
    }

    public record CancelarPedidoPortalRequest(string? Motivo);
    public record EditarPedidoPortalRequest(string? DestinatarioNombre, string? DestinatarioTelefono, string? Observaciones);

    /// <summary>
    /// El dueño cancela un envío propio que sigue en Borrador (gratis, §10.2-I) y que todavía no
    /// entró en el armado de ninguna ruta: si ya está en una, lo resuelve la Empresa, que sabe cómo
    /// quedó esa ruta. GuardarComoAsync(usuarioId: null) por el mismo motivo que CrearPedido: el id
    /// del login del portal no existe en `usuarios`.
    /// </summary>
    [HttpPost("pedidos/{id:long}/cancelar")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> CancelarPedido(long id, CancelarPedidoPortalRequest req, CancellationToken ct)
    {
        var (pedido, error) = await PedidoEnBorradorAsync(id, ct);
        if (pedido is null) return error!;

        if (await db.ParadaPedidos.AnyAsync(pp => pp.PedidoId == id, ct))
            return Conflict("Este envío ya está en el armado de una ruta; para cancelarlo, contactá a la Empresa.");
        if (!TransicionesPedido.Permitida(pedido.Estado, EstadoPedido.Cancelado))
            return Conflict("Este envío ya no se puede cancelar.");

        var motivo = string.IsNullOrWhiteSpace(req.Motivo) ? null : req.Motivo.Trim();
        pedido.Estado = EstadoPedido.Cancelado;
        ActividadPortal.Registrar(db, User, AccionesPortal.PedidoCancelado, "pedido", pedido.Id.ToString(),
            $"{pedido.DestinatarioNombre}{(motivo is null ? "" : $" · {motivo}")}");
        await db.GuardarComoAsync(usuarioId: null,
            motivo: $"Cancelado por el cliente desde el portal ({User.FindFirst("nombre")?.Value}){(motivo is null ? "" : $": {motivo}")}", ct);
        return NoContent();
    }

    /// <summary>Corrige nombre, teléfono y observaciones de un envío propio en Borrador. Una vez
    /// confirmado, los cambios los hace la Empresa (PedidosController.EditarContacto avisa al
    /// repartidor si ya salió).</summary>
    [HttpPut("pedidos/{id:long}")]
    [Authorize(Policy = "ClienteDueno")]
    public async Task<IActionResult> EditarPedido(long id, EditarPedidoPortalRequest req, CancellationToken ct)
    {
        if (req.DestinatarioNombre is not null && string.IsNullOrWhiteSpace(req.DestinatarioNombre))
            return BadRequest("El nombre del destinatario no puede quedar en blanco.");
        if (req.DestinatarioTelefono is not null && string.IsNullOrWhiteSpace(req.DestinatarioTelefono))
            return BadRequest("El teléfono no puede quedar en blanco.");

        var (pedido, error) = await PedidoEnBorradorAsync(id, ct);
        if (pedido is null) return error!;

        var cambios = new List<string>();
        if (req.DestinatarioNombre is not null && req.DestinatarioNombre.Trim() != pedido.DestinatarioNombre)
        {
            cambios.Add($"destinatario {pedido.DestinatarioNombre} → {req.DestinatarioNombre.Trim()}");
            pedido.DestinatarioNombre = req.DestinatarioNombre.Trim();
        }
        if (req.DestinatarioTelefono is not null && req.DestinatarioTelefono.Trim() != pedido.DestinatarioTelefono)
        {
            cambios.Add($"teléfono {pedido.DestinatarioTelefono} → {req.DestinatarioTelefono.Trim()}");
            pedido.DestinatarioTelefono = req.DestinatarioTelefono.Trim();
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

        ActividadPortal.Registrar(db, User, AccionesPortal.PedidoEditado, "pedido", pedido.Id.ToString(), string.Join("; ", cambios));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>El pedido `id` si es del cliente de la sesión y sigue en Borrador; si no, la respuesta
    /// de error (uno ajeno responde 404, igual que si no existiera).</summary>
    private async Task<(Pedido? Pedido, IActionResult? Error)> PedidoEnBorradorAsync(long id, CancellationToken ct)
    {
        if (User.ClienteId() is not { } clienteId) return (null, Forbid());

        var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == id && p.ClienteId == clienteId, ct);
        if (pedido is null) return (null, NotFound());
        if (pedido.Estado == EstadoPedido.Cancelado)
            return (null, Conflict("Este envío ya está cancelado."));
        if (pedido.Estado != EstadoPedido.Borrador)
            return (null, Conflict("Este envío ya fue confirmado; para cambiarlo, contactá a la Empresa."));
        return (pedido, null);
    }
}
