namespace Logistica.Entidades;

/// <summary>
/// B3 (acta RF-42, changelog 4.21; diseño_e2_rangos_liquidacion.md §3.1). Cinco filas fijas — Sin
/// rango, Bronce, Plata, Oro, Empresa —, sembradas por la migración AgregarRangos; agregar un rango es
/// cambio de alcance (Anexo I §6). Lo que la Empresa configura son los umbrales y los efectos: todos
/// arrancan vacíos (acta §13). Un umbral vacío no exige nada; un rango sin ningún umbral no se alcanza
/// por cálculo, solo por ajuste manual.
/// </summary>
public class Rango
{
    /// <summary>sin_rango | bronce | plata | oro | empresa</summary>
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public int Orden { get; set; }

    public int? MinEnviosTrimestre { get; set; }
    public decimal? MinFacturacionTrimestre { get; set; }
    public int? MinAntiguedadMeses { get; set; }
    public int? MinSemanasActivas { get; set; }
    public decimal? MinPctPagosEnTermino { get; set; }

    /// <summary>Definición J: % de descuento sobre la tarifa general de la zona.</summary>
    public decimal DescuentoPct { get; set; }

    /// <summary>Solo avisa en el alta (acta 4.21): el bloqueo sigue siendo el corte por deuda de D11.</summary>
    public decimal? LimiteCredito { get; set; }

    /// <summary>Orden de los pedidos pendientes al armar una ruta (acta 4.21 frente a §7): nunca el
    /// orden de las paradas.</summary>
    public int Prioridad { get; set; }
}
