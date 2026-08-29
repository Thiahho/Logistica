namespace Logistica.Entidades;

/// <summary>
/// Sesión persistente del acta (RNF: 'nunca pedir contraseña en la calle').
/// El token en sí nunca se persiste en texto plano, solo su hash (TokenHash).
/// Rotado en cada uso: al canjearse, queda revocado y encadenado a ReemplazadoPorId.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public Guid? ReemplazadoPorId { get; set; }
    public string? CreadoPorIp { get; set; }

    public bool EstaActivo => RevocadoEn is null && ExpiraEn > DateTimeOffset.UtcNow;
}
