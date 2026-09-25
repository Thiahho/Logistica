using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Logistica.Auth;

public static class CurrentUserExtensions
{
    public static Guid UsuarioId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public static string Rol(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role)!;

    /// <summary>"dueno" | "usuario" para un login del portal; null para el personal interno.</summary>
    public static string? ClienteRol(this ClaimsPrincipal user) =>
        user.FindFirstValue("cliente_rol");

    public static bool EsClienteDueno(this ClaimsPrincipal user) =>
        user.ClienteRol() == Entidades.RolesCliente.Dueno;

    /// <summary>Si la API le manda a esta sesión el precio de un envío. El personal interno siempre;
    /// un login del portal, solo el dueño y solo con Portal:MostrarPrecios (acta changelog 4.30:
    /// suscriptores). Facturas, saldo y pagos no pasan por acá: esos se ven igual.</summary>
    public static bool VePreciosDeEnvio(this ClaimsPrincipal user, bool mostrarPrecios) =>
        user.ClienteId() is null || (mostrarPrecios && user.EsClienteDueno());

    public static int? ClienteId(this ClaimsPrincipal user)
    {
        var valor = user.FindFirstValue("cliente_id");
        return valor is null ? null : int.Parse(valor);
    }
}
