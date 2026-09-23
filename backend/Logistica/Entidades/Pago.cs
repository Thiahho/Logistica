namespace Logistica.Entidades;

/// <summary>
/// E1 (Anexo I §10.2-B/D12). Libro de solo inserción, protegido por trg_pagos_inmutable — un pago
/// mal cargado se corrige con un contraasiento (Monto negativo + Nota obligatoria), nunca
/// editando el original.
///
/// Sin FacturaId a propósito: D12 dice que el cliente no elige la imputación, y no hay ningún
/// escritor legítimo de esa columna en el sistema. La imputación es FIFO y se deriva en lectura
/// (v_facturas_saldo, docs/schema_v3.sql) contra ClienteId, nunca se persiste acá.
/// </summary>
public class Pago
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public decimal Monto { get; set; }
    public DateOnly FechaPago { get; set; }

    /// <summary>transferencia | efectivo | cheque | otro</summary>
    public string Medio { get; set; } = null!;
    public string? Nota { get; set; }

    public Guid? RegistradoPor { get; set; }
    public Usuario? RegistradoPorUsuario { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}
