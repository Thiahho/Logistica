namespace Logistica.Entidades;

/// <summary>
/// B3 — historial de rango de un cliente (acta RF-42; diseño_e2_rangos_liquidacion.md §3.3). Una fila
/// por cambio de rango efectivo o por ajuste manual, con lo medido. Solo inserción
/// (trg_cliente_rangos_inmutable): es la explicación de por qué cambió el precio de un cliente.
/// </summary>
public class ClienteRango
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public string? RangoAnterior { get; set; }
    public string RangoNuevo { get; set; } = null!;

    /// <summary>recalculo | ajuste</summary>
    public string Origen { get; set; } = null!;

    /// <summary>Trimestre medido ("2026-T3"), solo en un recálculo.</summary>
    public string? Trimestre { get; set; }

    /// <summary>CriteriosRango serializado (jsonb), solo en un recálculo.</summary>
    public string? Criterios { get; set; }

    public string? Motivo { get; set; }

    public Guid RegistradoPor { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}
