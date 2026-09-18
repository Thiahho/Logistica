using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    AlmacenamientoFotos almacenamiento) : ControllerBase
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
}
