namespace Logistica.Entidades;

/// <summary>
/// Envío con varias paradas cargado de una vez (portal o BackOffice) para una sola empresa cliente.
/// Cada parada es un Pedido normal (con ViajeId); el viaje los agrupa y propone su propia ruta
/// (Ruta.ViajeId), ordenada por el sistema y revisada por Operación, que le asigna vehículo y
/// repartidor y la cierra con el flujo de siempre (acta §2: nunca ruteo sin revisión humana).
/// El avance (planificado, en curso, cerrado) sale del estado de su ruta: no se guarda dos veces.
/// </summary>
public class Viaje
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public DateOnly FechaEntrega { get; set; }

    public long OrigenUbicacionId { get; set; }
    public Ubicacion OrigenUbicacion { get; set; } = null!;

    /// <summary>camioneta | moto: el que eligió el cliente en el portal (cotiza con ese). null si lo
    /// cargó el BackOffice: el vehículo lo decide la ruta al cerrar la planificación.</summary>
    public string? TipoVehiculo { get; set; }

    /// <summary>activo | cancelado (ver EstadosViaje).</summary>
    public string Estado { get; set; } = EstadosViaje.Activo;

    /// <summary>Km del recorrido propuesto al cargarlo (por calle si el proveedor respondió; si no, en
    /// línea recta).</summary>
    public decimal? KmEstimados { get; set; }

    /// <summary>La ruta propuesta. null si todavía no tiene (todas las direcciones dudosas) o si
    /// Operación la eliminó para rearmarla.</summary>
    public long? RutaId { get; set; }
    public Ruta? Ruta { get; set; }

    public Guid? CreadoPorClienteUsuarioId { get; set; }
    public ClienteUsuario? CreadoPorClienteUsuario { get; set; }
    public Guid? CreadoPorUsuarioId { get; set; }
    public Usuario? CreadoPorUsuario { get; set; }

    public string? Observaciones { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public static class EstadosViaje
{
    public const string Activo = "activo";
    public const string Cancelado = "cancelado";
}
