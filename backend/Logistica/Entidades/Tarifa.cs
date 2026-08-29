namespace Logistica.Entidades;

public class Tarifa
{
    public long Id { get; set; }

    /// <summary>null = lista general</summary>
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public int ZonaId { get; set; }
    public Zona Zona { get; set; } = null!;

    public decimal Precio { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteHasta { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
}
