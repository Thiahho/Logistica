using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Logistica.Tests.BaseDeDatos;

/// <summary>Los controllers se prueban instanciados a mano contra la base real, sin levantar el host: lo
/// único que necesitan del pipeline es el usuario autenticado (User.UsuarioId() lee el claim `sub`).</summary>
internal static class ControladoresDePrueba
{
    public static T ComoUsuario<T>(this T controlador, Guid usuarioId) where T : ControllerBase
    {
        var identidad = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString())], "prueba");
        controlador.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidad) },
        };
        return controlador;
    }
}
