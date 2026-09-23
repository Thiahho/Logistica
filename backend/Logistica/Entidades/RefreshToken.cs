namespace Logistica.Entidades;

/// <summary>
/// Sesión persistente del acta (RNF: 'nunca pedir contraseña en la calle').
/// El token en sí nunca se persiste en texto plano, solo su hash (TokenHash).
/// Rotado en cada uso: al canjearse, queda revocado y encadenado a ReemplazadoPorId.
/// Exactamente uno de UsuarioId/ClienteUsuarioId está seteado (ck_refresh_tokens_actor_unico):
/// un login puede ser de personal interno o de un cliente, nunca ambos ni ninguno.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public Guid? ClienteUsuarioId { get; set; }
    public ClienteUsuario? ClienteUsuario { get; set; }

    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public Guid? ReemplazadoPorId { get; set; }
    public string? CreadoPorIp { get; set; }

    public bool EstaActivo => RevocadoEn is null && ExpiraEn > DateTimeOffset.UtcNow;
}
