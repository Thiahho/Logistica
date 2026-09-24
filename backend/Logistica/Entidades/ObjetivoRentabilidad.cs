namespace Logistica.Entidades;

/// <summary>
/// B7 (acta RF-43, changelog 4.21): un tramo de la estructura económica objetivo (la infografía habla
/// de 60-65% / 10-15% / 20-30%; qué es cada tramo lo define la Empresa después). Nombre libre, rango de
/// % sobre los ingresos del mes y qué fuentes de costo (o el margen) suma. Sin filas de fábrica.
/// </summary>
public class ObjetivoRentabilidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public decimal PctMin { get; set; }
    public decimal PctMax { get; set; }

    /// <summary>pago_repartidor | combustible | peajes | otros_costos | fijos | fijos:&lt;categoría&gt; | margen
    /// (Dominio/Rentabilidad.Fuentes).</summary>
    public string[] Fuentes { get; set; } = [];

    public int Orden { get; set; }
}
