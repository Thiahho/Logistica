using Logistica.Dominio;
using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>
/// Cierra solo los ciclos de facturación vencidos (E1, Anexo I §10.2-A). Antes el cierre dependía de
/// que Administración entrara a /facturas y lo disparara; se recuperaba si se atrasaba, pero nadie
/// facturaba mientras tanto.
///
/// No calcula nada propio: llama a CuentaCorrienteService.CerrarCiclosAsync, el mismo camino que el
/// cierre manual, que sigue disponible. Cierra hasta AYER: un período termina a la medianoche de su
/// último día, no durante el día. Si el proceso estuvo apagado (deploy, hosting dormido), la corrida
/// siguiente emite todo lo pendiente; repetirla es inocuo (ux_facturas_periodo).
///
/// Los ajustes sin aprobar no frenan la factura: entran en la siguiente cuando se aprueben, igual que
/// en el cierre manual. Las facturas automáticas quedan sin `emitida_por`.
/// </summary>
public class CierreCiclosAutomatico(
    IServiceScopeFactory scopes, IOptions<OpcionesFacturacion> opciones, IHostEnvironment entorno,
    ILogger<CierreCiclosAutomatico> log) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var prendido = opciones.Value.CierreAutomatico ?? !entorno.IsDevelopment();
        if (!prendido)
        {
            log.LogInformation("Cierre automático de facturación apagado (Facturacion:CierreAutomatico)");
            return;
        }

        try
        {
            // Después del calentamiento y de que el hosting dé por levantado el servicio.
            await Task.Delay(EsperaInicial, ct);
            using var reloj = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, opciones.Value.IntervaloHoras)));
            do
            {
                await CorrerAsync(ct);
            } while (await reloj.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // El host se está apagando.
        }
    }

    /// <summary>Una corrida. Nunca tira: un error se anota y la corrida siguiente reintenta.</summary>
    private async Task CorrerAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var cuentaCorriente = scope.ServiceProvider.GetRequiredService<CuentaCorrienteService>();
            var hasta = Reloj.HoyLocal().AddDays(-1);
            var resultados = await cuentaCorriente.CerrarCiclosAsync(hasta, clienteId: null, previsualizar: false, actor: null, ct);

            var emitidas = resultados.Where(r => r.FacturaId is not null).ToList();
            if (emitidas.Count > 0)
                log.LogInformation(
                    "Cierre automático de facturación hasta {Hasta}: {Facturas} facturas por {Total}",
                    hasta, emitidas.Count, emitidas.Sum(r => r.Total));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Cierre automático de facturación: la corrida falló; se reintenta en la próxima");
        }
    }
}
