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

    /// <summary>RF-23: verificación sin almacenar imagen del documento</summary>
    public bool IdentidadVerificada { get; set; }

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
