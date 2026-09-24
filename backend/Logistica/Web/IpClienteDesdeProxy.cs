using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Logistica.Web;

/// <summary>
/// IP real del usuario detrás de Vercel → Render (auditoria_seguridad.md hallazgo 14).
///
/// UseForwardedHeaders solo no alcanza: el backend también es alcanzable directo en *.onrender.com,
/// y ahí un atacante escribe su propio X-Forwarded-For (rotándolo, esquiva el límite de login). Por
/// eso la IP de Vercel se acepta solo si el request trae el secreto que agrega frontend/proxy.ts —
/// un request que no pasó por el frontend queda con la IP del salto de Render, que no se falsifica.
///
/// Las dos cabeceras se quitan siempre, con o sin secreto válido: ningún código de más abajo las ve.
/// </summary>
public static class IpClienteDesdeProxy
{
    public const string CabeceraSecreto = "X-Proxy-Secreto";
    public const string CabeceraIp = "X-Cliente-Ip";

    public static IApplicationBuilder UseIpClienteDesdeProxy(this IApplicationBuilder app, string secreto) =>
        app.Use(async (contexto, siguiente) =>
        {
            var ip = Resolver(contexto.Request.Headers, secreto);
            if (ip is not null) contexto.Connection.RemoteIpAddress = ip;
            contexto.Request.Headers.Remove(CabeceraSecreto);
            contexto.Request.Headers.Remove(CabeceraIp);
            await siguiente();
        });

    /// <summary>La IP informada por el frontend, o null si el secreto falta, no coincide o la IP no
    /// es una dirección válida.</summary>
    public static IPAddress? Resolver(IHeaderDictionary cabeceras, string secreto)
    {
        var recibido = cabeceras[CabeceraSecreto].ToString();
        if (recibido.Length == 0 || !SecretoCoincide(recibido, secreto)) return null;

        return IPAddress.TryParse(cabeceras[CabeceraIp].ToString().Trim(), out var ip) ? ip : null;
    }

    // Tiempo constante: una comparación que corta en el primer carácter distinto deja adivinar el
    // secreto midiendo cuánto tarda la respuesta.
    private static bool SecretoCoincide(string recibido, string esperado) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recibido), Encoding.UTF8.GetBytes(esperado));
}
