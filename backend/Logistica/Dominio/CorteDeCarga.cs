namespace Logistica.Dominio;

/// <summary>
/// RF-08 (acta §7, estructura de la jornada): a la hora de corte se cierra la carga del día siguiente,
/// para que la franja de planificación trabaje sobre un conjunto que ya no cambia. Es una validación
/// del alta, no un proceso: pasada la hora no entran pedidos nuevos con entrega mañana; los ya
/// cargados no se tocan. Las urgencias del día (RF-45) y los reintentos que genera el sistema no pasan
/// por acá. Lógica pura: se prueba sola en Logistica.Tests.
/// </summary>
public static class CorteDeCarga
{
    /// <summary>true si `fechaEntrega` es mañana y la carga de mañana ya cerró.</summary>
    public static bool Cerrada(DateOnly fechaEntrega, DateOnly hoy, TimeOnly ahora, TimeOnly horaCorte) =>
        fechaEntrega == hoy.AddDays(1) && ahora > horaCorte;

    /// <summary>La primera fecha de entrega futura que todavía admite carga: mañana antes del corte,
    /// pasado mañana después.</summary>
    public static DateOnly PrimeraFechaAbierta(DateOnly hoy, TimeOnly ahora, TimeOnly horaCorte) =>
        hoy.AddDays(ahora > horaCorte ? 2 : 1);

    public static string Mensaje(TimeOnly horaCorte, DateOnly primeraFechaAbierta) =>
        $"La carga para mañana cerró a las {horaCorte:HH\\:mm}; esto se carga a partir del {primeraFechaAbierta:dd/MM}.";
}
