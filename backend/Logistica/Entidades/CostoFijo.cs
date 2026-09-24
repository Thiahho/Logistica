namespace Logistica.Entidades;

/// <summary>
/// B7 (acta RF-43, changelog 4.21; diseño_e2_rangos_liquidacion.md §5.1). Un costo fijo de un mes
/// (alquiler, seguros, sueldos fijos, software…), cargado por administración. Categoría de texto libre:
/// la estructura objetivo de la Empresa se define después y los tramos las agrupan como quieran.
/// No es un libro inmutable: es un dato de gestión que se corrige si se cargó mal.
/// </summary>
public class CostoFijo
{
    public long Id { get; set; }

    /// <summary>Siempre el día 1 del mes (ck_costos_fijos_mes).</summary>
    public DateOnly Mes { get; set; }

    public string Categoria { get; set; } = null!;
    public string? Descripcion { get; set; }
    public decimal Monto { get; set; }

    public Guid CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
