namespace Logistica.Entidades;

public class Vehiculo
{
    public long Id { get; set; }

    /// <summary>Normalizada a mayúsculas sin espacios por el controller; única.</summary>
    public string Patente { get; set; } = null!;

    /// <summary>Alias operativo, ej. "Utilitario 1".</summary>
    public string? Descripcion { get; set; }

    public string? Marca { get; set; }
    public string? Modelo { get; set; }

    /// <summary>camioneta | moto (acta changelog 3.11). Determina qué tarifa aplica a los
    /// pedidos de una ruta cuando se cierra su planificación — ver OrigenRutaService/PrecioService.</summary>
    public string Tipo { get; set; } = "camioneta";
    public int? Anio { get; set; }
    public int? KmActual { get; set; }
    public DateOnly? VenceVtv { get; set; }
    public DateOnly? VenceSeguro { get; set; }
    public decimal? CostoKm { get; set; }

    /// <summary>Presupuesto de paradas del vehículo (P7 / RF-16). Prellena Ruta.CapacidadParadas
    /// al elegirlo en el armado de ruta.</summary>
    public int CapacidadParadas { get; set; } = 24;

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
