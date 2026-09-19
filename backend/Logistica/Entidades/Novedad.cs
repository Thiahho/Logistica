namespace Logistica.Entidades;

/// <summary>
/// Lo que pasa en la calle o en el escritorio con una ruta en curso y no encaja en el resultado de
/// una parada: una avería, un bulto dañado, un teléfono equivocado, un pedido que operación cancela
/// con el repartidor ya en camino (acta changelog 4.8, RF-36/RF-37). Una sola tabla para las cinco
/// clases porque comparten ciclo (alguien informa → alguien ve → alguien responde) y porque una
/// tabla por clase cruzaría el techo de tablas cinco veces.
///
/// Lo que informó quien la creó es inmutable (trg_novedades_inmutable): mismo principio que la
/// declaración del retiro — el reporte de la calle no se reescribe. Solo se completan estado,
/// resolución y visto_en.
/// </summary>
public class Novedad
{
    public long Id { get; set; }

    public long RutaId { get; set; }
    public Ruta Ruta { get; set; } = null!;

    public long? ParadaId { get; set; }
    public RutaParada? Parada { get; set; }

    public long? PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    /// <summary>incidencia_ruta | problema_carga | cambio_propuesto (origen repartidor) ·
    /// cambio_operacion | cancelacion (origen operacion)</summary>
    public string Tipo { get; set; } = null!;

    /// <summary>repartidor | operacion</summary>
    public string Origen { get; set; } = null!;

    /// <summary>Lista cerrada por tipo, en configuración (PruebaEntrega:CategoriasIncidencia/Carga).</summary>
    public string? Categoria { get; set; }

    public string Descripcion { get; set; } = null!;

    /// <summary>Solo cambio_propuesto: qué campo del pedido se quiere cambiar, de qué valor a cuál.
    /// El valor anterior lo pone el servidor, nunca el cliente.</summary>
    public string? PropuestaCampo { get; set; }
    public string? PropuestaValorAnterior { get; set; }
    public string? PropuestaValorNuevo { get; set; }

    public string? FotoPath { get; set; }

    /// <summary>RNF-02: idempotencia de lo que crea el repartidor desde el dispositivo.</summary>
    public string? DeviceUuid { get; set; }

    /// <summary>abierta | resuelta | rechazada</summary>
    public string Estado { get; set; } = "abierta";

    public Guid CreadaPor { get; set; }
    public Usuario CreadaPorUsuario { get; set; } = null!;
    public DateTimeOffset CreadaEn { get; set; }

    public Guid? ResueltaPor { get; set; }
    public Usuario? ResueltaPorUsuario { get; set; }
    public DateTimeOffset? ResueltaEn { get; set; }
    public string? Resolucion { get; set; }

    /// <summary>Cuándo lo vio el repartidor. En lo que crea operación es el acuse de recibo; en lo que
    /// crea el repartidor se vuelve a null al resolverse, para que la respuesta le llegue.</summary>
    public DateTimeOffset? VistoEn { get; set; }
}
