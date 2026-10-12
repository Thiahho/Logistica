using Logistica.Datos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Auth;

/// <summary>
/// Una empresa dada de baja (Cliente.Activo = false) tiene el servicio cortado hasta que pague. Sus
/// logins siguen entrando al portal y leyendo (facturas, deuda, historial), que es lo que necesitan
/// para pagar; lo que este filtro les corta es todo lo que pide o cambia servicio.
///
/// Va sobre el controller del portal y corta por defecto: toda acción que no sea de lectura (GET)
/// responde 409 salvo que lleve [PermitidoConEmpresaInactiva]. Así una acción nueva queda cortada
/// hasta que alguien decida lo contrario, en vez de quedar abierta por olvido.
///
/// Mira la base en cada request y no un claim: la baja tiene que valer ya, no cuando venza el
/// access token.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CortaServicioAEmpresaInactivaAttribute : Attribute, IAsyncActionFilter
{
    public const string Mensaje =
        "Tu cuenta está inactiva: el servicio está suspendido hasta regularizar el pago. "
        + "Podés seguir viendo tus facturas y tu deuda, e informar un pago.";

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        var http = contexto.HttpContext;
        var cortable = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
            && !contexto.ActionDescriptor.EndpointMetadata.OfType<PermitidoConEmpresaInactivaAttribute>().Any();

        // Sin cliente_id en la sesión no hay empresa que mirar: la acción responde su propio Forbid.
        if (cortable && http.User.ClienteId() is { } clienteId)
        {
            var db = http.RequestServices.GetRequiredService<LogisticaDbContext>();
            var activa = await db.Clientes.AsNoTracking()
                .Where(c => c.Id == clienteId)
                .Select(c => (bool?)c.Activo)
                .SingleOrDefaultAsync(http.RequestAborted);
            if (activa != true)
            {
                contexto.Result = new ConflictObjectResult(Mensaje);
                return;
            }
        }

        await siguiente();
    }
}

/// <summary>Excepción a CortaServicioAEmpresaInactiva: la acción no pide ni cambia servicio, así que
/// una empresa dada de baja la sigue usando. Cada uso dice por qué.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PermitidoConEmpresaInactivaAttribute : Attribute;
