using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Logistica.Auth;

public static class CurrentUserExtensions
{
    public static Guid UsuarioId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public static string Rol(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role)!;

    public static int? ClienteId(this ClaimsPrincipal user)
    {
        var valor = user.FindFirstValue("cliente_id");
        return valor is null ? null : int.Parse(valor);
    }
}
