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

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
