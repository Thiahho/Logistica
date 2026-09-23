using Logistica.Auth;
using Logistica.Datos;
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
/// Escrituras del repartidor sobre SU ruta (RF-35 y la mitad de calle de RF-26, acta changelog
/// 4.7). Hermano de MisParadasController, que escribe por parada contra v_paradas_repartidor:
/// este toca `rutas`, y solo la ruta en_curso del repartidor autenticado. Recurso distinto,
/// controller distinto (construccion_v1.md §2, un controller por recurso).
///
/// NO es liquidación (B4/E2, disparador "segundo repartidor", bloqueada por la definición abierta
/// del Anexo I §10.2-C): no escribe ni devuelve rutas.pago_repartidor ni otros_costos, y ninguna
/// respuesta de este controller contiene un precio, un margen ni un importe que el repartidor no
/// haya tipeado él mismo (RNF-08, construccion_v1.md §3 regla 4).
///
/// Tampoco cierra la ruta. Lo que el repartidor escribe es una DECLARACIÓN pendiente e inmutable
/// (trg_congelar_declaracion_repartidor); el cierre económico sigue siendo de administración
/// (RutasController.Cerrar), que la compara con sus propios números y la aprueba o la corrige con
/// motivo escrito. Los dos juegos de columnas conviven en la fila a propósito — acta §7, "el
/// número de la calle no se reescribe".
/// </summary>
[ApiController]
[Route("api/mi-jornada")]
[Authorize(Policy = "Repartidor")]
public class MiJornadaController(
    LogisticaDbContext db,
    AlmacenamientoFotos almacenamiento,
    IOptions<OpcionesPruebaEntrega> opciones) : ControllerBase
{
    /// <summary>Clase, no record posicional: [FromForm] + IFormFile en un record posicional es
    /// frágil con el model binder de multipart/form-data (mismo motivo que
    /// MisParadasController.CerrarParadaRequest).</summary>
    public class RetiroRequest
    {
        public string DeviceUuid { get; set; } = null!;

        /// <summary>RNF-03: la hora que vale es la del dispositivo, no now() del servidor.</summary>
        public DateTimeOffset CapturadaEn { get; set; }

        public int BultosContados { get; set; }

        /// <summary>Odómetro al salir. Va a rutas.retiro_km_inicial, NUNCA a rutas.km_inicial:
        /// ese es del cierre de administración, que puede aprobar este número o corregirlo.</summary>
        public int KmInicial { get; set; }

        /// <summary>Obligatorio solo si BultosContados difiere de los esperados.</summary>
        public string? Observaciones { get; set; }

        public IFormFile? Firma { get; set; }
    }

    public record RetiroResultado(
        long RutaId, DateTimeOffset RetiroConfirmadoEn, int BultosEsperados, int BultosContados,
        bool Discrepancia, bool Duplicado);

    /// <summary>
    /// RF-35 / acta §7: "ninguna ruta sale sin conteo firmado". Sin esto, MisParadasController
    /// rechaza registrar llegada y cerrar parada.
    /// </summary>
    [HttpPost("retiro")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    public async Task<IActionResult> Retiro([FromForm] RetiroRequest req, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var ruta = await db.Rutas
            .Where(r => r.RepartidorId == repartidorId && r.Estado == "en_curso")
            .OrderByDescending(r => r.Fecha)
            .FirstOrDefaultAsync(ct);
        if (ruta is null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.DeviceUuid) || !Guid.TryParse(req.DeviceUuid, out _))
            return BadRequest("DeviceUuid inválido.");

        // Idempotencia ANTES que cualquier validación (RNF-02): en la calle, ante una pantalla que
        // no responde, el botón se toca dos veces. El segundo tap no es un error ni un conflicto,
        // y tiene que responder lo mismo que el primero.
        if (ruta.RetiroConfirmadoEn is not null)
        {
            if (ruta.RetiroDeviceUuid == req.DeviceUuid)
                return Ok(new RetiroResultado(
                    ruta.Id, ruta.RetiroConfirmadoEn.Value,
                    ruta.RetiroBultosEsperados ?? 0, ruta.RetiroBultosContados ?? 0,
                    ruta.RetiroBultosContados != ruta.RetiroBultosEsperados, Duplicado: true));

            return Conflict("El retiro de esta ruta ya lo firmó otro dispositivo.");
        }

        if (req.BultosContados < 0 || req.KmInicial < 0)
            return BadRequest("Los bultos contados y el km inicial no pueden ser negativos.");

        // Los bultos esperados los cuenta el SERVIDOR, nunca llegan del cliente: son la lista
        // contra la que se firma. Se congelan en la fila para que un cambio posterior en los
        // pedidos no reescriba retroactivamente contra qué se contó.
        var bultosEsperados = await db.ParadaPedidos
            .Where(pp => pp.Parada.RutaId == ruta.Id)
            .SumAsync(pp => (int?)pp.Pedido.Bultos, ct) ?? 0;

        var discrepancia = req.BultosContados != bultosEsperados;
        if (discrepancia && string.IsNullOrWhiteSpace(req.Observaciones))
            return BadRequest($"El conteo ({req.BultosContados}) no coincide con los {bultosEsperados} bultos de la ruta: hace falta una observación antes de firmar.");

        // Lo único que el acta §7 exige sin excepción.
        if (req.Firma is null)
            return BadRequest("El retiro necesita la firma del repartidor.");

        // La firma se escribe a disco ANTES de la transacción, mismo criterio que
        // MisParadasController.Cerrar con la foto: si después falla la base, queda un archivo
        // huérfano inofensivo que el reintento pisa. Al revés quedaría una fila apuntando a un
        // archivo inexistente, irreparable sin intervención manual.
        string firmaPath;
        await using (var stream = req.Firma.OpenReadStream())
            firmaPath = await almacenamiento.GuardarAsync(
                ruta.Id, req.DeviceUuid, stream, req.Firma.Length, ct, carpeta: "retiros");

        // Npgsql exige offset 0 para timestamptz; el dispositivo manda su offset local (RNF-03).
        // ToUniversalTime() no cambia el instante, solo la representación.
        var capturadaEnUtc = req.CapturadaEn.ToUniversalTime();

        ruta.RetiroConfirmadoEn = capturadaEnUtc;
        ruta.RetiroBultosEsperados = bultosEsperados;
        ruta.RetiroBultosContados = req.BultosContados;
        ruta.RetiroObservaciones = string.IsNullOrWhiteSpace(req.Observaciones) ? null : req.Observaciones.Trim();
        ruta.RetiroFirmaPath = firmaPath;
        ruta.RetiroKmInicial = req.KmInicial;
        ruta.RetiroDeviceUuid = req.DeviceUuid;

        // SaveChangesAsync directo, NO GuardarComoAsync: esto no toca `pedidos`, así que el GUC
        // app.usuario_id no tiene consumidor (fn_log_estado_pedido solo mira pedidos). Mismo
        // criterio ya documentado en MisParadasController.RegistrarLlegada y RutasController.Cerrar
        // — a primera vista parece violar la regla 7, no la viola.
        await db.SaveChangesAsync(ct);

        return Ok(new RetiroResultado(
            ruta.Id, capturadaEnUtc, bultosEsperados, req.BultosContados, discrepancia, Duplicado: false));
    }

    public class CierreJornadaRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string DeviceUuid { get; set; } = null!;

        /// <summary>RNF-03: la hora que vale es la del dispositivo, no now() del servidor.</summary>
        public DateTimeOffset CapturadaEn { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue, ErrorMessage = "El km final no puede ser negativo.")]
        public int KmFinal { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue, ErrorMessage = "El combustible no puede ser negativo.")]
        public decimal CombustibleMonto { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue, ErrorMessage = "Los peajes no pueden ser negativos.")]
        public decimal PeajesMonto { get; set; }

        public string? Notas { get; set; }
    }

    /// <summary>Solo conteos y lo que el repartidor tipeó él mismo — ningún importe de la empresa
    /// (RNF-08). Combustible y peajes son gastos que pagó él y ya conoce, no precios.</summary>
    public record CierreJornadaResultado(
        long RutaId, DateTimeOffset CierreConfirmadoEn, int Total, int Entregadas, int Fallidas,
        int KmInicial, int KmFinal, int KmRecorridos, decimal CombustibleMonto, decimal PeajesMonto,
        string? Notas, bool Duplicado);

    /// <summary>
    /// RF-26, mitad de calle. La DECLARACIÓN del repartidor, no el cierre de la ruta: la ruta sigue
    /// en_curso hasta que administración la compara y la cierra (RutasController.Cerrar). Inmutable
    /// una vez sellada (trg_congelar_declaracion_repartidor) — el controller solo tiene el camino de
    /// idempotencia; si algo intenta editarla, el trigger lo rechaza y ManejadorExcepciones lo traduce.
    /// </summary>
    [HttpPost("cierre")]
    public async Task<IActionResult> Cierre(CierreJornadaRequest req, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var ruta = await db.Rutas
            .Where(r => r.RepartidorId == repartidorId && r.Estado == "en_curso")
            .OrderByDescending(r => r.Fecha)
            .FirstOrDefaultAsync(ct);
        if (ruta is null) return NotFound();

        if (!Guid.TryParse(req.DeviceUuid, out _))
            return BadRequest("DeviceUuid inválido.");

        var estados = await db.RutaParadas.AsNoTracking()
            .Where(p => p.RutaId == ruta.Id)
            .GroupBy(p => p.Estado)
            .Select(g => new { Estado = g.Key, Cantidad = g.Count() })
            .ToListAsync(ct);
        int Cuantas(string estado) => estados.FirstOrDefault(e => e.Estado == estado)?.Cantidad ?? 0;

        // Idempotencia primero (RNF-02), igual que el retiro: el segundo tap responde lo mismo que el primero.
        if (ruta.CierreRepartidorEn is not null)
        {
            if (ruta.CierreRepartidorDeviceUuid == req.DeviceUuid)
                return Ok(ArmarCierre(ruta, estados.Sum(e => e.Cantidad), Cuantas("completada"), Cuantas("fallida"), duplicado: true));

            return Conflict("El cierre de esta ruta ya lo declaró otro dispositivo.");
        }

        if (ruta.RetiroConfirmadoEn is null || ruta.RetiroKmInicial is null)
            return Conflict("La ruta no tiene el retiro confirmado: no hay km inicial contra el cual cerrar.");

        var pendientes = Cuantas("pendiente");
        if (pendientes > 0)
            return Conflict($"Todavía te quedan {pendientes} parada(s) pendientes. La jornada se cierra cuando la calle terminó.");

        // Contra lo que ÉL declaró al retirar, no contra rutas.km_inicial (todavía null: es del cierre de administración).
        if (req.KmFinal < ruta.RetiroKmInicial)
            return BadRequest($"El km final ({req.KmFinal}) no puede ser menor al km con el que salió ({ruta.RetiroKmInicial}).");

        // Solo cierre_repartidor_*: Estado, km_final, combustible_monto, peajes_monto, otros_costos y
        // pago_repartidor son de administración.
        ruta.CierreRepartidorEn = req.CapturadaEn.ToUniversalTime();
        ruta.CierreRepartidorKmFinal = req.KmFinal;
        ruta.CierreRepartidorCombustible = req.CombustibleMonto;
        ruta.CierreRepartidorPeajes = req.PeajesMonto;
        ruta.CierreRepartidorNotas = string.IsNullOrWhiteSpace(req.Notas) ? null : req.Notas.Trim();
        ruta.CierreRepartidorDeviceUuid = req.DeviceUuid;

        // SaveChangesAsync directo, no GuardarComoAsync: no toca `pedidos` (mismo criterio que Retiro).
        await db.SaveChangesAsync(ct);

        return Ok(ArmarCierre(ruta, estados.Sum(e => e.Cantidad), Cuantas("completada"), Cuantas("fallida"), duplicado: false));
    }

    // ---------------------------------------------------------------- Novedades (RF-36)

    private static readonly string[] TiposDelRepartidor = ["incidencia_ruta", "problema_carga", "cambio_propuesto"];

    /// <summary>Whitelist de lo que se puede proponer cambiar de un pedido en vivo: exactamente lo que
    /// fn_congelar_pedido NO protege. Destino y precio están congelados desde que el pedido sale de
    /// Borrador (P1) — "dirección incorrecta" es una entrega fallida, no una edición.</summary>
    private static readonly string[] CamposEditables = ["destinatario_telefono", "destinatario_nombre", "observaciones"];

    /// <summary>Clase, no record posicional: [FromForm] + IFormFile en un record posicional es frágil.</summary>
    public class NovedadRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string DeviceUuid { get; set; } = null!;

        /// <summary>incidencia_ruta | problema_carga | cambio_propuesto</summary>
        public string Tipo { get; set; } = null!;

        public string? Categoria { get; set; }
        public string? Descripcion { get; set; }
        public long? ParadaId { get; set; }
        public long? PedidoId { get; set; }

        /// <summary>Solo cambio_propuesto: destinatario_telefono | destinatario_nombre | observaciones.</summary>
        public string? Campo { get; set; }
        public string? ValorNuevo { get; set; }

        public IFormFile? Foto { get; set; }
    }

    public record NovedadResultado(long Id, string Tipo, string Estado, bool Duplicado);

    /// <summary>
    /// El repartidor informa algo de SU ruta en curso: una incidencia de ruta/vehículo, un problema con
    /// la carga o una corrección de un dato de contacto. Nada de esto cambia un pedido por sí solo: queda
    /// abierto hasta que operación lo resuelve (NovedadesController), y recién ahí un cambio propuesto
    /// se aplica. Idempotente por DeviceUuid (RNF-02). Escribe con SaveChangesAsync plano: no toca pedidos.
    /// </summary>
    [HttpPost("novedades")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    public async Task<IActionResult> CrearNovedad([FromForm] NovedadRequest req, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var ruta = await db.Rutas.AsNoTracking()
            .Where(r => r.RepartidorId == repartidorId && r.Estado == "en_curso")
            .OrderByDescending(r => r.Fecha)
            .FirstOrDefaultAsync(ct);
        if (ruta is null) return NotFound();

        if (!Guid.TryParse(req.DeviceUuid, out _))
            return BadRequest("DeviceUuid inválido.");

        // Idempotencia antes que cualquier validación: el segundo tap responde lo mismo que el primero.
        var previa = await db.Novedades.AsNoTracking()
            .FirstOrDefaultAsync(n => n.RutaId == ruta.Id && n.DeviceUuid == req.DeviceUuid, ct);
        if (previa is not null)
            return Ok(new NovedadResultado(previa.Id, previa.Tipo, previa.Estado, Duplicado: true));

        if (!TiposDelRepartidor.Contains(req.Tipo))
            return BadRequest("Tipo de novedad inválido.");

        // Categoría: lista cerrada por tipo, en configuración. cambio_propuesto no lleva categoría.
        string? categoria = null;
        if (req.Tipo != "cambio_propuesto")
        {
            var validas = req.Tipo == "incidencia_ruta"
                ? opciones.Value.CategoriasIncidencia
                : opciones.Value.CategoriasCarga;
            if (string.IsNullOrWhiteSpace(req.Categoria) || !validas.Contains(req.Categoria))
                return BadRequest("La categoría debe ser una de la lista.");
            categoria = req.Categoria;
        }

        var descripcion = req.Descripcion?.Trim();

        // La parada y el pedido tienen que ser de SU ruta: el id llega del cliente. 404 y no 403, mismo
        // criterio que el resto de MisParadas — no se confirma que exista en otra ruta.
        long? paradaId = req.ParadaId;
        if (paradaId is not null &&
            !await db.RutaParadas.AnyAsync(p => p.Id == paradaId && p.RutaId == ruta.Id, ct))
            return NotFound();

        if (req.PedidoId is not null)
        {
            var paradaDelPedido = await db.ParadaPedidos
                .Where(pp => pp.PedidoId == req.PedidoId && pp.Parada.RutaId == ruta.Id)
                .Select(pp => (long?)pp.ParadaId)
                .FirstOrDefaultAsync(ct);
            if (paradaDelPedido is null) return NotFound();
            if (paradaId is not null && paradaId != paradaDelPedido)
                return BadRequest("El pedido no pertenece a esa parada.");
            paradaId = paradaDelPedido;
        }

        string? campo = null, valorAnterior = null, valorNuevo = null;
        switch (req.Tipo)
        {
            case "incidencia_ruta":
                if (string.IsNullOrWhiteSpace(descripcion))
                    return BadRequest("Contá qué pasó en una línea.");
                break;

            case "problema_carga":
                if (req.PedidoId is null && paradaId is null)
                    return BadRequest("Indicá en qué parada o pedido está el problema.");
                if (string.IsNullOrWhiteSpace(descripcion) && req.Foto is null)
                    return BadRequest("Agregá una nota o una foto del problema.");
                break;

            case "cambio_propuesto":
                if (req.PedidoId is null) return BadRequest("Indicá qué pedido querés corregir.");
                if (req.Campo is null || !CamposEditables.Contains(req.Campo))
                    return BadRequest("Solo se puede corregir el teléfono, el nombre del destinatario o las observaciones. La dirección y el precio no se editan: si la dirección es incorrecta, informá la entrega como fallida.");
                if (string.IsNullOrWhiteSpace(req.ValorNuevo))
                    return BadRequest("Falta el valor nuevo.");

                // Regla 3.4: el repartidor lee de v_paradas_repartidor, nunca de pedidos. El valor
                // anterior lo pone el servidor — lo que el cliente crea que había no cuenta.
                var actual = await db.Set<ParadaRepartidor>().AsNoTracking()
                    .Where(v => v.PedidoId == req.PedidoId && v.RutaId == ruta.Id)
                    .Select(v => new { v.DestinatarioTelefono, v.DestinatarioNombre, v.Observaciones })
                    .FirstOrDefaultAsync(ct);
                if (actual is null) return NotFound();

                campo = req.Campo;
                valorNuevo = req.ValorNuevo.Trim();
                valorAnterior = campo switch
                {
                    "destinatario_telefono" => actual.DestinatarioTelefono,
                    "destinatario_nombre" => actual.DestinatarioNombre,
                    _ => actual.Observaciones,
                };
                if (valorAnterior == valorNuevo)
                    return BadRequest("El valor nuevo es igual al actual.");
                descripcion = string.IsNullOrWhiteSpace(descripcion) ? "Corrección propuesta desde la calle." : descripcion;
                break;
        }

        // La foto se escribe a disco ANTES de la fila: un archivo huérfano es inofensivo, una fila que
        // apunta a un archivo inexistente no (mismo criterio que Retiro y Cerrar).
        string? fotoPath = null;
        if (req.Foto is not null)
        {
            await using var stream = req.Foto.OpenReadStream();
            fotoPath = await almacenamiento.GuardarAsync(
                ruta.Id, req.DeviceUuid, stream, req.Foto.Length, ct, carpeta: "novedades");
        }

        var ahora = DateTimeOffset.UtcNow;
        var novedad = new Novedad
        {
            RutaId = ruta.Id,
            ParadaId = paradaId,
            PedidoId = req.PedidoId,
            Tipo = req.Tipo,
            Origen = "repartidor",
            Categoria = categoria,
            Descripcion = descripcion ?? "",
            PropuestaCampo = campo,
            PropuestaValorAnterior = valorAnterior,
            PropuestaValorNuevo = valorNuevo,
            FotoPath = fotoPath,
            DeviceUuid = req.DeviceUuid,
            Estado = "abierta",
            CreadaPor = repartidorId,
            CreadaEn = ahora,
            // Lo creó él: no tiene nada que acusar. Vuelve a null recién cuando operación responde.
            VistoEn = ahora,
        };
        db.Novedades.Add(novedad);

        try
        {
            // SaveChangesAsync directo, no GuardarComoAsync: no toca `pedidos`.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Carrera de dos taps que pasaron el chequeo de idempotencia a la vez.
            var yaExiste = await db.Novedades.AsNoTracking()
                .FirstAsync(n => n.RutaId == ruta.Id && n.DeviceUuid == req.DeviceUuid, ct);
            return Ok(new NovedadResultado(yaExiste.Id, yaExiste.Tipo, yaExiste.Estado, Duplicado: true));
        }

        return Ok(new NovedadResultado(novedad.Id, novedad.Tipo, novedad.Estado, Duplicado: false));
    }

    /// <summary>Acuse de recibo de un aviso de operación (cambio, cancelación) o de la respuesta a algo que
    /// el repartidor informó. Idempotente. Solo sobre novedades de SU ruta en curso.</summary>
    [HttpPost("novedades/{id:long}/visto")]
    public async Task<IActionResult> MarcarVista(long id, CancellationToken ct)
    {
        var repartidorId = User.UsuarioId();

        var novedad = await db.Novedades
            .SingleOrDefaultAsync(n => n.Id == id && n.Ruta.RepartidorId == repartidorId && n.Ruta.Estado == "en_curso", ct);
        if (novedad is null) return NotFound();

        if (novedad.VistoEn is null)
        {
            novedad.VistoEn = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    private static CierreJornadaResultado ArmarCierre(Ruta ruta, int total, int entregadas, int fallidas, bool duplicado) =>
        new(ruta.Id, ruta.CierreRepartidorEn!.Value, total, entregadas, fallidas,
            ruta.RetiroKmInicial ?? 0, ruta.CierreRepartidorKmFinal ?? 0,
            (ruta.CierreRepartidorKmFinal ?? 0) - (ruta.RetiroKmInicial ?? 0),
            ruta.CierreRepartidorCombustible ?? 0, ruta.CierreRepartidorPeajes ?? 0,
            ruta.CierreRepartidorNotas, duplicado);
}
