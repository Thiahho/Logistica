namespace Logistica.Entidades;

/// <summary>
/// De solo inserción: trg_log_inmutable rechaza cualquier UPDATE/DELETE (P2 / RF-28).
/// Las filas las escribe el trigger trg_log_estado_pedido, no la aplicación.
/// </summary>
public class PedidoEvento
{
    public long Id { get; set; }

    public long PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;

    public EstadoPedido? EstadoAnterior { get; set; }
    public EstadoPedido EstadoNuevo { get; set; }
    public string? Motivo { get; set; }

    /// <summary>sistema | usuario</summary>
    public string ActorTipo { get; set; } = null!;
    public Guid? ActorUsuarioId { get; set; }
    public Usuario? ActorUsuario { get; set; }

    /// <summary>solo cuando actor_tipo='sistema'</summary>
    public string? ActorTexto { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
}
