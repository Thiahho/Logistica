namespace Logistica.Entidades;

/// <summary>
/// Historial de lo que hace cada login del portal (cargar un envío, editar un contacto, iniciar
/// sesión, administrar usuarios), para que el dueño de la empresa cliente vea cómo trabaja su
/// equipo. Solo inserción (trigger trg_actividad_inmutable), igual que pedido_eventos.
/// </summary>
public class ClienteUsuarioActividad
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public Guid ClienteUsuarioId { get; set; }
    public ClienteUsuario ClienteUsuario { get; set; } = null!;

    /// <summary>Ver AccionesPortal.</summary>
    public string Accion { get; set; } = null!;
    public string? EntidadTipo { get; set; }
    public string? EntidadId { get; set; }
    public string? Detalle { get; set; }
    public DateTimeOffset OcurridoEn { get; set; }
}
