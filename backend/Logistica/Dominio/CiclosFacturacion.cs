namespace Logistica.Dominio;

/// <summary>
/// E1 (Anexo I §10.2-A/D2): la regla de calendario del ciclo de facturación, declarada una sola
/// vez y sin tocar la base — hermano de TransicionesPedido.cs. Fechas fijas: quincenal cierra el
/// 15 y el último día de cada mes; mensual, solo el último día.
///
/// Sin scheduler en el proyecto (no hay Hangfire/Quartz/IHostedService — confirmado): el cierre
/// se dispara a mano desde /api/facturas/cierre. Un endpoint que solo supiera "¿cierra hoy?"
/// perdería el período entero si nadie lo corre ese día exacto (feriado, el admin no entró).
/// CierresPendientes enumera en vez de preguntar — correr el cierre tarde se recupera solo, y
/// re-correrlo el mismo día es inocuo (Servicios/CuentaCorrienteService.cs lo hace idempotente
/// a nivel de base con ux_facturas_periodo).
/// </summary>
public static class CiclosFacturacion
{
    public const string Quincenal = "quincenal";
    public const string Mensual = "mensual";

    /// <summary>Días de plazo hasta el vencimiento (D2): 7 quincenal, 10 mensual. Se cuentan
    /// desde periodo_hasta, nunca desde la fecha de emisión — ver SiguienteCierreDespuesDe.</summary>
    public static int DiasVencimiento(string ciclo) => ciclo == Quincenal ? 7 : 10;

    public static bool EsDiaDeCierre(string ciclo, DateOnly fecha) =>
        fecha.Day == DateTime.DaysInMonth(fecha.Year, fecha.Month)
        || (ciclo == Quincenal && fecha.Day == 15);

    /// <summary>Inicio del período que cierra en `cierre`: el 1 o el 16 del mes (quincenal), o
    /// siempre el 1 (mensual). Asume que `cierre` es, en efecto, un día de cierre.</summary>
    public static DateOnly InicioDePeriodo(string ciclo, DateOnly cierre) =>
        ciclo == Quincenal && cierre.Day != 15
            ? new DateOnly(cierre.Year, cierre.Month, 16)
            : new DateOnly(cierre.Year, cierre.Month, 1);

    /// <summary>
    /// Todos los cierres de este ciclo en (desdeExclusive, hasta]. `desdeExclusive = null`
    /// (cliente sin facturas todavía) no hace backfill histórico: devuelve un único cierre, el
    /// más reciente que ya pasó — todo lo pendiente hasta esa fecha entra en esa primera factura
    /// igual, porque el barrido de CuentaCorrienteService está acotado solo por arriba.
    /// </summary>
    public static IEnumerable<DateOnly> CierresPendientes(string ciclo, DateOnly? desdeExclusive, DateOnly hasta)
    {
        if (desdeExclusive is null)
        {
            var ultimo = UltimoCierreHasta(ciclo, hasta);
            if (ultimo is { } u) yield return u;
            yield break;
        }

        var candidato = SiguienteCierreDespuesDe(ciclo, desdeExclusive.Value);
        while (candidato <= hasta)
        {
            yield return candidato;
            candidato = SiguienteCierreDespuesDe(ciclo, candidato);
        }
    }

    // Caminata día por día: con ciclos de ~15/30 días, como mucho ~31 pasos — despreciable, y
    // obviamente correcta porque se apoya en EsDiaDeCierre en vez de reconstruir la aritmética
    // de calendario dos veces.
    private static DateOnly? UltimoCierreHasta(string ciclo, DateOnly fecha)
    {
        var cursor = fecha;
        for (var i = 0; i < 40; i++)
        {
            if (EsDiaDeCierre(ciclo, cursor)) return cursor;
            cursor = cursor.AddDays(-1);
        }
        return null; // defensivo; con las reglas de arriba siempre hay un cierre dentro de 40 días
    }

    private static DateOnly SiguienteCierreDespuesDe(string ciclo, DateOnly fecha)
    {
        var cursor = fecha.AddDays(1);
        while (!EsDiaDeCierre(ciclo, cursor)) cursor = cursor.AddDays(1);
        return cursor;
    }
}
