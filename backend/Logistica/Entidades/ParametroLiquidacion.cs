namespace Logistica.Entidades;

/// <summary>
/// B4 (acta RF-41, changelog 4.21; diseño_e2_rangos_liquidacion.md §2.1). Valores con que se calcula
/// el pago al repartidor, uno por tipo de vehículo y con vigencia, igual que las tarifas. Es
/// configuración de la Empresa (Anexo I §6): no hay filas de fábrica — sin fila vigente, el pago de
/// una ruta se sigue tipeando a mano (acta §13, un casillero vacío antes que un número inventado).
/// </summary>
public class ParametroLiquidacion
{
    public long Id { get; set; }

    /// <summary>camioneta | moto</summary>
    public string TipoVehiculo { get; set; } = null!;

    public decimal PagoPorEntrega { get; set; }
    public decimal BonoRuta { get; set; }

    /// <summary>Definición C del Anexo I: % mínimo de entregas exitosas (sobre entregas + fallidas
    /// imputables) para cobrar el bono.</summary>
    public decimal PctMinimoExitosas { get; set; }

    /// <summary>Definición C: motivos de fallo (subconjunto de PruebaEntrega:MotivosFallo) que
    /// cuentan contra el repartidor. Los demás no suman ni restan.</summary>
    public string[] MotivosImputables { get; set; } = [];

    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteHasta { get; set; }

    public Guid? CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
