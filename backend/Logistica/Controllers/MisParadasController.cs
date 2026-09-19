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
    AlmacenamientoFotos almacenamiento,
    JornadaService jornada,
    CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    /// <summary>Origen viaja acá (y no por GET /api/ubicaciones/deposito, que es BackOffice) para
    /// no ampliar la audiencia de un endpoint de back-office: RNF-07 pide que la PWA reciba todo lo
    /// que necesita en un solo request, y el mapa del repartidor necesita el origen del recorrido.
    /// No siempre es el depósito (acta changelog 3.6): puede ser donde quedó la camioneta el día
    /// anterior — de ahí que sea un OrigenRuta completo (con dirección) y no solo lat/lng, para
    /// que el repartidor sepa desde dónde arranca. Por la misma razón, Recorrido viaja YA TRAZADO
    /// acá adentro en vez de exigir un POST propio a /api/recorrido: un repartidor sin señal en la
    /// calle no puede depender de una llamada en vivo — la ruta se descarga completa antes de
    /// salir (RNF-07). Recorrido es null si no se pudo trazar (OSRM caído): el mapa cae a línea
    /// recta entre los puntos, nunca rompe.</summary>
    public record JornadaDelDia(
        DateOnly? Fecha, long? RutaId, int Total, int Completadas, int Fallidas, int Canceladas,
        IReadOnlyList<string> MotivosFallo, int UmbralDesvioMetros, OrigenRuta? Origen,
        Recorrido? Recorrido, List<ParadaDelDia> Paradas,
        // Acta changelog 4.7: viajan acá y no en un GET propio de /api/mi-jornada porque RNF-07
        // pide un solo request antes de salir — tres campos no justifican un segundo round-trip.
        // BultosEsperados se recalcula en vivo mientras el retiro no está confirmado (es lo que
        // la pantalla de retiro muestra para contar) y sale de la fila una vez firmado (es contra
        // qué se contó, congelado).
        DateTimeOffset? RetiroConfirmadoEn, int BultosEsperados, DateTimeOffset? CierreRepartidorEn,
        // Acta changelog 4.8: novedades abiertas o sin ver de la ruta (pocas filas) y las listas
        // cerradas de categorías, por la misma razón — un solo request antes de salir (RNF-07).
        List<NovedadDelDia> Novedades, IReadOnlyList<string> CategoriasIncidencia, IReadOnlyList<string> CategoriasCarga);

    /// <summary>SinVer: true si el repartidor todavía no acusó recibo. Una respuesta de operación a lo que
    /// él informó vuelve a SinVer al resolverse, para que le llegue.</summary>
    public record NovedadDelDia(
        long Id, string Tipo, string Origen, string? Categoria, string Descripcion,
        string? PropuestaCampo, string? PropuestaValorNuevo, long? ParadaId, long? PedidoId,
        string Estado, string? Resolucion, DateTimeOffset CreadaEn, DateTimeOffset? ResueltaEn, bool SinVer);

    private const string MensajeRetiroPendiente =
        "La ruta no tiene el retiro confirmado (acta §7): ninguna ruta sale sin conteo firmado.";

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

        /// <summary>RF-23 en su redacción anterior a acta changelog 4.7 (solo el booleano, sin imagen).
        /// La imagen del documento y su retención son fase 2 de la tanda de 4.7, todavía sin construir.</summary>
        public bool IdentidadVerificada { get; set; }

        public string? MotivoFallo { get; set; }
        public decimal? Lat { get; set; }
        public decimal? Lng { get; set; }
        public IFormFile? Foto { get; set; }
    }

    /// <summary>SiguienteParadaId lo resuelve el servidor (no el bundle que el cliente tiene en memoria):
    /// el back-office puede reordenar las pendientes en vivo justo mientras el repartidor llena el
    /// formulario. Va también en los caminos de duplicado: un reintento tras recuperar señal tiene que
    /// llevar al mismo lugar que el intento original.</summary>
    public record CierreResultado(long ParadaId, string EstadoParada, int PedidosActualizados, int? DesvioMetros, bool DesvioAlto, bool Duplicado, long? SiguienteParadaId);

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
            return Ok(new JornadaDelDia(null, null, 0, 0, 0, 0, opciones.Value.MotivosFallo, opciones.Value.UmbralDesvioMetros, null, null, [], null, 0, null,
                [], opciones.Value.CategoriasIncidencia, opciones.Value.CategoriasCarga));

        // JornadaService.ArmarAsync es el mismo bundle que arma /api/rutas/{id}/jornada para el
        // back-office (JornadaController) — acá se le suma la config propia del repartidor
        // (MotivosFallo, UmbralDesvioMetros) que no tiene sentido exponer fuera de la PWA.
        var bundle = await jornada.ArmarAsync(ruta.Id, ct)
            ?? throw new InvalidOperationException($"La ruta {ruta.Id} desapareció entre la consulta y el armado del bundle.");

        // CerrarPlanificacion (RF-17, acta changelog 3.8) exige el origen elegido antes de pasar
        // a en_curso: una ruta que llegó hasta acá siempre tiene uno resuelto. El throw es
        // defensivo, no un camino esperado.
        if (bundle.Origen is null)
            throw new InvalidOperationException($"La ruta {ruta.Id} está en curso sin origen resuelto.");

        // Una query más, solo mientras el retiro no está firmado: es el número que la pantalla de
        // retiro pone arriba para contra qué contar. Una vez firmado sale de la fila, porque lo
        // que importa desde ahí es contra qué se contó, no cuántos bultos tiene la ruta ahora.
        var bultosEsperados = ruta.RetiroBultosEsperados
            ?? await db.ParadaPedidos
                .Where(pp => pp.Parada.RutaId == ruta.Id)
                .SumAsync(pp => (int?)pp.Pedido.Bultos, ct) ?? 0;

        var novedades = await db.Novedades.AsNoTracking()
            // Sin acusar, o lo que él informó y sigue abierto. Un aviso de operación ya acusado sale del
            // bundle aunque su estado siga "abierta" (nadie lo resuelve: no espera respuesta).
            .Where(n => n.RutaId == ruta.Id && (n.VistoEn == null || (n.Origen == "repartidor" && n.Estado == "abierta")))
            .OrderBy(n => n.CreadaEn)
            .Select(n => new NovedadDelDia(
                n.Id, n.Tipo, n.Origen, n.Categoria, n.Descripcion, n.PropuestaCampo, n.PropuestaValorNuevo,
                n.ParadaId, n.PedidoId, n.Estado, n.Resolucion, n.CreadaEn, n.ResueltaEn, n.VistoEn == null))
            .ToListAsync(ct);

        return Ok(new JornadaDelDia(
            ruta.Fecha, ruta.Id, bundle.Total, bundle.Completadas, bundle.Fallidas, bundle.Canceladas,
            opciones.Value.MotivosFallo, opciones.Value.UmbralDesvioMetros,
            bundle.Origen, bundle.Recorrido, bundle.Paradas,
            ruta.RetiroConfirmadoEn, bultosEsperados, ruta.CierreRepartidorEn,
            novedades, opciones.Value.CategoriasIncidencia, opciones.Value.CategoriasCarga));
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

        if (parada.Ruta.RetiroConfirmadoEn is null) return Conflict(MensajeRetiroPendiente);

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

        var idsDeLaParada = await db.ParadaPedidos.Where(pp => pp.ParadaId == paradaId).Select(pp => pp.PedidoId).ToListAsync(ct);
        if (idsDeLaParada.Count == 0) return NotFound();

        // Un pedido que operación canceló con la ruta en curso (acta changelog 4.8) no se entrega ni se
        // falla: se excluye del cierre en vez de dejar que dispare el Conflict de más abajo y bloquee
        // a los demás pedidos de la parada consolidada. Se decide acá, antes del chequeo de
        // idempotencia, para que un reintento compare contra el mismo conjunto que el intento original.
        var todosLosPedidos = await db.Pedidos.Where(p => idsDeLaParada.Contains(p.Id)).ToListAsync(ct);
        var pedidoIds = todosLosPedidos.Where(p => p.Estado != EstadoPedido.Cancelado).Select(p => p.Id).ToList();
        if (pedidoIds.Count == 0)
            return Conflict("Operación canceló todos los pedidos de esta parada: no hay nada para entregar.");

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
                previo.DesvioMetros, previo.DesvioMetros > opciones.Value.UmbralDesvioMetros, Duplicado: true,
                await SiguienteParadaAsync(parada.RutaId, ct)));
        }

        // Después del chequeo de idempotencia a propósito: un reintento de algo ya cerrado sigue
        // respondiendo duplicado: true aunque alguien haya limpiado el retiro a mano en la base.
        if (parada.Ruta.RetiroConfirmadoEn is null) return Conflict(MensajeRetiroPendiente);

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

        var pedidos = todosLosPedidos.Where(p => pedidoIds.Contains(p.Id)).ToList();
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

            // E1: acuña el ítem facturable de la entrega — por pedido, no por parada, porque
            // una parada consolidada (RF-14) puede traer más de un pedido a la vez. Nada en el
            // camino "fallido" — confirmado: un intento fallido no factura aparte (§10.2-I).
            if (req.Resultado == "entregado" && pedido.Total is not null)
                cuentaCorriente.AgregarItemDePedido(pedido,
                    $"Pedido #{pedido.Id} — {pedido.DestinatarioNombre}", pedido.Total.Value, repartidorId);
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
                yaExistentes.FirstOrDefault()?.DesvioMetros, desvioAlto, Duplicado: true,
                await SiguienteParadaAsync(parada.RutaId, ct)));
        }

        return Ok(new CierreResultado(paradaId, parada.Estado, pedidos.Count, desvioMetros, desvioAlto, Duplicado: false,
            await SiguienteParadaAsync(parada.RutaId, ct)));
    }

    /// <summary>La pendiente de menor Orden de la ruta, ya aplicado el cierre. Una query.</summary>
    private async Task<long?> SiguienteParadaAsync(long rutaId, CancellationToken ct) =>
        await db.RutaParadas.AsNoTracking()
            .Where(p => p.RutaId == rutaId && p.Estado == "pendiente")
            .OrderBy(p => p.Orden)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);

    private static bool EsViolacionDeUnique(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
