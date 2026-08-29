namespace Logistica.Entidades;

/// <summary>
/// Registro sin maquinaria (P4 / RF-31). Se acumulan desde el día uno;
/// nadie los procesa automáticamente hasta los 6 meses de historial (acta §9.2).
/// </summary>
public class EventoCliente
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public int TipoId { get; set; }
    public TipoEventoCliente Tipo { get; set; } = null!;

    public long? PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    /// <summary>días de atraso, monto, etc. según el tipo</summary>
    public decimal? ValorNum { get; set; }
    public string? Nota { get; set; }
    public DateTimeOffset OcurridoEn { get; set; }

    public Guid? RegistradoPorId { get; set; }
    public Usuario? RegistradoPor { get; set; }
}
