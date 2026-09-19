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
/// El lado de back-office de las novedades (RF-36/RF-37, acta changelog 4.8): ver qué informó el
/// repartidor y responderlo. Lo que el repartidor informa NO cambia nada por sí solo — un cambio
/// propuesto recién se aplica al pedido cuando operación lo acepta acá, con su nombre como actor.
/// Todas las acciones comparten una sola
/// política, así que se declara una vez a nivel de clase (regla 8, mismo criterio que JornadaController).
/// </summary>
[ApiController]
[Authorize(Policy = "BackOffice")]
public class NovedadesController(LogisticaDbContext db, AlmacenamientoFotos almacenamiento) : ControllerBase
{
    public record NovedadResumen(
        long Id, long RutaId, DateOnly RutaFecha, string? RepartidorNombre,
        long? ParadaId, int? ParadaOrden, long? PedidoId, string? PedidoDestinatario,
        string Tipo, string Origen, string? Categoria, string Descripcion,
        string? PropuestaCampo, string? PropuestaValorAnterior, string? PropuestaValorNuevo,
        bool TieneFoto, string Estado, string CreadaPorNombre, DateTimeOffset CreadaEn,
        string? ResueltaPorNombre, DateTimeOffset? ResueltaEn, string? Resolucion, DateTimeOffset? VistoEn);

    public record ResolverRequest(bool Aceptar, string? Resolucion);

    /// <summary>Panel de novedades. Por defecto solo las abiertas — es lo que hay que atender;
    /// `estado=todas` trae también las resueltas y rechazadas (el historial de un pedido, por ejemplo).</summary>
    [HttpGet("api/novedades")]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado, [FromQuery] long? rutaId, [FromQuery] long? pedidoId,
        [FromQuery] int? pagina, [FromQuery] int? tamanioPagina, CancellationToken ct)
    {
        var query = db.Novedades.AsNoTracking().AsQueryable();
        // "Abierta" solo significa "esperando respuesta" para lo que informó el repartidor. Un aviso de
        // operación (cambio, cancelación) nace abierto pero no espera respuesta de nadie: si entrara acá
        // inflaría el contador del panel para siempre.
        if (estado != "todas")
            query = query.Where(n => n.Estado == (estado ?? "abierta") && n.Origen == "repartidor");
        if (rutaId is not null) query = query.Where(n => n.RutaId == rutaId);
        if (pedidoId is not null) query = query.Where(n => n.PedidoId == pedidoId);

        var total = await query.CountAsync(ct);

        query = query.OrderByDescending(n => n.CreadaEn);
        if (tamanioPagina is > 0)
            query = query.Skip(((pagina is > 0 ? pagina.Value : 1) - 1) * tamanioPagina.Value).Take(tamanioPagina.Value);

        return Ok(new ListaPaginada<NovedadResumen>(await Proyectar(query).ToListAsync(ct), total));
    }

    /// <summary>Todas las novedades de una ruta, en cualquier estado, la más vieja primero — es la
    /// historia de lo que pasó ese día.</summary>
    [HttpGet("api/rutas/{rutaId:long}/novedades")]
    public async Task<IActionResult> DeLaRuta(long rutaId, CancellationToken ct)
    {
        if (!await db.Rutas.AnyAsync(r => r.Id == rutaId, ct)) return NotFound();

        var filas = await Proyectar(db.Novedades.AsNoTracking().Where(n => n.RutaId == rutaId).OrderBy(n => n.CreadaEn))
            .ToListAsync(ct);
        return Ok(filas);
    }

    /// <summary>La foto que adjuntó el repartidor. Mismo criterio que la foto de la prueba de entrega:
    /// nunca por wwwroot, el token va en el header — el frontend baja el blob con fetchConSesion.</summary>
    [HttpGet("api/novedades/{id:long}/foto")]
    public async Task<IActionResult> Foto(long id, CancellationToken ct)
    {
        var fotoPath = await db.Novedades.AsNoTracking()
            .Where(n => n.Id == id).Select(n => n.FotoPath).SingleOrDefaultAsync(ct);
        if (fotoPath is null) return NotFound();

        var stream = await almacenamiento.AbrirAsync(fotoPath, ct);
        if (stream is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        return File(stream, "image/jpeg");
    }

    /// <summary>
    /// Responde una novedad del repartidor. Incidencia de ruta y problema de carga: resolver es dejar
    /// escrito qué se hizo (la herramienta —reasignar, interrumpir, abrir un ajuste de bultos— es otra
    /// acción). Cambio propuesto: aceptar APLICA el cambio al pedido en la misma transacción, con el
    /// que resuelve como actor; rechazar exige motivo. Una novedad resuelta no vuelve a abrirse
    /// (trg_novedades_inmutable).
    /// </summary>
    [HttpPut("api/novedades/{id:long}/resolver")]
    public async Task<IActionResult> Resolver(long id, ResolverRequest req, CancellationToken ct)
    {
        var novedad = await db.Novedades.SingleOrDefaultAsync(n => n.Id == id, ct);
        if (novedad is null) return NotFound();

        if (novedad.Origen != "repartidor")
            return BadRequest("Solo se responden las novedades que informó el repartidor.");
        if (novedad.Estado != "abierta")
            return Conflict("Esta novedad ya fue resuelta.");

        var resolucion = req.Resolucion?.Trim();
        var actor = User.UsuarioId();
        var ahora = DateTimeOffset.UtcNow;

        if (novedad.Tipo == "cambio_propuesto")
        {
            if (!req.Aceptar && string.IsNullOrWhiteSpace(resolucion))
                return BadRequest("Explicale al repartidor por qué se rechaza el cambio.");

            if (req.Aceptar)
            {
                var pedido = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == novedad.PedidoId, ct);
                if (pedido is null) return NotFound();

                if (pedido.Estado is not (EstadoPedido.Confirmado or EstadoPedido.EnRuta))
                    return Conflict($"El pedido ya está {pedido.Estado}: no se le aplican correcciones.");

                var actual = novedad.PropuestaCampo switch
                {
                    "destinatario_telefono" => pedido.DestinatarioTelefono,
                    "destinatario_nombre" => pedido.DestinatarioNombre,
                    "observaciones" => pedido.Observaciones,
                    _ => throw new InvalidOperationException($"Campo no editable en la novedad {novedad.Id}: {novedad.PropuestaCampo}."),
                };
                // Si alguien cambió el dato mientras la propuesta estaba abierta, aplicarla en silencio
                // pisaría ese cambio con un valor que se calculó sobre uno viejo.
                if (actual != novedad.PropuestaValorAnterior)
                    return Conflict("El dato cambió desde que el repartidor lo propuso. Rechazá la propuesta o corregilo a mano.");

                switch (novedad.PropuestaCampo)
                {
                    case "destinatario_telefono": pedido.DestinatarioTelefono = novedad.PropuestaValorNuevo!; break;
                    case "destinatario_nombre": pedido.DestinatarioNombre = novedad.PropuestaValorNuevo!; break;
                    default: pedido.Observaciones = novedad.PropuestaValorNuevo!; break;
                }
            }
            novedad.Estado = req.Aceptar ? "resuelta" : "rechazada";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(resolucion))
                return BadRequest("Dejá escrito qué se hizo con esta novedad.");
            novedad.Estado = "resuelta";
        }

        novedad.ResueltaPor = actor;
        novedad.ResueltaEn = ahora;
        novedad.Resolucion = resolucion;
        // Vuelve a "sin ver": la respuesta le tiene que llegar al repartidor.
        novedad.VistoEn = null;

        // GuardarComoAsync (regla 7) porque un cambio aceptado toca pedidos; para el resto es inocuo.
        await db.GuardarComoAsync(actor, novedad.Tipo == "cambio_propuesto" && req.Aceptar
            ? $"Corrección de {novedad.PropuestaCampo} propuesta por el repartidor (novedad #{novedad.Id})."
            : null, ct);
        return NoContent();
    }

    private static IQueryable<NovedadResumen> Proyectar(IQueryable<Novedad> query) =>
        query.Select(n => new NovedadResumen(
            n.Id, n.RutaId, n.Ruta.Fecha, n.Ruta.Repartidor != null ? n.Ruta.Repartidor.Nombre : null,
            n.ParadaId, n.Parada != null ? n.Parada.Orden : (int?)null,
            n.PedidoId, n.Pedido != null ? n.Pedido.DestinatarioNombre : null,
            n.Tipo, n.Origen, n.Categoria, n.Descripcion,
            n.PropuestaCampo, n.PropuestaValorAnterior, n.PropuestaValorNuevo,
            n.FotoPath != null, n.Estado, n.CreadaPorUsuario.Nombre, n.CreadaEn,
            n.ResueltaPorUsuario != null ? n.ResueltaPorUsuario.Nombre : null, n.ResueltaEn, n.Resolucion, n.VistoEn));
}
