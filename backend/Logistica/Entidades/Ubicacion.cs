namespace Logistica.Entidades;

public class Ubicacion
{
    public long Id { get; set; }
    public string CalleNumero { get; set; } = null!;

    public int? LocalidadId { get; set; }
    public Localidad? Localidad { get; set; }

    public string? Referencia { get; set; }

    /// <summary>No-null = esta ubicación es un depósito seleccionable al armar una ruta, con este
    /// nombre corto (acta changelog 3.8). Campo propio, no se reusa `Referencia`: esa columna ya
    /// significa otra cosa (la nota de la parada que ve el repartidor, v_paradas_repartidor) y
    /// mezclar los dos sentidos rompía esa vista para cualquier ubicación que fuera depósito.</summary>
    public string? NombreDeposito { get; set; }

    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    /// <summary>alta | media | baja | fallida</summary>
    public string? GeoConfianza { get; set; }
    public string? GeoProveedor { get; set; }
    public DateTimeOffset? GeoFecha { get; set; }
    public bool Verificada { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
}
