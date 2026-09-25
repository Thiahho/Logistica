namespace Logistica.Entidades;

/// <summary>
/// Pago que el dueño de una empresa cliente avisa desde el portal ("te transferí $X"). No es un
/// Pago: no mueve el saldo hasta que Administración lo confirma, y recién ahí se crea el Pago
/// (insert-only, trg_pagos_inmutable) enlazado en PagoId. Rechazado queda como registro, con motivo.
/// </summary>
public class PagoInformado
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public Guid ClienteUsuarioId { get; set; }
    public ClienteUsuario ClienteUsuario { get; set; } = null!;

    public decimal Monto { get; set; }
    public DateOnly FechaPago { get; set; }
    /// <summary>transferencia | efectivo | cheque | otro (el mismo check que pagos).</summary>
    public string Medio { get; set; } = null!;
    public string? Nota { get; set; }
    /// <summary>Foto del comprobante (AlmacenamientoFotos, carpeta "comprobantes"); opcional.</summary>
    public string? ComprobantePath { get; set; }

    /// <summary>pendiente | confirmado | rechazado (ver EstadosPagoInformado).</summary>
    public string Estado { get; set; } = EstadosPagoInformado.Pendiente;
    public string? MotivoRechazo { get; set; }
    public Guid? RevisadoPor { get; set; }
    public Usuario? RevisadoPorUsuario { get; set; }
    public DateTimeOffset? RevisadoEn { get; set; }
    public long? PagoId { get; set; }
    public Pago? Pago { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
}

public static class EstadosPagoInformado
{
    public const string Pendiente = "pendiente";
    public const string Confirmado = "confirmado";
    public const string Rechazado = "rechazado";
}
