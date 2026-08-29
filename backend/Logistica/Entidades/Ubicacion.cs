namespace Logistica.Entidades;

public class Ubicacion
{
    public long Id { get; set; }
    public string CalleNumero { get; set; } = null!;

    public int? LocalidadId { get; set; }
    public Localidad? Localidad { get; set; }

    public string? Referencia { get; set; }
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    /// <summary>alta | media | baja | fallida</summary>
    public string? GeoConfianza { get; set; }
    public string? GeoProveedor { get; set; }
    public DateTimeOffset? GeoFecha { get; set; }
    public bool Verificada { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
}
