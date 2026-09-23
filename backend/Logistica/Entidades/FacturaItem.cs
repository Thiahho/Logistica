namespace Logistica.Entidades;

/// <summary>
/// E1 (Anexo I §5, B1 + B16). Todo lo facturable nace acá en el momento exacto en que se vuelve
/// facturable (entrega, o cancelación de un Confirmado — Controllers/PedidosController.cs,
/// Controllers/MisParadasController.cs) — el cierre de ciclo no "descubre" pedidos escaneando
/// `pedidos`, solo barre los items con FacturaId null. El unique parcial
/// ux_factura_items_pedido (tipo='pedido') garantiza a nivel de base que un pedido se factura una
/// sola vez.
///
/// Tipo 'ajuste' (B16, sumado a E1): operación lo solicita al detectar una diferencia de bultos
/// al retiro físico (Estado='pendiente', Monto null); un admin lo aprueba fijando el Monto a mano
/// — mismo patrón que Pedido.PrecioManual (B9) — o lo rechaza. Nunca entra a una factura sin
/// estar aprobado (ck_factura_items_estado_factura).
/// </summary>
public class FacturaItem
{
    public long Id { get; set; }

    /// <summary>null = todavía sin facturar (recién creado) o ajuste sin aprobar todavía.</summary>
    public long? FacturaId { get; set; }
    public Factura? Factura { get; set; }

    /// <summary>null = ítem que no es un pedido (ajuste, nota de crédito).</summary>
    public long? PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    /// <summary>pedido | ajuste | nota_credito</summary>
    public string Tipo { get; set; } = null!;
    public string Descripcion { get; set; } = null!;

    /// <summary>null solo mientras un ajuste está 'pendiente' de que un admin le fije el monto.</summary>
    public decimal? Monto { get; set; }

    /// <summary>pendiente | aprobado | rechazado. tipo='pedido' nace siempre 'aprobado'.</summary>
    public string Estado { get; set; } = "aprobado";

    public Guid? CreadoPor { get; set; }
    public Usuario? CreadoPorUsuario { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Guid? ResueltoPor { get; set; }
    public Usuario? ResueltoPorUsuario { get; set; }
    public DateTimeOffset? ResueltoEn { get; set; }
}
