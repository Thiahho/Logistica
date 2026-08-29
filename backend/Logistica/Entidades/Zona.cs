namespace Logistica.Entidades;

public class Zona
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public bool Activa { get; set; } = true;
}
