namespace Logistica.Entidades;

/// <summary>
/// E1 (Anexo I §5, B1). Libro de solo inserción: una factura emitida no se edita (protegida por
/// trg_facturas_inmutable) — las correcciones son FacturaItem nuevos que entran en la siguiente.
/// El saldo (pagado/pendiente) no vive acá: se deriva por FIFO en v_facturas_saldo
/// (docs/schema_v3.sql), nunca se persiste como estado mutable.
/// </summary>
public class Factura
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    /// <summary>quincenal | moto — snapshot del ciclo del cliente al momento de emitir (acta
    /// §10.2-A / D2). El cliente puede cambiar de ciclo después sin reescribir el historial.</summary>
    public string Ciclo { get; set; } = null!;

    public DateOnly PeriodoDesde { get; set; }
    public DateOnly PeriodoHasta { get; set; }

    /// <summary>Cuándo corrió el cierre — puede ser posterior al período si el cierre se corrió
    /// tarde. FechaVencimiento se cuenta desde PeriodoHasta, nunca desde acá.</summary>
    public DateOnly FechaEmision { get; set; }
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>Sin restricción de signo: un período que neteó a crédito (notas de crédito >
    /// pedidos) puede dar un total negativo. v_facturas_saldo lo maneja bien.</summary>
    public decimal Total { get; set; }

    public Guid? EmitidaPor { get; set; }
    public Usuario? EmitidaPorUsuario { get; set; }
    public DateTimeOffset CreadaEn { get; set; }

    public List<FacturaItem> Items { get; set; } = [];
}
