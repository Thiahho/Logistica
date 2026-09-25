namespace Logistica.Entidades;

/// <summary>
/// "cliente" ya no es una fila de esta tabla (ver ClienteUsuario) pero se mantiene como constante:
/// la policy "Cliente" de Program.cs y el claim de rol del JWT que emite un login de
/// ClienteUsuario siguen usando este mismo string.
/// </summary>
public static class Roles
{
    public const string Administracion = "administracion";
    public const string Operacion = "operacion";
    public const string Repartidor = "repartidor";
    public const string Cliente = "cliente";

    /// <summary>Roles que sí son filas de `usuarios` (personal interno). No incluye Cliente.</summary>
    public static readonly string[] Todos = [Administracion, Operacion, Repartidor];
}

/// <summary>Rol dentro de una empresa cliente (clientes_usuarios.rol, claim "cliente_rol"). No es un
/// rol de autorización aparte: los dos siguen siendo Roles.Cliente; esto separa lo que el dueño
/// puede ver y hacer (policy "ClienteDueno") de lo que hace un empleado.</summary>
public static class RolesCliente
{
    public const string Dueno = "dueno";
    public const string Usuario = "usuario";

    public static readonly string[] Todos = [Dueno, Usuario];
}

/// <summary>Personal interno: administracion | operacion | repartidor. Los logins de cliente
/// viven en ClienteUsuario/clientes_usuarios, tabla separada a propósito.</summary>
public class Usuario
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    /// <summary>administracion | operacion | repartidor</summary>
    public string Rol { get; set; } = null!;

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
