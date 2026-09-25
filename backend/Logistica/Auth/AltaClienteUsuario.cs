using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Auth;

/// <summary>
/// Alta de un login del portal. Compartida por el BackOffice (ClientesController, Administración)
/// y el dueño de la empresa (MiCuentaController): las dos puertas validan igual. La unicidad del
/// email la impone la base (índice + trigger entre tablas) y la traduce ManejadorExcepciones.
/// </summary>
public static class AltaClienteUsuario
{
    public static (ClienteUsuario? Usuario, string? Error) Crear(
        int clienteId, string nombre, string email, string password, string rol)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return (null, "El nombre es obligatorio.");
        if (!Validaciones.EmailValido(email)) return (null, "El email no es válido.");
        if (!RolesCliente.Todos.Contains(rol)) return (null, "Rol inválido.");
        if (PoliticaContrasena.Validar(password, email) is { } errorClave) return (null, errorClave);

        var usuario = new ClienteUsuario
        {
            Id = Guid.NewGuid(),
            ClienteId = clienteId,
            Nombre = nombre.Trim(),
            Email = email.Trim(),
            Rol = rol,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        usuario.PasswordHash = AuthService.HashearCliente(usuario, password);
        return (usuario, null);
    }
}
