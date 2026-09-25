using System.Security.Claims;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;

namespace Logistica.Servicios;

/// <summary>Acciones que quedan en clientes_usuarios_actividad. Strings estables: el frontend las
/// traduce a texto (lib/dominio/tipos.ts, etiquetaAccionPortal).</summary>
public static class AccionesPortal
{
    public const string SesionIniciada = "sesion.iniciada";
    public const string PedidoCargado = "pedido.cargado";
    public const string ContactoCreado = "contacto.creado";
    public const string ContactoEditado = "contacto.editado";
    public const string ContactoEliminado = "contacto.eliminado";
    public const string UsuarioCreado = "usuario.creado";
    public const string UsuarioActivado = "usuario.activado";
    public const string UsuarioDesactivado = "usuario.desactivado";
    public const string UsuarioContrasena = "usuario.contrasena";
    public const string PedidoCancelado = "pedido.cancelado";
    public const string PedidoEditado = "pedido.editado";
    public const string PagoInformado = "pago.informado";
    public const string ViajeCargado = "viaje.cargado";
    public const string ViajeCancelado = "viaje.cancelado";
}

/// <summary>
/// Registra lo que hace un login del portal. Solo agrega la fila al contexto: se guarda en el
/// mismo SaveChanges de la acción, así que la acción y su registro quedan juntos o no queda
/// ninguno.
/// </summary>
public static class ActividadPortal
{
    public static void Registrar(
        LogisticaDbContext db, int clienteId, Guid clienteUsuarioId,
        string accion, string? entidadTipo = null, string? entidadId = null, string? detalle = null)
    {
        db.ClientesUsuariosActividad.Add(new ClienteUsuarioActividad
        {
            ClienteId = clienteId,
            ClienteUsuarioId = clienteUsuarioId,
            Accion = accion,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            Detalle = detalle,
            OcurridoEn = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Atajo para una acción hecha por el login de la sesión actual. No hace nada si la
    /// sesión no es de un cliente (no debería pasar: se llama solo desde MiCuentaController).</summary>
    public static void Registrar(
        LogisticaDbContext db, ClaimsPrincipal user,
        string accion, string? entidadTipo = null, string? entidadId = null, string? detalle = null)
    {
        if (user.ClienteId() is not { } clienteId) return;
        Registrar(db, clienteId, user.UsuarioId(), accion, entidadTipo, entidadId, detalle);
    }
}
