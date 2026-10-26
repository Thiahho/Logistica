namespace Logistica.Entidades;

/// <summary>
/// Ubicación a la que un cliente llegó desde el portal resolviendo la dirección (M-N1). `ubicaciones` es
/// un catálogo compartido: el portal solo acepta un id que el cliente resolvió, o que ya es destino de un
/// envío o de un contacto suyo (AccesoUbicacionesService). Sin backfill: esos dos casos cubren lo previo.
/// </summary>
public class ClienteUbicacion
{
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public long UbicacionId { get; set; }
    public Ubicacion Ubicacion { get; set; } = null!;

    public DateTimeOffset CreadoEn { get; set; }
}
