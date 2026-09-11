namespace Logistica.Dominio;

/// <summary>
/// E1 (cuenta corriente): todo el sistema calculaba "hoy" con
/// DateOnly.FromDateTime(DateTime.UtcNow) — una entrega cerrada a las 22:00 (AR) del día 15 se
/// guardaba con creado_en ≈ 01:00 UTC del 16 y caía en el período siguiente. Antes de E1 eso solo
/// desplazaba una vigencia de tarifa; en cuenta corriente mueve plata de una factura a otra, así
/// que deja de ser tolerable. Único punto de conversión a hora local de todo el backend — nunca
/// hacer TimeZoneInfo.ConvertTime a mano en un controller o servicio.
/// </summary>
public static class Reloj
{
    private static readonly TimeZoneInfo Argentina =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public static DateOnly HoyLocal() => ALaFechaLocal(DateTimeOffset.UtcNow);

    public static DateOnly ALaFechaLocal(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, Argentina).DateTime);
}
