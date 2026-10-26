using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Logistica.Datos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Auth;

/// <summary>
/// Chequeo contra la base de cada access token ya validado (firma, emisor, vida), desde
/// JwtBearerEvents.OnTokenValidated (Program.cs). Una baja, una contraseña nueva o un rol nuevo
/// tienen que valer en el próximo request, no cuando vence el token (Jwt:AccessMinutos): el login
/// tiene que existir, estar activo y el token tiene que ser posterior a su último corte de sesiones
/// (SesionesRevocadasEn). Sin caché a propósito: una búsqueda por clave primaria por request.
/// </summary>
public static class ValidacionDeSesion
{
    /// <summary>Momento de emisión del access token en milisegundos Unix. El iat estándar es de un
    /// segundo y un login justo después de un corte cae casi siempre en ese mismo segundo: con iat
    /// habría que rechazarlo (o aceptar tokens viejos de ese segundo).</summary>
    public const string ClaimEmitido = "emitido_ms";

    /// <summary>Ahora truncado al milisegundo. Emisión y corte usan la misma precisión, así un token
    /// emitido en el mismo milisegundo que el corte, después de él, no se rechaza; el costo es que uno
    /// emitido en ese mismo milisegundo pero antes del corte sigue valiendo.</summary>
    public static DateTimeOffset Ahora() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static async Task<bool> EsVigenteAsync(LogisticaDbContext db, ClaimsPrincipal sesion, CancellationToken ct = default)
    {
        if (!Guid.TryParse(sesion.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)) return false;

        // cliente_id solo lo llevan los logins del portal (TokenService).
        var login = sesion.ClienteId() is null
            ? await db.Usuarios.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new { u.Activo, u.SesionesRevocadasEn }).SingleOrDefaultAsync(ct)
            : await db.ClientesUsuarios.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new { u.Activo, u.SesionesRevocadasEn }).SingleOrDefaultAsync(ct);

        if (login is null || !login.Activo) return false;
        if (login.SesionesRevocadasEn is not { } corte) return true;

        // Sin el claim es un token emitido antes de este cambio, y los cortes recién se anotan desde
        // este cambio: si el login tiene uno, el token es anterior.
        return long.TryParse(sesion.FindFirstValue(ClaimEmitido), out var emitidoMs)
            && emitidoMs >= corte.ToUnixTimeMilliseconds();
    }
}
