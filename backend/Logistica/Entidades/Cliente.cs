namespace Logistica.Entidades;

public class Cliente
{
    public int Id { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string? Cuit { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }

    // Indicadores internos (RF-32/RF-33). Nunca se exponen al rol 'cliente'.
    public string ColorPago { get; set; } = "rojo";
    public string ColorTrato { get; set; } = "amarillo";
    public string ColorOper { get; set; } = "amarillo";

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>quincenal | mensual (acta §10.2-A / D2). Snapshot copiado a cada Factura al
    /// emitirla — cambiar esto no reescribe el historial.</summary>
    public string CicloFacturacion { get; set; } = "mensual";

    // Plan de cuotas (§10.2-L4): sin tabla propia — el gate de corte (PedidosController.Crear)
    // ignora la deuda vencida mientras CorteSuspendidoHasta >= hoy. Admin extiende la fecha cada
    // vez que entra una cuota; si una no entra, la fecha pasa sola y el corte vuelve, sin job.
    // Los cuatro campos van juntos o ninguno (ck_clientes_corte_suspendido).
    public DateOnly? CorteSuspendidoHasta { get; set; }
    public string? CorteSuspendidoMotivo { get; set; }
    public Guid? CorteSuspendidoPor { get; set; }
    public Usuario? CorteSuspendidoPorUsuario { get; set; }
    public DateTimeOffset? CorteSuspendidoEn { get; set; }

    // Rango (B3, acta RF-42, changelog 4.21). El efectivo no se guarda: es el calculado movido por el
    // ajuste si sigue vigente (Dominio/RangosCliente.Efectivo), así un ajuste que vence deja de contar
    // solo, sin proceso que lo limpie.

    /// <summary>El que dio el último recálculo trimestral (D4).</summary>
    public string RangoCalculado { get; set; } = "sin_rango";
    public DateTimeOffset? RangoCalculadoEn { get; set; }

    /// <summary>Definición F: −1, 0 o +1. Distinto de 0 exige motivo y vencimiento (ck_clientes_rango_ajuste).</summary>
    public short RangoAjuste { get; set; }
    public string? RangoAjusteMotivo { get; set; }
    public DateOnly? RangoAjusteVence { get; set; }
    public Guid? RangoAjustePor { get; set; }
    public DateTimeOffset? RangoAjusteEn { get; set; }
}
