namespace Logistica.Entidades;

public class Zona
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public bool Activa { get; set; } = true;

    /// <summary>Rango de distancia de la zona, en km. Sin valores de fábrica: son datos
    /// comerciales que administración carga desde /tarifas (mismo criterio que las tarifas
    /// mismas — un número inventado es peor que un casillero vacío, acta_sistema_v3.md §13).
    /// KmHasta null = sin límite superior (la zona más lejana).</summary>
    public int? KmDesde { get; set; }
    public int? KmHasta { get; set; }
}
