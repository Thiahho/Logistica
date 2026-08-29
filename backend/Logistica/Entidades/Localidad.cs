namespace Logistica.Entidades;

public class Localidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string? Partido { get; set; }
    public string? Cp { get; set; }

    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }
}
