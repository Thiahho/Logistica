using System.ComponentModel.DataAnnotations;
using Logistica.Web;
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
/// Delivery/urgencia punto a punto ad-hoc (D14 del Anexo I: "solo entrega, sin retiro
/// programado"). Es un Pedido con Tipo="delivery" — no una tabla nueva (construccion_v1.md §1:
/// techo de 17 tablas ya alcanzado) — así que hereda gratis la máquina de estados
/// (Dominio/TransicionesPedido.cs), el log inmutable de eventos y la cuenta corriente. La
/// diferencia con PedidosController.Crear: acá el operador elige el tipo de vehículo en el alta
/// (no depende de una ruta que recién se arma esa noche), así que el precio se cotiza y congela
/// en el acto — nace directo en Confirmado, nunca en Borrador. Si la zona no tiene tarifa cargada
/// (B9, "+40 km → Cotización"), el alta exige un precio manual en el mismo request en vez de
/// dejar un Borrador pendiente de una segunda pasada — acá no hay una CerrarPlanificacion futura
/// que lo complete.
/// </summary>
[ApiController]
[Route("api/deliverys")]
[Authorize(Policy = "BackOffice")]
public class DeliverysController(
    LogisticaDbContext db, PrecioService precios, DistanciaService distancias,
    CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    public record DeliveryResumen(
        long Id, string DestinatarioNombre, string Estado, decimal? Total,
        DateOnly FechaEntrega, int ClienteId, string ClienteRazonSocial,
        bool Urgente, decimal? KmCobrados, string? KmFuente);

    /// <summary>Origen y destino ya resueltos vía POST /api/ubicaciones (mismo camino que el
    /// destino de un Pedido normal) — el origen de un delivery es arbitrario, no siempre el
    /// depósito, así que no se puede reusar OrigenRutaService.PrincipalParaPedidosAsync acá.</summary>
    public record CotizarDeliveryRequest(
        long OrigenUbicacionId, long DestinoUbicacionId, int ClienteId, DateOnly FechaEntrega,
        bool Urgente, string TipoVehiculo,
        [Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")] decimal Peajes = 0m,
        [Range(0, double.MaxValue, ErrorMessage = "El km manual no puede ser negativo.")] decimal? KmManual = null,
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio manual debe ser mayor a cero.")] decimal? PrecioManual = null);

    public record CrearDeliveryRequest(
        int ClienteId,
        string? ReferenciaCliente,
        long OrigenUbicacionId,
        long DestinoUbicacionId,
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre del destinatario es obligatorio.")] string DestinatarioNombre,
        [Required(AllowEmptyStrings = false, ErrorMessage = "El teléfono del destinatario es obligatorio.")] string DestinatarioTelefono,
        [Range(1, 999, ErrorMessage = "Los bultos deben estar entre 1 y 999.")] int Bultos,
        [Range(0, 100000, ErrorMessage = "El peso debe estar entre 0 y 100000 kg.")] decimal? PesoKg,
        [Range(0, 1000000000, ErrorMessage = "El valor declarado debe estar entre 0 y 1.000.000.000.")] decimal? ValorDeclarado,
        DateOnly FechaEntrega,
        bool Urgente,
        string TipoVehiculo,
        [Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")] decimal Peajes,
        [Range(0, double.MaxValue, ErrorMessage = "El km manual no puede ser negativo.")] decimal? KmManual,
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio manual debe ser mayor a cero.")] decimal? PrecioManual,
        string? Observaciones);

    /// <summary>Mismo envoltorio de paginación que PedidosController.Listar (RF-10 y ss.),
    /// acotado a Tipo == "delivery".</summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
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
        var query = db.Pedidos.AsNoTracking().Where(p => p.Tipo == "delivery");

        if (clienteId is not null) query = query.Where(p => p.ClienteId == clienteId.Value);
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
            "id" => query.OrderBy(p => p.Id),
            "-id" => query.OrderByDescending(p => p.Id),
            "total" => query.OrderBy(p => p.Total).ThenByDescending(p => p.Id),
            "-total" => query.OrderByDescending(p => p.Total).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.FechaEntrega).ThenByDescending(p => p.Id),
        };

        tamanioPagina = Paginacion.TamanioEfectivo(tamanioPagina);
        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? Math.Min(pagina.Value, 1_000_000) : 1;
            query = query.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value);
        }

        var resultado = await query
            .Select(p => new DeliveryResumen(
                p.Id, p.DestinatarioNombre, p.Estado.ToString(), p.Total, p.FechaEntrega,
                p.ClienteId, p.Cliente.RazonSocial, p.Urgente, p.KmCobrados, p.KmFuente))
            .ToListAsync(ct);

        return Ok(new ListaPaginada<DeliveryResumen>(resultado, total));
    }

    /// <summary>Estimado en vivo — a diferencia de PedidosController.Cotizar, acá el tipo de
    /// vehículo ya está elegido (el operador lo fija en el alta), así que devuelve un solo
    /// DesglosePrecio en vez de cotizar camioneta y moto en paralelo.</summary>
    [HttpPost("cotizar")]
    public async Task<IActionResult> Cotizar(CotizarDeliveryRequest req, CancellationToken ct)
    {
        if (req.TipoVehiculo is not ("camioneta" or "moto")) return BadRequest("Tipo de vehículo inválido.");

        var geo = await ResolverGeoAsync(req.OrigenUbicacionId, req.DestinoUbicacionId, ct);
        if (geo.Error is not null) return BadRequest(geo.Error);

        var distancia = await distancias.ResolverAsync(geo.OrigenLat, geo.OrigenLng, geo.DestinoLat, geo.DestinoLng, req.KmManual, ct);

        try
        {
            var desglose = await precios.CotizarAsync(
                req.ClienteId, geo.ZonaId!.Value, req.FechaEntrega, req.Urgente, req.Peajes,
                descuentoRuta: false, req.TipoVehiculo, req.PrecioManual, distancia, ct);
            return Ok(desglose);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Alta. El body no trae total ni precio_base (regla §3.1: el precio se calcula en
    /// el servidor) salvo PrecioManual, que sustituye solo el origen de precio_base (B9) — la
    /// fórmula de §6 sigue aplicando recargo/descuento/peajes encima igual.</summary>
    [HttpPost]
    public async Task<IActionResult> Crear(CrearDeliveryRequest req, CancellationToken ct)
    {
        if (req.TipoVehiculo is not ("camioneta" or "moto")) return BadRequest("Tipo de vehículo inválido.");

        // Mismo chequeo de corte por deuda vencida que PedidosController.Crear (§10.2-L1/L3): un
        // delivery es un pedido más a los efectos de facturación, así que respeta la misma regla.
        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == req.ClienteId)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return BadRequest("El cliente no existe.");
        if (!cliente.Activo) return Conflict($"{cliente.RazonSocial} está inactivo; no admite pedidos nuevos.");

        var hoy = Reloj.HoyLocal();
        var errorFecha = ValidacionFechas.FechaEntrega(req.FechaEntrega, hoy, diasAtras: 30, diasAdelante: 365);
        if (errorFecha is not null) return BadRequest(errorFecha);
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

        var geo = await ResolverGeoAsync(req.OrigenUbicacionId, req.DestinoUbicacionId, ct);
        if (geo.Error is not null) return BadRequest(geo.Error);

        var distancia = await distancias.ResolverAsync(geo.OrigenLat, geo.OrigenLng, geo.DestinoLat, geo.DestinoLng, req.KmManual, ct);

        DesglosePrecio desglose;
        try
        {
            desglose = await precios.CotizarAsync(
                req.ClienteId, geo.ZonaId!.Value, req.FechaEntrega, req.Urgente, req.Peajes,
                descuentoRuta: false, req.TipoVehiculo, req.PrecioManual, distancia, ct);
        }
        catch (InvalidOperationException ex)
        {
            // A diferencia de un Pedido normal (que puede nacer en Borrador y esperar a
            // CerrarPlanificacion), acá no hay una segunda pasada futura: sin PrecioManual, la
            // zona sin tarifa bloquea el alta en vez de dejar un delivery a medio cotizar.
            return BadRequest($"{ex.Message} Fijá PrecioManual en el alta para completarla igual.");
        }

        var ahora = DateTimeOffset.UtcNow;
        var actorId = User.UsuarioId();
        var pedido = new Pedido
        {
            ClienteId = req.ClienteId,
            ReferenciaCliente = req.ReferenciaCliente,
            Tipo = "delivery",
            OrigenUbicacionId = req.OrigenUbicacionId,
            DestinoUbicacionId = req.DestinoUbicacionId,
            DestinatarioNombre = req.DestinatarioNombre.Trim(),
            DestinatarioTelefono = req.DestinatarioTelefono.Trim(),
            Bultos = req.Bultos,
            PesoKg = req.PesoKg,
            ValorDeclarado = req.ValorDeclarado,
            FechaEntrega = req.FechaEntrega,
            Urgente = req.Urgente,
            ZonaId = geo.ZonaId,
            PrecioBase = desglose.PrecioBase,
            RecargoKm = desglose.RecargoKm,
            KmCobrados = desglose.KmCobrados,
            KmFuente = desglose.KmFuente,
            RecargoUrgencia = desglose.RecargoUrgencia,
            DescuentoRuta = desglose.DescuentoRuta,
            Peajes = req.Peajes,
            Total = desglose.Total,
            PrecioCongeladoEn = ahora,
            PrecioManual = req.PrecioManual,
            PrecioManualPor = req.PrecioManual is not null ? actorId : null,
            PrecioManualEn = req.PrecioManual is not null ? ahora : null,
            KmManual = req.KmManual,
            KmManualPor = req.KmManual is not null ? actorId : null,
            KmManualEn = req.KmManual is not null ? ahora : null,
            Estado = EstadoPedido.Confirmado,
            Observaciones = req.Observaciones,
            CreadoEn = ahora,
        };

        db.Pedidos.Add(pedido);
        await db.GuardarComoAsync(actorId, "Alta de delivery.", ct);

        return CreatedAtAction(nameof(Listar), new { }, new DeliveryResumen(
            pedido.Id, pedido.DestinatarioNombre, pedido.Estado.ToString(), pedido.Total,
            pedido.FechaEntrega, pedido.ClienteId, cliente.RazonSocial, pedido.Urgente,
            pedido.KmCobrados, pedido.KmFuente));
    }

    private record GeoResuelta(int? ZonaId, decimal? OrigenLat, decimal? OrigenLng, decimal? DestinoLat, decimal? DestinoLng, string? Error);

    private async Task<GeoResuelta> ResolverGeoAsync(long origenUbicacionId, long destinoUbicacionId, CancellationToken ct)
    {
        var origen = await db.Ubicaciones.AsNoTracking()
            .Where(u => u.Id == origenUbicacionId)
            .Select(u => new { u.Lat, u.Lng })
            .SingleOrDefaultAsync(ct);
        if (origen is null) return new GeoResuelta(null, null, null, null, null, "La ubicación de origen no existe.");

        var destino = await db.Ubicaciones.AsNoTracking()
            .Where(u => u.Id == destinoUbicacionId)
            .Select(u => new { u.Lat, u.Lng, ZonaId = u.Localidad != null ? u.Localidad.ZonaId : null })
            .SingleOrDefaultAsync(ct);
        if (destino is null) return new GeoResuelta(null, null, null, null, null, "La ubicación de destino no existe.");
        if (destino.ZonaId is null) return new GeoResuelta(null, null, null, null, null, "La localidad de destino no tiene zona asignada; no se puede cotizar.");

        return new GeoResuelta(destino.ZonaId, origen.Lat, origen.Lng, destino.Lat, destino.Lng, null);
    }
}
