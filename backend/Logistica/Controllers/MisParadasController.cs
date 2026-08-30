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
using Npgsql;

namespace Logistica.Controllers;

/// <summary>
/// Superficie de escritura del repartidor (H2, RF-18 a RF-24). Consulta v_paradas_repartidor
/// (construccion_v1.md §3 regla 4), nunca pedidos — esa vista no tiene importes. Solo las paradas
/// de rutas asignadas al repartidor autenticado.
/// </summary>
[ApiController]
[Route("api/mis-paradas")]
[Authorize(Policy = "Repartidor")]
public class MisParadasController(
    LogisticaDbContext db,
    IOptions<OpcionesPruebaEntrega> opciones,
    AlmacenamientoFotos almacenamiento) : ControllerBase
{
    public record PedidoDeParada(long PedidoId, string DestinatarioNombre, string DestinatarioTelefono, int Bultos, string? Observaciones);

    public record ParadaDelDia(
        long ParadaId, long RutaId, int Orden, string Tipo, string Estado,
        DateTimeOffset? LlegadaEn, DateTimeOffset? SalidaEn,
        string CalleNumero, string? Localidad, string? Referencia, decimal? Lat, decimal? Lng,
        List<PedidoDeParada> Pedidos);

    public record JornadaDelDia(
        DateOnly? Fecha, long? RutaId, int Total, int Completadas, int Fallidas,
        IReadOnlyList<string> MotivosFallo, int UmbralDesvioMetros,
        List<ParadaDelDia> Paradas);

    public record RegistrarLlegadaRequest(DateTimeOffset LlegadaEn, string DeviceUuid);

    /// <summary>Clase, no record posicional: [FromForm] + IFormFile en un record posicional es
    /// frágil con el model binder de multipart/form-data.</summary>
    public class CerrarParadaRequest
    {
        public string DeviceUuid { get; set; } = null!;

        /// <summary>entregado | fallido</summary>
        public string Resultado { get; set; } = null!;

        /// <summary>RNF-03: la hora que vale es la del dispositivo, no now() del servidor.</summary>
        public DateTimeOffset CapturadaEn { get; set; }

        /// <summary>Por si el POST a /llegada nunca salió (offline).</summary>
        public DateTimeOffset? LlegadaEn { get; set; }

        public string? ReceptorNombre { get; set; }

        /// <summary>RF-23: verificación sin almacenar imagen del documento.</summary>
        public bool IdentidadVerificada { get; set; }

        public string? MotivoFallo { get; set; }
        public decimal? Lat { get; set; }
        public decimal? Lng { get; set; }
        public IFormFile? Foto { get; set; }
    }

    public record CierreResultado(long ParadaId, string EstadoParada, int PedidosActualizados, int? DesvioMetros, bool DesvioAlto, bool Duplicado);

    /// <summary>Versión plana original, una fila por (parada, pedido) — RF-14 hace que una parada
    /// consolidada aparezca duplicada. Reemplazada por Dia() de abajo; queda hasta que /hoy deje
    /// de usarla (etapa 2 de H2), para no romper el frontend actual a mitad de esta etapa.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var paradas = await (
            from p in db.Set<ParadaRepartidor>()
            join r in db.Rutas on p.RutaId equals r.Id
            where r.RepartidorId == repartidorId && r.Estado == "en_curso"
            orderby p.Orden
            select p
        ).ToListAsync(ct);

        return Ok(paradas);
    }

    /// <summary>Bundle único de RNF-07: todo lo que la PWA necesita antes de salir, en un solo
    /// request. La vista trae una fila por (parada, pedido) — RF-14 consolida varios pedidos en
    /// una parada — así que acá se agrupa por ParadaId antes de exponerla.</summary>
    [HttpGet("dia")]
    public async Task<IActionResult> Dia(CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var ruta = await db.Rutas.AsNoTracking()
            .Where(r => r.RepartidorId == repartidorId && r.Estado == "en_curso")
            .OrderByDescending(r => r.Fecha)
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return Ok(new JornadaDelDia(null, null, 0, 0, 0, opciones.Value.MotivosFallo, opciones.Value.UmbralDesvioMetros, []));

        var filas = await (
            from p in db.Set<ParadaRepartidor>()
            where p.RutaId == ruta.Id
            orderby p.Orden
            select p
        ).ToListAsync(ct);

        var paradas = filas
            .GroupBy(f => f.ParadaId)
            .Select(g =>
            {
                var primero = g.First();
                return new ParadaDelDia(
                    primero.ParadaId, primero.RutaId, primero.Orden, primero.Tipo, primero.Estado,
                    primero.LlegadaEn, primero.SalidaEn,
                    primero.CalleNumero, primero.Localidad, primero.Referencia, primero.Lat, primero.Lng,
                    g.Select(f => new PedidoDeParada(f.PedidoId, f.DestinatarioNombre, f.DestinatarioTelefono, f.Bultos, f.Observaciones)).ToList());
            })
            .OrderBy(p => p.Orden)
            .ToList();

        return Ok(new JornadaDelDia(
            ruta.Fecha, ruta.Id, paradas.Count,
            paradas.Count(p => p.Estado == "completada"), paradas.Count(p => p.Estado == "fallida"),
            opciones.Value.MotivosFallo, opciones.Value.UmbralDesvioMetros, paradas));
    }

    /// <summary>RF-24. Idempotente: si ya hay una llegada registrada, se conserva la primera —
    /// el doble tap (o un reintento offline) no la pisa.</summary>
    [HttpPost("{paradaId:long}/llegada")]
    public async Task<IActionResult> RegistrarLlegada(long paradaId, RegistrarLlegadaRequest req, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();
        var parada = await db.RutaParadas
            .Include(p => p.Ruta)
            .SingleOrDefaultAsync(p => p.Id == paradaId && p.Ruta.RepartidorId == repartidorId && p.Ruta.Estado == "en_curso", ct);
        if (parada is null) return NotFound();

        if (parada.LlegadaEn is null)
        {
            // Npgsql exige offset 0 para timestamptz; el dispositivo manda su offset local
            // (RNF-03: la hora que vale es la de captura). ToUniversalTime() no cambia el
            // instante, solo la representación — se guarda el mismo momento exacto.
            parada.LlegadaEn = req.LlegadaEn.ToUniversalTime();
            // SaveChangesAsync directo, NO GuardarComoAsync: esto no toca `pedidos`, así que el
            // GUC app.usuario_id no tiene consumidor (fn_log_estado_pedido solo mira pedidos).
            // Mismo criterio que RutasController.Cerrar, que escribe la economía de la ruta con
            // SaveChangesAsync plano — a primera vista parece violar la regla 7, no la viola.
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { parada.LlegadaEn });
    }

    /// <summary>
    /// Cierre de parada (RF-19/20/21/23/24/29): un solo POST multipart atómico. Desvío consciente
    /// de construccion_v1.md §7, que separaba subir-la-foto de insertar-la-fila porque asumía
    /// Supabase Storage con escritura directa del cliente al bucket — con backend .NET propio, un
    /// solo request server-side es más simple y conserva todos los invariantes que el diagrama
    /// protegía (idempotencia, hora de captura, desvío).
    /// </summary>
    [HttpPost("{paradaId:long}/cierre")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Cerrar(long paradaId, [FromForm] CerrarParadaRequest req, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var parada = await db.RutaParadas
            .Include(p => p.Ruta)
            .Include(p => p.Ubicacion)
            .SingleOrDefaultAsync(p => p.Id == paradaId && p.Ruta.RepartidorId == repartidorId && p.Ruta.Estado == "en_curso", ct);
        if (parada is null) return NotFound();

        var pedidoIds = await db.ParadaPedidos.Where(pp => pp.ParadaId == paradaId).Select(pp => pp.PedidoId).ToListAsync(ct);
        if (pedidoIds.Count == 0) return NotFound();

        // Chequeo de idempotencia EXPLÍCITO primero (RNF-02): si ya existe una prueba para todos
        // los pedidos de la parada con este device_uuid, no es un conflicto — es un reintento. Un
        // 409 genérico no le dice al cliente si "ya estaba" (borrar la captura local) o "conflicto
        // real" (dejarla bloqueada), por eso no se delega esta parte en el unique index.
        var existentes = await db.PruebasEntrega.AsNoTracking()
            .Where(pe => pe.DeviceUuid == req.DeviceUuid && pedidoIds.Contains(pe.PedidoId))
            .ToListAsync(ct);
        if (existentes.Count == pedidoIds.Count)
        {
            var previo = existentes[0];
            return Ok(new CierreResultado(paradaId, parada.Estado, existentes.Count,
                previo.DesvioMetros, previo.DesvioMetros > opciones.Value.UmbralDesvioMetros, Duplicado: true));
        }

        if (req.Resultado is not ("entregado" or "fallido"))
            return BadRequest("Resultado inválido.");

        var nuevoEstadoPedido = req.Resultado == "entregado" ? EstadoPedido.Entregado : EstadoPedido.Fallido;

        // Se consulta TransicionesPedido.MotivoObligatorio en vez de hardcodear "resultado ==
        // fallido": es la misma regla que ya rige /api/pedidos/{id}/estado, no se duplica.
        if (TransicionesPedido.MotivoObligatorio(nuevoEstadoPedido)
            && (string.IsNullOrWhiteSpace(req.MotivoFallo) || !opciones.Value.MotivosFallo.Contains(req.MotivoFallo)))
            return BadRequest("El motivo de entrega fallida debe ser uno de la lista.");

        if (req.Resultado == "entregado" && (req.Foto is null || string.IsNullOrWhiteSpace(req.ReceptorNombre)))
            return BadRequest("La entrega exige foto y nombre del receptor.");

        var pedidos = await db.Pedidos.Where(p => pedidoIds.Contains(p.Id)).ToListAsync(ct);
        if (pedidos.Any(p => !TransicionesPedido.Permitida(p.Estado, nuevoEstadoPedido)))
            return Conflict("Alguno de los pedidos de la parada ya cambió de estado; volvé a cargar la jornada.");

        // La foto se escribe a disco ANTES de la transacción: si después falla la base, queda un
        // archivo huérfano inofensivo que el reintento pisa. Al revés (base primero) quedaría una
        // fila apuntando a un archivo inexistente, irreparable sin intervención manual.
        string? fotoPath = null;
        if (req.Foto is not null)
        {
            await using var stream = req.Foto.OpenReadStream();
            fotoPath = await almacenamiento.GuardarAsync(paradaId, req.DeviceUuid, stream, req.Foto.Length, ct);
        }

        int? desvioMetros = null;
        if (req.Lat is not null && req.Lng is not null && parada.Ubicacion.Lat is not null && parada.Ubicacion.Lng is not null)
            desvioMetros = Geo.DistanciaMetros(req.Lat.Value, req.Lng.Value, parada.Ubicacion.Lat.Value, parada.Ubicacion.Lng.Value);
        var desvioAlto = desvioMetros > opciones.Value.UmbralDesvioMetros;

        // Npgsql exige offset 0 para timestamptz; el dispositivo manda su offset local (RNF-03:
        // la hora que vale es la de captura). ToUniversalTime() no cambia el instante.
        var capturadaEnUtc = req.CapturadaEn.ToUniversalTime();
        var llegadaEnUtc = req.LlegadaEn?.ToUniversalTime();

        foreach (var pedido in pedidos)
        {
            db.PruebasEntrega.Add(new PruebaEntrega
            {
                PedidoId = pedido.Id,
                ParadaId = paradaId,
                Resultado = req.Resultado,
                MotivoFallo = req.Resultado == "fallido" ? req.MotivoFallo : null,
                ReceptorNombre = req.ReceptorNombre,
                IdentidadVerificada = req.IdentidadVerificada,
                FotoPath = fotoPath,
                Lat = req.Lat,
                Lng = req.Lng,
                DesvioMetros = desvioMetros,
                CapturadaEn = capturadaEnUtc,
                DeviceUuid = req.DeviceUuid,
            });
            pedido.Estado = nuevoEstadoPedido;
        }

        parada.Estado = req.Resultado == "entregado" ? "completada" : "fallida";
        parada.LlegadaEn ??= llegadaEnUtc;
        parada.SalidaEn = capturadaEnUtc;

        try
        {
            // GuardarComoAsync obligatorio acá (regla 7): cambia Pedido.Estado y
            // fn_log_estado_pedido tiene que registrar al repartidor como actor.
            var motivo = req.Resultado == "fallido" ? req.MotivoFallo : "Entrega confirmada por el repartidor.";
            await db.GuardarComoAsync(repartidorId, motivo, ct);
        }
        catch (DbUpdateException ex) when (EsViolacionDeUnique(ex))
        {
            // Backstop de carrera: dos taps casi simultáneos pasaron el chequeo de idempotencia a
            // la vez. Se responde igual que el camino feliz de "ya existía" — nunca se deja
            // escapar como 409, que ManejadorExcepciones no sabría distinguir de un conflicto real.
            var yaExistentes = await db.PruebasEntrega.AsNoTracking()
                .Where(pe => pe.DeviceUuid == req.DeviceUuid && pedidoIds.Contains(pe.PedidoId))
                .ToListAsync(ct);
            return Ok(new CierreResultado(paradaId, parada.Estado, yaExistentes.Count,
                yaExistentes.FirstOrDefault()?.DesvioMetros, desvioAlto, Duplicado: true));
        }

        return Ok(new CierreResultado(paradaId, parada.Estado, pedidos.Count, desvioMetros, desvioAlto, Duplicado: false));
    }

    private static bool EsViolacionDeUnique(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
