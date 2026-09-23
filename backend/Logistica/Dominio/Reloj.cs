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

    /// <summary>B5 (diseño_b5_portal_carga.md §6): corte horario del portal, necesita la hora del
    /// día, no solo la fecha — mismo criterio que el resto de esta clase, nunca convertir a mano.</summary>
    public static TimeOnly HoraLocal() =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Argentina).DateTime);

    /// <summary>B13 (diseño_b13_recepcion_portal.md §5): límites en UTC del día local `dia`, para
    /// filtrar una columna `timestamptz` (`pedidos.creado_en`) por fecha local sin convertir cada
    /// fila — misma razón de ser que el resto de esta clase, la comparación queda traducible a SQL
    /// como un simple rango en vez de necesitar una conversión por fila.</summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) RangoLocalUtc(DateOnly dia)
    {
        var desde = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), Argentina.GetUtcOffset(dia.ToDateTime(TimeOnly.MinValue)));
        return (desde.ToUniversalTime(), desde.AddDays(1).ToUniversalTime());
    }
}
