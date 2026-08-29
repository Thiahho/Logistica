namespace Logistica.Entidades;

public class Ruta
{
    public long Id { get; set; }
    public DateOnly Fecha { get; set; }

    public Guid? RepartidorId { get; set; }
    public Usuario? Repartidor { get; set; }

    /// <summary>texto libre: un vehículo</summary>
    public string? Vehiculo { get; set; }

    /// <summary>Presupuesto de paradas de la jornada (P7 / RF-16)</summary>
    public int CapacidadParadas { get; set; } = 24;

    /// <summary>planificada | en_curso | cerrada</summary>
    public string Estado { get; set; } = "planificada";

    public int? KmInicial { get; set; }
    public int? KmFinal { get; set; }
    public decimal? CombustibleMonto { get; set; }
    public decimal? PeajesMonto { get; set; }
    public decimal? OtrosCostos { get; set; }
    public decimal? PagoRepartidor { get; set; }
    public string? NotasCierre { get; set; }
    public DateTimeOffset? CerradaEn { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
}
