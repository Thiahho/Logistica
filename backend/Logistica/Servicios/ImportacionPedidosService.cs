using System.Diagnostics;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>ok: se puede importar. aviso: se puede, pero parece repetida. error: no se importa.</summary>
public record FilaPrevista(
    int Numero, string Estado, IReadOnlyList<string> Mensajes, FilaPlanilla Original, PedidoImportable? Pedido);

public record FilaImportada(int Numero, long? PedidoId, string? Error, bool DireccionDudosa);

/// <summary>
/// B11 (Anexo I §5, E4): importación masiva de pedidos de un cliente, desde el back-office. Dos pasos:
/// previsualizar (no escribe nada) e importar. Importar vuelve a validar cada fila — lo que llega del
/// navegador no se da por bueno — y da de alta fila por fila: una que falla no arrastra a las demás.
/// Los pedidos nacen igual que en PedidosController.Crear (borrador, sin precio), con
/// origen_carga = 'importado' (RF-06).
/// </summary>
public class ImportacionPedidosService(
    LogisticaDbContext db, UbicacionService ubicaciones, OrigenRutaService origenes,
    CuentaCorrienteService cuentaCorriente, IOptions<OpcionesCarga> opcionesCarga,
    ILogger<ImportacionPedidosService> log)
{
    /// <summary>Nominatim admite ~1 consulta por segundo (construccion_v1.md §1): entre dos direcciones
    /// nuevas se espera. Una dirección ya conocida no consulta al geocoder y no espera.</summary>
    private static readonly TimeSpan PausaGeocoder = TimeSpan.FromMilliseconds(1100);

    /// <summary>Lo mismo que frena un alta de a uno: cliente inexistente, inactivo o con el servicio
    /// cortado por deuda vencida (§10.2-L). null si el cliente puede recibir pedidos.</summary>
    public async Task<string?> ErrorDeClienteAsync(int clienteId, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => new { c.RazonSocial, c.Activo, c.CorteSuspendidoHasta })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return "El cliente no existe.";
        if (!cliente.Activo) return $"{cliente.RazonSocial} está inactivo; no admite pedidos nuevos.";

        var hoy = Reloj.HoyLocal();
        if (cliente.CorteSuspendidoHasta is { } hasta && hasta >= hoy) return null;
        var deuda = await cuentaCorriente.DeudaVencidaAsync(clienteId, hoy, ct);
        return deuda > 0
            ? $"Servicio cortado por deuda vencida: {cliente.RazonSocial} adeuda ${deuda:N2} de comprobantes vencidos."
            : null;
    }

    public async Task<List<FilaPrevista>> PrevisualizarAsync(int clienteId, IReadOnlyList<FilaPlanilla> filas, CancellationToken ct)
    {
        var validadas = await ValidarAsync(filas, ct);

        // Posibles repetidos: contra lo ya cargado para este cliente en esas fechas, y dentro del mismo
        // archivo. Es lo que pasa al volver a subir una planilla corregida.
        var fechas = validadas.Where(v => v.Pedido is not null).Select(v => v.Pedido!.FechaEntrega).Distinct().ToList();
        var cargados = (await db.Pedidos.AsNoTracking()
                .Where(p => p.ClienteId == clienteId && fechas.Contains(p.FechaEntrega) && p.Estado != EstadoPedido.Cancelado)
                .Select(p => new { p.Id, p.DestinatarioNombre, p.DestinoUbicacion.CalleNumero, p.DestinoUbicacion.LocalidadId, p.FechaEntrega })
                .ToListAsync(ct))
            .Where(p => p.LocalidadId is not null)
            .GroupBy(p => ImportacionPedidos.ClaveDuplicado(p.DestinatarioNombre, p.CalleNumero, p.LocalidadId!.Value, p.FechaEntrega))
            .ToDictionary(g => g.Key, g => g.Min(p => p.Id));

        var vistas = new Dictionary<string, int>();
        var resultado = new List<FilaPrevista>();
        foreach (var (validada, original) in validadas.Zip(filas))
        {
            if (validada.Pedido is not { } pedido)
            {
                resultado.Add(new FilaPrevista(validada.Numero, "error", validada.Errores, original, null));
                continue;
            }

            var clave = ImportacionPedidos.ClaveDuplicado(pedido.DestinatarioNombre, pedido.CalleNumero, pedido.LocalidadId, pedido.FechaEntrega);
            var avisos = new List<string>();
            if (cargados.TryGetValue(clave, out var pedidoId))
                avisos.Add($"Ya hay un pedido igual cargado para ese día (#{pedidoId}).");
            if (vistas.TryGetValue(clave, out var filaAnterior))
                avisos.Add($"Repite la fila {filaAnterior} de la planilla.");
            else
                vistas[clave] = validada.Numero;

            resultado.Add(new FilaPrevista(validada.Numero, avisos.Count > 0 ? "aviso" : "ok", avisos, original, pedido));
        }
        return resultado;
    }

    public async Task<List<FilaImportada>> ImportarAsync(
        int clienteId, IReadOnlyList<FilaPlanilla> filas, Guid actor, CancellationToken ct)
    {
        var validadas = await ValidarAsync(filas, ct);
        var localidadIds = validadas.Where(v => v.Pedido is not null).Select(v => v.Pedido!.LocalidadId).Distinct().ToList();
        var zonas = await db.Localidades.AsNoTracking()
            .Where(l => localidadIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.ZonaId, ct);
        var origen = await origenes.PrincipalParaPedidosAsync(ct);

        var resultado = new List<FilaImportada>();
        Stopwatch? desdeElUltimoGeocoder = null;
        foreach (var validada in validadas)
        {
            if (validada.Pedido is not { } fila)
            {
                resultado.Add(new FilaImportada(validada.Numero, null, string.Join(" ", validada.Errores), false));
                continue;
            }

            try
            {
                var calle = UbicacionService.NormalizarCalle(fila.CalleNumero);
                var conocida = await db.Ubicaciones.AnyAsync(
                    u => u.LocalidadId == fila.LocalidadId && u.CalleNumero.ToLower() == calle.ToLower(), ct);
                if (!conocida)
                {
                    if (desdeElUltimoGeocoder is not null && desdeElUltimoGeocoder.Elapsed < PausaGeocoder)
                        await Task.Delay(PausaGeocoder - desdeElUltimoGeocoder.Elapsed, ct);
                    desdeElUltimoGeocoder = Stopwatch.StartNew();
                }
                var destino = await ubicaciones.ResolverOCrearAsync(fila.CalleNumero, fila.LocalidadId, fila.ReferenciaDireccion, ct);

                var pedido = new Pedido
                {
                    ClienteId = clienteId,
                    ReferenciaCliente = fila.ReferenciaCliente,
                    OrigenUbicacionId = origen.UbicacionId,
                    DestinoUbicacionId = destino.Id,
                    DestinatarioNombre = fila.DestinatarioNombre,
                    DestinatarioTelefono = fila.DestinatarioTelefono,
                    Bultos = fila.Bultos,
                    PesoKg = fila.PesoKg,
                    ValorDeclarado = fila.ValorDeclarado,
                    FechaEntrega = fila.FechaEntrega,
                    Urgente = fila.Urgente,
                    ZonaId = zonas[fila.LocalidadId],
                    Estado = EstadoPedido.Borrador,
                    OrigenCarga = "importado",
                    Observaciones = fila.Observaciones,
                    CreadoEn = DateTimeOffset.UtcNow,
                };
                db.Pedidos.Add(pedido);
                await db.GuardarComoAsync(actor, ct: ct);

                resultado.Add(new FilaImportada(
                    validada.Numero, pedido.Id, null,
                    DireccionDudosa: destino.GeoConfianza is not ("alta" or "media") && !destino.Verificada));
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                // Lo que quedó a medio guardar de esta fila no puede contaminar el alta de la siguiente.
                db.ChangeTracker.Clear();
                log.LogWarning(ex, "Importación de pedidos: no se pudo cargar la fila {Fila} del cliente {ClienteId}", validada.Numero, clienteId);
                resultado.Add(new FilaImportada(validada.Numero, null, "No se pudo cargar esta fila.", false));
            }
        }
        return resultado;
    }

    private async Task<List<FilaValidada>> ValidarAsync(IReadOnlyList<FilaPlanilla> filas, CancellationToken ct)
    {
        var localidades = await db.Localidades.AsNoTracking()
            .Select(l => new LocalidadImportable(l.Id, l.Nombre, l.Partido, l.ZonaId != null))
            .ToListAsync(ct);
        var (hoy, ahora, corte) = (Reloj.HoyLocal(), Reloj.HoraLocal(), opcionesCarga.Value.HoraCorteDiaSiguiente);
        return filas.Select(f => ImportacionPedidos.Validar(f, hoy, ahora, corte, localidades)).ToList();
    }
}
