using System.ComponentModel.DataAnnotations;
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
    UbicacionService ubicaciones, GeocodificacionService geocodificador)
    : ControllerBase
{
    public record FacturaPropia(
        long Id, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
        DateOnly FechaVencimiento, decimal Total, decimal Saldo, string Estado);

    public record CuentaPropia(
        decimal Saldo, decimal DeudaVencida, bool ServicioCortado,
        DateOnly? ProximoVencimiento, List<FacturaPropia> Facturas);

    public record CrearPedidoPortalRequest(
        string DestinatarioNombre,
        string DestinatarioTelefono,
        long DestinoUbicacionId,
        [Range(1, int.MaxValue, ErrorMessage = "Los bultos deben ser al menos 1.")] int Bultos,
        [Range(0, double.MaxValue, ErrorMessage = "El peso no puede ser negativo.")] decimal? PesoKg,
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
    public record ResolverUbicacionRequest(string CalleNumero, int LocalidadId, string? Referencia);
    public record UbicacionResuelta(long Id, decimal? Lat, decimal? Lng, string? GeoConfianza);

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

    private static string EstadoDe(decimal saldo, decimal total, DateOnly vencimiento, DateOnly hoy)
    {
        if (saldo <= 0) return "pagada";
        if (vencimiento < hoy) return "vencida";
        return saldo < total ? "parcial" : "pendiente";
    }

    [HttpGet]
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

        return Ok(new CuentaPropia(saldo, deudaVencida, servicioCortado, proximoVencimiento, facturas));
    }

    /// <summary>Espejo de LocalidadesController.Buscar (ver comentario arriba de estos records) —
    /// necesario para que SelectorLocalidad funcione dentro de /mis-envios/nuevo.</summary>
    [HttpGet("localidades/buscar")]
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

    /// <summary>Espejo de LocalidadesController.Crear. Nace sin zona, igual que el alta interna:
    /// un cliente eligiendo su localidad no puede decidir sola la zona de precio.</summary>
    [HttpPost("localidades")]
    public async Task<IActionResult> CrearLocalidad(CrearLocalidadRequest req, CancellationToken ct)
    {
        var nombre = req.Nombre.Trim();
        if (nombre.Length == 0) return BadRequest("El nombre de la localidad no puede estar vacío.");
        var partido = string.IsNullOrWhiteSpace(req.Partido) ? null : req.Partido.Trim();

        var existente = await db.Localidades.FirstOrDefaultAsync(l =>
            l.Nombre.ToLower() == nombre.ToLower() &&
            (partido == null ? l.Partido == null : l.Partido!.ToLower() == partido.ToLower()), ct);
        if (existente is not null)
            return Ok(new LocalidadResumen(existente.Id, existente.Nombre, existente.Partido, existente.ZonaId));

        var nueva = new Localidad { Nombre = nombre, Partido = partido, ZonaId = null };
        db.Localidades.Add(nueva);
        await db.SaveChangesAsync(ct);
        return Ok(new LocalidadResumen(nueva.Id, nueva.Nombre, nueva.Partido, nueva.ZonaId));
    }

    /// <summary>Espejo de UbicacionesController.Resolver — geocodifica al salir del campo
    /// dirección, mismo resolver-o-crear que el alta interna.</summary>
    [HttpPost("ubicaciones")]
    public async Task<IActionResult> ResolverUbicacion(ResolverUbicacionRequest req, CancellationToken ct)
    {
        var ubicacion = await ubicaciones.ResolverOCrearAsync(req.CalleNumero, req.LocalidadId, req.Referencia, ct);
        return Ok(new UbicacionResuelta(ubicacion.Id, ubicacion.Lat, ubicacion.Lng, ubicacion.GeoConfianza));
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

        if (req.TipoVehiculo is not ("camioneta" or "moto"))
            return BadRequest("Tipo de vehículo inválido.");

        // §6: corte de las 16:00 (provisional, configurable — Opciones/OpcionesPortal.cs). Es el
        // cierre del LOTE DE HOY (16:00-18:00 es la ventana de recepción, B13 §1), no un bloqueo
        // general del portal — un pedido para mañana entra a cualquier hora. Se valida la hora
        // del servidor al confirmar, no solo al mostrar el formulario, para que dejar la pantalla
        // abierta no permita colarse en el lote de hoy después del corte.
        var hoy = Reloj.HoyLocal();
        var horaCorte = opcionesPortal.Value.HoraCorte;
        if (req.FechaEntrega <= hoy && Reloj.HoraLocal() > horaCorte)
        {
            return Conflict(new
            {
                mensaje = $"La carga de hoy cerró a las {horaCorte:HH\\:mm}; esto se carga para mañana.",
                fechaEntregaSugerida = hoy.AddDays(1),
            });
        }

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId.Value)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null || !cliente.Activo) return Conflict("Tu cuenta está inactiva; no admite pedidos nuevos.");

        var suspendido = cliente.CorteSuspendidoHasta is { } h && h >= hoy;
        if (!suspendido)
        {
            var deuda = await cuentaCorriente.DeudaVencidaAsync(clienteId.Value, hoy, ct);
            if (deuda > 0)
                return Conflict(
                    $"Servicio cortado por deuda vencida: adeudás ${deuda:N2} de comprobantes vencidos. " +
                    "Los pedidos ya confirmados o en ruta no se ven afectados. Se rehabilita solo al pagar " +
                    "el total o con un plan de cuotas.");
        }

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
        };

        // B9 (Anexo I §4): si la zona no tiene tarifa cargada para ningún tipo de vehículo, no hay
        // nada que cotizar ni fijar como vinculante — el pedido se guarda igual, sin precio, para
        // que Administración lo fije a mano (diseño_b5_portal_carga.md §5).
        decimal? precioVinculante = null;
        try
        {
            var distancia = await distancias.ResolverAsync(
                origen.Lat, origen.Lng, destino.Lat, destino.Lng, kmManual: null, ct);
            var desglose = await precios.CotizarAsync(
                clienteId.Value, zonaId.Value, req.FechaEntrega, req.Urgente,
                peajes: 0m, descuentoRuta: false, req.TipoVehiculo, precioManual: null, distancia, ct);
            precioVinculante = desglose.Total;
        }
        catch (InvalidOperationException)
        {
            // Zona sin tarifa (B9) — RequiereCotizacion queda true, ver más abajo.
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

        return CreatedAtAction("Detalle", "Pedidos", new { id = pedido.Id }, new PedidoPortalCreado(
            pedido.Id, pedido.FechaEntrega, precioVinculante, RequiereCotizacion: precioVinculante is null));
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
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
