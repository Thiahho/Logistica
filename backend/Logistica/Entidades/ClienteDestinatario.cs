namespace Logistica.Entidades;

/// <summary>
/// "Mis clientes" del portal (diseño §4a, changelog acta 4.10) — libreta de destinatarios que el
/// propio cliente registra a mano, para prellenar el alta de un envío nuevo. Reversión explícita de
/// la decisión 3.5 ("no se crea una entidad Destinatario propia"): a diferencia de
/// `destinatarios-frecuentes` (derivado del historial de `pedidos`, sin tabla propia), esto es un
/// registro explícito, a pedido del cliente. Nada la referencia por FK desde `pedidos` — el alta
/// copia los valores, no guarda este id — así que es ABM libre, sin trigger de inmutabilidad.
/// </summary>
public class ClienteDestinatario
{
    public Guid Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public string Nombre { get; set; } = null!;
    public string Telefono { get; set; } = null!;

    public long DestinoUbicacionId { get; set; }
    public Ubicacion DestinoUbicacion { get; set; } = null!;

    public string? Observaciones { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
