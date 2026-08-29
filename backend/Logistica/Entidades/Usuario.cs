namespace Logistica.Entidades;

public static class Roles
{
    public const string Administracion = "administracion";
    public const string Operacion = "operacion";
    public const string Repartidor = "repartidor";
    public const string Cliente = "cliente";

    public static readonly string[] Todos = [Administracion, Operacion, Repartidor, Cliente];
}

public class Usuario
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    /// <summary>administracion | operacion | repartidor | cliente</summary>
    public string Rol { get; set; } = null!;

    /// <summary>solo rol 'cliente'</summary>
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
