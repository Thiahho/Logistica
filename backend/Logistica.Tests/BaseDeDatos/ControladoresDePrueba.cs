using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Sesión de un login del portal, con los claims que emite TokenService. `db` queda
    /// disponible en RequestServices: es de donde lo toman los filtros de acción.</summary>
    public static T ComoPortal<T>(this T controlador, ClienteUsuario login, LogisticaDbContext db) where T : ControllerBase
    {
        var identidad = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, login.Id.ToString()),
            new Claim("nombre", login.Nombre),
            new Claim(ClaimTypes.Role, Roles.Cliente),
            new Claim("cliente_id", login.ClienteId.ToString()),
            new Claim("cliente_rol", login.Rol),
        ], "prueba");
        controlador.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identidad),
                RequestServices = new ServiceCollection().AddSingleton(db).BuildServiceProvider(),
            },
        };
        return controlador;
    }

    /// <summary>
    /// Corre `accion` como la correría el pipeline en lo que a filtros de acción del controller se
    /// refiere: arma el descriptor desde los atributos reales del método (verbo HTTP incluido), pasa
    /// por los filtros declarados en la clase y, si ninguno corta, ejecuta `ejecutar`. Devuelve la
    /// respuesta que vería el cliente.
    /// </summary>
    public static async Task<IActionResult> PorLosFiltrosAsync<T>(
        this T controlador, string accion, Func<Task<IActionResult>> ejecutar) where T : ControllerBase
    {
        var metodo = typeof(T).GetMethod(accion)!;
        var http = controlador.HttpContext;
        http.Request.Method = metodo.GetCustomAttributes<HttpMethodAttribute>().Single().HttpMethods.Single();
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = metodo,
            ControllerTypeInfo = typeof(T).GetTypeInfo(),
            EndpointMetadata = [.. typeof(T).GetCustomAttributes(inherit: true), .. metodo.GetCustomAttributes(inherit: true)],
        };
        var contexto = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), descriptor), [], new Dictionary<string, object?>(), controlador);

        IActionResult? respuesta = null;
        ActionExecutionDelegate siguiente = async () =>
        {
            respuesta = await ejecutar();
            return new ActionExecutedContext(contexto, [], controlador) { Result = respuesta };
        };
        foreach (var filtro in typeof(T).GetCustomAttributes(inherit: true).OfType<IAsyncActionFilter>().Reverse())
        {
            var interior = siguiente;
            siguiente = async () =>
            {
                await filtro.OnActionExecutionAsync(contexto, interior);
                return new ActionExecutedContext(contexto, [], controlador) { Result = contexto.Result ?? respuesta };
            };
        }
        await siguiente();
        return contexto.Result ?? respuesta!;
    }
}
