namespace Logistica.Entidades;

public class Ruta
{
    public long Id { get; set; }
    public DateOnly Fecha { get; set; }

    public Guid? RepartidorId { get; set; }
    public Usuario? Repartidor { get; set; }

    public long? VehiculoId { get; set; }
    public Vehiculo? Vehiculo { get; set; }

    /// <summary>Punto de partida de la jornada. null = depósito de la empresa (caso mayoritario y
    /// comportamiento histórico); con valor, es donde quedó la camioneta el día anterior. Se
    /// resuelve siempre por OrigenRutaService, nunca leyendo esta propiedad suelta.</summary>
    public long? OrigenUbicacionId { get; set; }
    public Ubicacion? Origen { get; set; }

    /// <summary>Presupuesto de paradas de la jornada (P7 / RF-16)</summary>
    public int CapacidadParadas { get; set; } = 24;

    /// <summary>planificada | en_curso | cerrada</summary>
    public string Estado { get; set; } = "planificada";

    // ── Cierre económico de administración (RF-26/27). Los escribe SOLO
    // RutasController.Cerrar; el repartidor no los toca (RNF-08, regla 4).
    public int? KmInicial { get; set; }
    public int? KmFinal { get; set; }
    public decimal? CombustibleMonto { get; set; }
    public decimal? PeajesMonto { get; set; }
    public decimal? OtrosCostos { get; set; }
    public decimal? PagoRepartidor { get; set; }
    public string? NotasCierre { get; set; }
    public DateTimeOffset? CerradaEn { get; set; }

    /// <summary>Quién cerró la ruta, que es el mismo acto que aprobar la declaración del
    /// repartidor (acta changelog 4.7). `rutas` no tiene log de eventos propio y crearlo cruzaría
    /// el techo de tablas (construccion_v1.md §4.3): esta columna es la única huella del actor.</summary>
    public Guid? CerradaPor { get; set; }

    public DateTimeOffset CreadaEn { get; set; }

    // ── Retiro con conteo firmado (RF-35, acta §7). Lo escribe SOLO
    // MiJornadaController.Retiro, y trg_congelar_declaracion_repartidor lo vuelve inmutable
    // una vez sellado RetiroConfirmadoEn.

    /// <summary>RNF-03: hora de captura del dispositivo, no now() del servidor. Null = la ruta
    /// todavía no salió: MisParadasController rechaza llegada y cierre de parada (acta §7).</summary>
    public DateTimeOffset? RetiroConfirmadoEn { get; set; }

    /// <summary>Bultos que la ruta tenía cargados al momento de firmar, congelados por el
    /// servidor sumando Pedido.Bultos. Nunca llega del cliente: es la lista contra la que se
    /// contó, y tiene que sobrevivir a cualquier cambio posterior en los pedidos.</summary>
    public int? RetiroBultosEsperados { get; set; }

    /// <summary>Lo que el repartidor contó de verdad. Distinto del esperado no bloquea la salida
    /// —costaría la jornada entera por un bulto— pero exige RetiroObservaciones.</summary>
    public int? RetiroBultosContados { get; set; }

    public string? RetiroObservaciones { get; set; }

    /// <summary>Ruta relativa de la firma, mismo criterio que PruebaEntrega.FotoPath: el archivo
    /// vive fuera de wwwroot y no se sirve como estático.</summary>
    public string? RetiroFirmaPath { get; set; }

    /// <summary>Km del odómetro declarados por el repartidor al retirar. NO es KmInicial: ese es
    /// del cierre de administración, que puede aprobar este número o corregirlo con motivo.</summary>
    public int? RetiroKmInicial { get; set; }

    /// <summary>RNF-02: idempotencia del retiro, mismo rol que PruebaEntrega.DeviceUuid.</summary>
    public string? RetiroDeviceUuid { get; set; }

    // ── Cierre de jornada declarado por el repartidor (RF-26, mitad de calle). Juego de columnas
    // separado del cierre de administración a propósito (acta changelog 4.7): lo que dijo la
    // calle queda intacto al lado de lo que cerró administración, y el desvío entre los dos es
    // evidencia. Mismo trigger los vuelve inmutables una vez sellado CierreRepartidorEn.

    public DateTimeOffset? CierreRepartidorEn { get; set; }
    public int? CierreRepartidorKmFinal { get; set; }
    public decimal? CierreRepartidorCombustible { get; set; }
    public decimal? CierreRepartidorPeajes { get; set; }
    public string? CierreRepartidorNotas { get; set; }
    public string? CierreRepartidorDeviceUuid { get; set; }
}
