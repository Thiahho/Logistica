namespace Logistica.Entidades;

/// <summary>
/// Login de consulta de una empresa cliente (rol implícito "cliente" — ver Roles.Cliente).
/// Tabla separada de `usuarios` (personal interno) a propósito: los dos tipos de cuenta no
/// comparten gestión ni ciclo de vida, solo el mecanismo de emisión de JWT (TokenService).
/// </summary>
public class ClienteUsuario
{
    public Guid Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public string Nombre { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    /// <summary>dueno | usuario (ver RolesCliente). El dueño ve la cuenta corriente y administra a
    /// los usuarios (empleados) de su empresa; el usuario solo carga y sigue envíos.</summary>
    public string Rol { get; set; } = RolesCliente.Dueno;

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
