using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>Lo medido de un cliente en un trimestre (acta RF-42, diseño_e2_rangos_liquidacion.md §3.1).
/// Queda guardado tal cual en cliente_rangos.criterios: es lo que permite explicarle a un cliente por
/// qué cambió su precio.</summary>
public record CriteriosRango(
    int Envios, decimal Facturacion, int AntiguedadMeses, int SemanasActivas, decimal PctPagosEnTermino,
    int FacturasVencidas);

/// <summary>
/// B3 — lógica pura de rangos, sin base: se prueba sola (Logistica.Tests) y la usa
/// Servicios/RangoClienteService.cs. Los códigos de rango son fijos (sin_rango … empresa, ordenados por
/// Rango.Orden); agregar uno es cambio de alcance (Anexo I §6).
/// </summary>
public static class RangosCliente
{
    public const string SinRango = "sin_rango";

    /// <summary>
    /// El rango más alto cuyos umbrales cargados se cumplen todos. Un umbral vacío no exige nada, pero
    /// un rango sin ningún umbral cargado no se alcanza por cálculo (solo por ajuste manual) — si no,
    /// con la tabla recién creada, todos los clientes serían "empresa".
    /// </summary>
    public static string Calcular(CriteriosRango c, IEnumerable<Rango> rangos)
    {
        var alcanzado = rangos
            .Where(r => r.Codigo != SinRango && TieneUmbrales(r) && Cumple(c, r))
            .OrderByDescending(r => r.Orden)
            .FirstOrDefault();
        return alcanzado?.Codigo ?? SinRango;
    }

    public static bool TieneUmbrales(Rango r) =>
        r.MinEnviosTrimestre is not null || r.MinFacturacionTrimestre is not null || r.MinAntiguedadMeses is not null
        || r.MinSemanasActivas is not null || r.MinPctPagosEnTermino is not null;

    private static bool Cumple(CriteriosRango c, Rango r) =>
        (r.MinEnviosTrimestre is null || c.Envios >= r.MinEnviosTrimestre)
        && (r.MinFacturacionTrimestre is null || c.Facturacion >= r.MinFacturacionTrimestre)
        && (r.MinAntiguedadMeses is null || c.AntiguedadMeses >= r.MinAntiguedadMeses)
        && (r.MinSemanasActivas is null || c.SemanasActivas >= r.MinSemanasActivas)
        && (r.MinPctPagosEnTermino is null || c.PctPagosEnTermino >= r.MinPctPagosEnTermino);

    /// <summary>Definición F: el calculado, movido un rango (±1) por el ajuste manual si todavía no
    /// venció, sin salirse de la escala. Un ajuste vencido no cuenta, aunque nadie lo haya limpiado:
    /// no hace falta un proceso que lo borre a tiempo.</summary>
    public static string Efectivo(
        string calculado, short ajuste, DateOnly? vence, DateOnly hoy, IReadOnlyList<Rango> rangos)
    {
        if (ajuste == 0 || vence is null || vence < hoy) return calculado;
        var ordenados = rangos.OrderBy(r => r.Orden).ToList();
        var i = ordenados.FindIndex(r => r.Codigo == calculado);
        if (i < 0) return calculado;
        return ordenados[Math.Clamp(i + ajuste, 0, ordenados.Count - 1)].Codigo;
    }

    /// <summary>
    /// % de facturas con vencimiento en [desde, hasta] que estaban cubiertas a su vencimiento, con la
    /// imputación FIFO de D12 (la misma de v_facturas_saldo): una factura está cubierta si lo pagado hasta
    /// su vencimiento alcanza a todo lo facturado hasta ella inclusive. Sin facturas vencidas en el
    /// trimestre no hubo nada que incumplir: 100.
    /// </summary>
    public static (decimal Pct, int Vencidas) PctPagosEnTermino(
        IEnumerable<(long Id, DateOnly Emision, DateOnly Vencimiento, decimal Total)> facturas,
        IEnumerable<(DateOnly Fecha, decimal Monto)> pagos,
        DateOnly desde, DateOnly hasta)
    {
        var ordenadas = facturas.OrderBy(f => f.Emision).ThenBy(f => f.Id).ToList();
        var listaPagos = pagos.ToList();
        var acumulado = 0m;
        int vencidas = 0, enTermino = 0;
        foreach (var f in ordenadas)
        {
            acumulado += f.Total;
            if (f.Vencimiento < desde || f.Vencimiento > hasta) continue;
            vencidas++;
            var pagadoAlVencer = listaPagos.Where(p => p.Fecha <= f.Vencimiento).Sum(p => p.Monto);
            if (pagadoAlVencer >= acumulado) enTermino++;
        }
        return vencidas == 0 ? (100m, 0) : (Math.Round(enTermino * 100m / vencidas, 2), vencidas);
    }

    /// <summary>Trimestre calendario "2026-T3" → [1/7/2026, 30/9/2026].</summary>
    public static (DateOnly Desde, DateOnly Hasta)? Trimestre(string texto)
    {
        var partes = texto.Split("-T");
        if (partes.Length != 2 || !int.TryParse(partes[0], out var anio) || !int.TryParse(partes[1], out var t)
            || t is < 1 or > 4 || anio is < 2000 or > 2100)
            return null;
        var desde = new DateOnly(anio, (t - 1) * 3 + 1, 1);
        return (desde, desde.AddMonths(3).AddDays(-1));
    }

    /// <summary>Meses cumplidos entre el alta y el fin del trimestre. Un alta del 31 cumple mes el último
    /// día de un mes más corto (31/7 → 30/9 son dos meses).</summary>
    public static int MesesEntre(DateOnly alta, DateOnly hasta)
    {
        var meses = (hasta.Year - alta.Year) * 12 + hasta.Month - alta.Month;
        var finDeMes = hasta.Day == DateTime.DaysInMonth(hasta.Year, hasta.Month);
        if (hasta.Day < alta.Day && !finDeMes) meses--;
        return Math.Max(meses, 0);
    }
}
