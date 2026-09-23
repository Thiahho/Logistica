namespace Logistica.Entidades;

/// <summary>
/// Tabla puente: RF-14, N retiros en la misma dirección colapsan en una sola parada.
/// Clave primaria compuesta (parada_id, pedido_id).
/// </summary>
public class ParadaPedido
{
    public long ParadaId { get; set; }
    public RutaParada Parada { get; set; } = null!;

    public long PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
}
