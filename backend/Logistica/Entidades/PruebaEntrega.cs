namespace Logistica.Entidades;

public class PruebaEntrega
{
    public long Id { get; set; }

    public long PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;

    public long? ParadaId { get; set; }
    public RutaParada? Parada { get; set; }

    /// <summary>entregado | fallido</summary>
    public string Resultado { get; set; } = null!;

    /// <summary>RF-21, lista cerrada definida en la app</summary>
    public string? MotivoFallo { get; set; }
    public string? ReceptorNombre { get; set; }

    /// <summary>RF-23 en su redacción anterior a acta changelog 4.7 (solo el booleano, sin imagen).
    /// Se conserva cuando 4.7 sume documento_numero/foto_documento_path: responde "¿se verificó?".</summary>
    public bool IdentidadVerificada { get; set; }

    /// <summary>Número de documento del receptor, solo dígitos (sin puntos ni espacios). Sin imagen del
    /// documento a propósito: la foto de un documento de un tercero exige retención con plazo, purga y
    /// dictamen legal (acta RF-23, changelog 4.7) — esto es la versión reducida, solo el número.
    /// Dato personal de un tercero (RNF-09): lo ve únicamente administración.</summary>
    public string? DocumentoNumero { get; set; }

    /// <summary>Motivo escrito cuando el receptor no dio el documento. Exactamente uno de
    /// DocumentoNumero / SinDocumentoMotivo está cargado en una entrega.</summary>
    public string? SinDocumentoMotivo { get; set; }

    public string? FotoPath { get; set; }
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    /// <summary>RF-29: distancia entre la prueba y el destino</summary>
    public int? DesvioMetros { get; set; }

    /// <summary>RNF-03: la hora que vale es la de captura en el dispositivo</summary>
    public DateTimeOffset CapturadaEn { get; set; }
    public DateTimeOffset SincronizadaEn { get; set; }

    /// <summary>RNF-02: idempotencia de la sincronización, único junto a pedido_id</summary>
    public string DeviceUuid { get; set; } = null!;
}
