namespace Logistica.Dominio;

/// <summary>
/// Qué pago se puede registrar a mano (E1, Anexo I §10.2-B/D12). `pagos` es de solo inserción
/// (trg_pagos_inmutable): un pago mal cargado se corrige con un contraasiento — monto negativo y nota
/// obligatoria (ck_pagos_monto, ck_pagos_reverso_nota). Los checks de la base son la red de seguridad;
/// esto da el mensaje en español antes de llegar a ellos, y agrega el tope que la base no tiene.
/// Lógica pura: se prueba sola en Logistica.Tests.
/// </summary>
public static class PagosCliente
{
    /// <summary>numeric(12,2): diez dígitos enteros.</summary>
    private const decimal MontoMaximo = 9_999_999_999.99m;

    /// <summary>null si el pago se puede registrar; si no, el motivo.</summary>
    /// <param name="pagadoHastaAhora">Suma de todos los pagos del cliente, correcciones incluidas.</param>
    public static string? Validar(decimal monto, string? nota, decimal pagadoHastaAhora)
    {
        if (monto == 0) return "El monto no puede ser cero.";
        if (Math.Abs(monto) > MontoMaximo) return "El monto es demasiado grande.";
        if (monto > 0) return null;

        if (string.IsNullOrWhiteSpace(nota))
            return "Una corrección (monto negativo) necesita una nota que explique qué se corrige.";
        // Una corrección deshace pagos cargados: no puede restar más de lo que hay registrado.
        if (pagadoHastaAhora + monto < 0)
            return $"La corrección supera lo que el cliente tiene registrado como pagado (${pagadoHastaAhora:N2}).";
        return null;
    }
}
