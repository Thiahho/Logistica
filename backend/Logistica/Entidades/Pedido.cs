namespace Logistica.Entidades;

public class Pedido
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public string? ReferenciaCliente { get; set; }

    /// <summary>entrega | retorno | reintento</summary>
    public string Tipo { get; set; } = "entrega";
    public long? PedidoOrigenId { get; set; }
    public Pedido? PedidoOrigen { get; set; }

    public long OrigenUbicacionId { get; set; }
    public Ubicacion OrigenUbicacion { get; set; } = null!;

    public long DestinoUbicacionId { get; set; }
    public Ubicacion DestinoUbicacion { get; set; } = null!;

    public string DestinatarioNombre { get; set; } = null!;
    public string DestinatarioTelefono { get; set; } = null!;
    public int Bultos { get; set; } = 1;
    public decimal? PesoKg { get; set; }
    public decimal? ValorDeclarado { get; set; }
    public DateOnly FechaEntrega { get; set; }
    public bool Urgente { get; set; }

    // Snapshot de precio (P1 / RF-02). Nunca se recalcula tras confirmar; trg_congelar_pedido lo impide.
    // Nullable desde acta changelog 3.11: el precio depende del tipo de vehículo (camioneta/moto),
    // que recién se conoce cuando la ruta que lo lleva cierra su planificación — hasta entonces el
    // pedido queda en Borrador, sin ninguno de estos campos. `Peajes` es la excepción: es un dato
    // que el cliente ya conoce al cargar el pedido, no depende del vehículo, se guarda de entrada.
    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }
    public decimal? PrecioBase { get; set; }
    public decimal? RecargoUrgencia { get; set; }
    public decimal? DescuentoRuta { get; set; }
    public decimal Peajes { get; set; }
    public decimal? Total { get; set; }
    public DateTimeOffset? PrecioCongeladoEn { get; set; }

    public EstadoPedido Estado { get; set; } = EstadoPedido.Borrador;

    /// <summary>interno | importado | portal | api</summary>
    public string OrigenCarga { get; set; } = "interno";
    public string? Observaciones { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
