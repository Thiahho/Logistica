using System.Diagnostics;
using System.Security.Claims;

namespace Logistica.Web;

/// <summary>
/// Una línea de log por request (construccion_v1.md changelog 1.42, monitoreo): método, ruta, status,
/// duración y rol. **Sin query ni cuerpo** a propósito: ahí viajan nombres, teléfonos y DNI (RNF-09).
/// Va primero en el pipeline, así mide también lo que resuelve el manejador de excepciones.
/// /health va en Debug: el monitor externo lo consulta cada minuto y taparía todo lo demás.
/// </summary>
public static class RegistroSolicitudes
{
    private const int MilisegundosLento = 2000;

    public static IApplicationBuilder UseRegistroSolicitudes(this IApplicationBuilder app)
    {
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Solicitudes");
        return app.Use(async (contexto, siguiente) =>
        {
            var reloj = Stopwatch.StartNew();
            try
            {
                await siguiente();
            }
            finally
            {
                reloj.Stop();
                var status = contexto.Response.StatusCode;
                var ruta = contexto.Request.Path.Value ?? "";
                var nivel = status >= 500 || reloj.ElapsedMilliseconds > MilisegundosLento ? LogLevel.Warning
                    : ruta.StartsWith("/health") ? LogLevel.Debug
                    : LogLevel.Information;
                log.Log(nivel, "{Metodo} {Ruta} -> {Status} en {Milisegundos} ms (rol {Rol}, traceId {TraceId})",
                    contexto.Request.Method, ruta, status, reloj.ElapsedMilliseconds,
                    contexto.User.FindFirstValue(ClaimTypes.Role) ?? "anonimo", contexto.TraceIdentifier);
            }
        });
    }
}
