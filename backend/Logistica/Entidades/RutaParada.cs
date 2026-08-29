namespace Logistica.Entidades;

public class RutaParada
{
    public long Id { get; set; }

    public long RutaId { get; set; }
    public Ruta Ruta { get; set; } = null!;

    public long UbicacionId { get; set; }
    public Ubicacion Ubicacion { get; set; } = null!;

    /// <summary>retiro | entrega | deposito (deposito: previsión 10.2, no se usa hoy)</summary>
    public string Tipo { get; set; } = null!;

    public int Orden { get; set; }

    /// <summary>urgentes: no se reordenan</summary>
    public bool Anclada { get; set; }

    /// <summary>pendiente | completada | fallida</summary>
    public string Estado { get; set; } = "pendiente";

    public DateTimeOffset? LlegadaEn { get; set; }
    public DateTimeOffset? SalidaEn { get; set; }
}
