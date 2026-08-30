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
public class PedidosController(LogisticaDbContext db, PrecioService precios, IOptions<OpcionesPruebaEntrega> opcionesPruebaEntrega) : ControllerBase
{
    public record PedidoResumen(
        long Id, string DestinatarioNombre, string Estado, decimal Total,
        DateOnly FechaEntrega, int ClienteId, bool DireccionDudosa);

    public record CotizarRequest(int ClienteId, int LocalidadId, DateOnly FechaEntrega, bool Urgente, decimal Peajes = 0m);

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
        decimal PrecioBase, decimal RecargoUrgencia, decimal DescuentoRuta, decimal Peajes, decimal Total,
        DateTimeOffset PrecioCongeladoEn,
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

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? fecha, [FromQuery] string? estado, CancellationToken ct)
    {
        var query = db.Pedidos.AsNoTracking().AsQueryable();

        var clienteId = User.ClienteId();
        if (clienteId is not null)
            query = query.Where(p => p.ClienteId == clienteId);

        if (fecha is not null)
            query = query.Where(p => p.FechaEntrega == fecha.Value);

        if (estado is not null)
        {
            if (!Enum.TryParse<EstadoPedido>(estado, ignoreCase: true, out var estadoParsed))
                return BadRequest("Estado inválido.");
            query = query.Where(p => p.Estado == estadoParsed);
        }

        // DireccionDudosa reusa ubicacion_apta (registrada como DbFunction en LogisticaDbContext)
        // en vez de reimplementar el criterio en C# — construccion_v1.md §3 regla 3.
        var filas = await query
            .OrderByDescending(p => p.FechaEntrega)
            .Select(p => new
            {
                p.Id,
                p.DestinatarioNombre,
                p.Estado,
                p.Total,
                p.FechaEntrega,
                p.ClienteId,
                DireccionDudosa = !LogisticaDbContext.UbicacionApta(p.DestinoUbicacionId),
            })
            .ToListAsync(ct);

        var resultado = filas.Select(p => new PedidoResumen(
            p.Id, p.DestinatarioNombre, p.Estado.ToString(), p.Total, p.FechaEntrega, p.ClienteId, p.DireccionDudosa));

        return Ok(resultado);
    }

    /// <summary>
    /// Pedidos confirmados de una fecha, disponibles para armar una ruta (RF-10, agrupables por
    /// ZonaCodigo en el cliente; RF-14, se consolidan por DestinoUbicacionId en el cliente).
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
            .Where(p => p.FechaEntrega == fecha && p.Estado == EstadoPedido.Confirmado)
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

    /// <summary>Precio en vivo mientras se completa el alta (construccion_v1.md §4.1). No persiste nada.</summary>
    [HttpPost("cotizar")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Cotizar(CotizarRequest req, CancellationToken ct)
    {
        var zonaId = await db.Localidades.Where(l => l.Id == req.LocalidadId).Select(l => l.ZonaId).SingleOrDefaultAsync(ct);
        if (zonaId is null)
            return BadRequest("La localidad no tiene zona asignada; no se puede cotizar.");

        try
        {
            var desglose = await precios.CotizarAsync(
                req.ClienteId, zonaId.Value, req.FechaEntrega, req.Urgente, req.Peajes, descuentoRuta: false, ct);
            return Ok(desglose);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Alta de pedido. El body no trae total ni precio_base (regla §3.1: el precio se calcula en
    /// el servidor). Se crea directamente 'confirmado': el precio ya está calculado y congelado,
    /// que es la condición de esa transición (construccion_v1.md §5). Una dirección sin
    /// geolocalizar no bloquea el alta —queda marcada y trg_bloquear_direccion_dudosa la frena
    /// recién al rutear.
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

        var origen = await db.Ubicaciones.SingleAsync(u => u.Referencia == "deposito", ct);

        DesglosePrecio desglose;
        try
        {
            desglose = await precios.CotizarAsync(
                req.ClienteId, zonaId.Value, req.FechaEntrega, req.Urgente, req.Peajes, descuentoRuta: false, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var pedido = new Pedido
        {
            ClienteId = req.ClienteId,
            ReferenciaCliente = req.ReferenciaCliente,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destino.Id,
            DestinatarioNombre = req.DestinatarioNombre,
            DestinatarioTelefono = req.DestinatarioTelefono,
            Bultos = req.Bultos,
            PesoKg = req.PesoKg,
            ValorDeclarado = req.ValorDeclarado,
            FechaEntrega = req.FechaEntrega,
            Urgente = req.Urgente,
            ZonaId = zonaId,
            PrecioBase = desglose.PrecioBase,
            RecargoUrgencia = desglose.RecargoUrgencia,
            DescuentoRuta = desglose.DescuentoRuta,
            Peajes = desglose.Peajes,
            Total = desglose.Total,
            PrecioCongeladoEn = DateTimeOffset.UtcNow,
            Estado = EstadoPedido.Confirmado,
            Observaciones = req.Observaciones,
            CreadoEn = DateTimeOffset.UtcNow,
        };

        db.Pedidos.Add(pedido);
        await db.GuardarComoAsync(User.UsuarioId(), ct: ct);

        return CreatedAtAction(nameof(Listar), new { }, new PedidoResumen(
            pedido.Id, pedido.DestinatarioNombre, pedido.Estado.ToString(), pedido.Total,
            pedido.FechaEntrega, pedido.ClienteId, DireccionDudosa: destino.GeoConfianza is not ("alta" or "media") && !destino.Verificada));
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
            // original — P1 lo impediría igual una vez confirmado.
            var zonaRetorno = await db.Ubicaciones.Where(u => u.Id == pedido.OrigenUbicacionId)
                .Select(u => u.Localidad!.ZonaId).SingleOrDefaultAsync(ct);
            if (zonaRetorno is null)
                return BadRequest("No se puede cotizar el retorno: el depósito no tiene zona asignada.");

            DesglosePrecio desglose;
            try
            {
                desglose = await precios.CotizarAsync(
                    pedido.ClienteId, zonaRetorno.Value, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                    urgente: false, peajes: 0, descuentoRuta: false, ct);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            db.Pedidos.Add(new Pedido
            {
                ClienteId = pedido.ClienteId,
                ReferenciaCliente = pedido.ReferenciaCliente,
                Tipo = "retorno",
                PedidoOrigenId = pedido.Id,
                OrigenUbicacionId = pedido.DestinoUbicacionId,
                DestinoUbicacionId = pedido.OrigenUbicacionId,
                DestinatarioNombre = pedido.DestinatarioNombre,
                DestinatarioTelefono = pedido.DestinatarioTelefono,
                Bultos = pedido.Bultos,
                FechaEntrega = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                ZonaId = zonaRetorno,
                PrecioBase = desglose.PrecioBase,
                RecargoUrgencia = desglose.RecargoUrgencia,
                DescuentoRuta = desglose.DescuentoRuta,
                Peajes = desglose.Peajes,
                Total = desglose.Total,
                PrecioCongeladoEn = DateTimeOffset.UtcNow,
                Estado = EstadoPedido.Confirmado,
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
