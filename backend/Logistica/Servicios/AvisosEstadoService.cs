using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>
/// B6 (Anexo I §5, E4): aviso automático de estado al cliente. Un resumen por día y por cliente, al
/// email de su ficha, solo para los clientes con `avisos_estado` activado. Antes el cliente tenía que
/// entrar al portal para saber qué pasó con sus envíos.
///
/// La fuente es pedido_eventos, que ya registra cada cambio de estado por trigger: acá no se engancha
/// ningún controlador. Cada cliente lleva en `avisos_estado_hasta` hasta dónde se le informó; el
/// resumen cubre desde ahí hasta el último cierre (Opciones: HoraResumen) y recién entonces avanza.
/// Si el correo no sale, no avanza y la corrida siguiente reintenta; si el proceso estuvo apagado, al
/// volver manda lo pendiente. Un cliente sin movimientos avanza sin recibir correo.
/// </summary>
public class AvisosEstadoService(
    LogisticaDbContext db, EmailService email, IOptions<OpcionesAvisosEstado> opciones, ILogger<AvisosEstadoService> log)
{
    /// <summary>Tope de lo que se informa atrasado: si los avisos estuvieron caídos una semana, el
    /// cliente no recibe una semana de historia de golpe.</summary>
    private static readonly TimeSpan MaximoAtras = TimeSpan.FromDays(3);

    /// <summary>El cierre de resumen más reciente que ya pasó: el de hoy si ya es la hora, si no el de ayer.</summary>
    public static DateTimeOffset UltimoCierre(DateTimeOffset ahora, TimeOnly horaResumen)
    {
        var hoy = Reloj.ALaFechaLocal(ahora);
        var cierreDeHoy = Reloj.InstanteLocal(hoy, horaResumen);
        return ahora >= cierreDeHoy ? cierreDeHoy : Reloj.InstanteLocal(hoy.AddDays(-1), horaResumen);
    }

    /// <summary>Manda los resúmenes pendientes. Devuelve cuántos correos salieron (o se simularon).</summary>
    public async Task<int> EnviarPendientesAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var cierre = UltimoCierre(ahora, opciones.Value.HoraResumen);
        var dia = Reloj.ALaFechaLocal(cierre);

        var clientes = await db.Clientes
            .Where(c => c.AvisosEstado && c.Activo && c.Email != null
                && (c.AvisosEstadoHasta == null || c.AvisosEstadoHasta < cierre))
            .ToListAsync(ct);

        var enviados = 0;
        foreach (var cliente in clientes)
        {
            var desde = cliente.AvisosEstadoHasta ?? cierre.AddDays(-1);
            if (desde < cierre - MaximoAtras) desde = cierre - MaximoAtras;

            var eventos = await db.PedidoEventos.AsNoTracking()
                .Where(e => e.Pedido.ClienteId == cliente.Id && e.OcurridoEn > desde && e.OcurridoEn <= cierre
                    && ResumenEnvios.Informables.Contains(e.EstadoNuevo))
                .OrderBy(e => e.OcurridoEn).ThenBy(e => e.Id)
                .Select(e => new
                {
                    e.PedidoId, e.EstadoNuevo, e.Motivo, e.Pedido.DestinatarioNombre,
                    e.Pedido.DestinoUbicacion.CalleNumero,
                    Localidad = e.Pedido.DestinoUbicacion.Localidad != null ? e.Pedido.DestinoUbicacion.Localidad.Nombre : null,
                })
                .ToListAsync(ct);

            // Un envío que salió y se entregó el mismo día cuenta una vez, como entregado.
            var movimientos = eventos
                .GroupBy(e => e.PedidoId)
                .Select(g => g.Last())
                .Select(e => new MovimientoEnvio(
                    e.PedidoId, e.DestinatarioNombre,
                    e.Localidad is null ? e.CalleNumero : $"{e.CalleNumero}, {e.Localidad}", e.EstadoNuevo, e.Motivo))
                .ToList();

            if (ResumenEnvios.Armar(cliente.RazonSocial, dia, movimientos) is { } mensaje)
            {
                var resultado = await email.EnviarAsync(cliente.Email!, mensaje.Asunto, mensaje.Cuerpo, ct);
                if (!resultado.Enviado && !resultado.Simulado)
                {
                    log.LogWarning(
                        "Resumen de envíos del {Dia} no enviado al cliente {ClienteId}: {Error}. Se reintenta en la próxima corrida",
                        dia, cliente.Id, resultado.Error);
                    continue;
                }
                enviados++;
            }

            cliente.AvisosEstadoHasta = cierre;
            // Sin el token del caller: si el correo ya salió, que quede anotado aunque el host se esté apagando.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return enviados;
    }
}

/// <summary>Corre AvisosEstadoService cada media hora. Nunca tira: un error se anota y la corrida
/// siguiente reintenta.</summary>
public class AvisosEstadoAutomatico(
    IServiceScopeFactory scopes, IOptions<OpcionesAvisosEstado> opciones, ILogger<AvisosEstadoAutomatico> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!opciones.Value.Habilitado)
        {
            log.LogInformation("Resumen diario de envíos apagado (AvisosEstado:Habilitado)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(30));
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var enviados = await scope.ServiceProvider.GetRequiredService<AvisosEstadoService>()
                        .EnviarPendientesAsync(DateTimeOffset.UtcNow, ct);
                    if (enviados > 0) log.LogInformation("Resumen diario de envíos: {Enviados} correos", enviados);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Resumen diario de envíos: la corrida falló; se reintenta en la próxima");
                }
            } while (await reloj.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // El host se está apagando.
        }
    }
}
