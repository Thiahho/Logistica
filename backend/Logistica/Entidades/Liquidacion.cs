namespace Logistica.Entidades;

/// <summary>
/// B4 (acta RF-41, changelog 4.21; diseño_e2_rangos_liquidacion.md §2.3). Comprobante de lo que se le
/// paga a un repartidor por un período: las rutas cerradas del período que todavía no estaban en
/// otra liquidación, vinculadas por rutas.liquidacion_id. Sin tabla de ítems: el detalle de cada ruta
/// ya vive en sus columnas liq_*.
///
/// Solo inserción (trg_liquidaciones_inmutable): una liquidación emitida no se edita ni se borra, y
/// una ruta liquidada no cambia de liquidación ni de pago (trg_rutas_liquidada). Un error se corrige
/// en la liquidación siguiente.
/// </summary>
public class Liquidacion
{
    public long Id { get; set; }

    public Guid RepartidorId { get; set; }
    public Usuario Repartidor { get; set; } = null!;

    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    public int CantidadRutas { get; set; }
    public decimal Total { get; set; }
    public string? Nota { get; set; }

    public Guid EmitidaPor { get; set; }
    public DateTimeOffset EmitidaEn { get; set; }
}
