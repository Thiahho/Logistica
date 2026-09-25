using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>Los números de un mes, antes de compararlos con los tramos.</summary>
public record DatosMes(
    decimal Ingresos, decimal PagoRepartidor, decimal Combustible, decimal Peajes, decimal OtrosCostos,
    IReadOnlyDictionary<string, decimal> FijosPorCategoria)
{
    public decimal Variables => PagoRepartidor + Combustible + Peajes + OtrosCostos;
    public decimal Fijos => FijosPorCategoria.Values.Sum();
    public decimal Margen => Ingresos - Variables - Fijos;
}

/// <summary>Un tramo comparado: cuánto suma, qué % de los ingresos es, y si queda debajo, dentro o encima.
/// Pct y Estado null si el mes no tuvo ingresos (no hay contra qué medir).</summary>
public record TramoEvaluado(int Id, string Nombre, decimal PctMin, decimal PctMax, string[] Fuentes,
    decimal Monto, decimal? Pct, string? Estado);

/// <summary>
/// B7 — resultado del mes contra la estructura objetivo (acta RF-43; diseño_e2_rangos_liquidacion.md §5.2).
/// Lógica pura: RentabilidadController junta los números de la base y esto los compara.
/// </summary>
public static class Rentabilidad
{
    public static readonly string[] FuentesFijas = ["pago_repartidor", "combustible", "peajes", "otros_costos", "fijos", "margen"];

    /// <summary>Una fuente es una de las fijas o "fijos:&lt;categoría&gt;" con una categoría no vacía.</summary>
    public static bool FuenteValida(string fuente) =>
        FuentesFijas.Contains(fuente) || (fuente.StartsWith("fijos:") && fuente.Length > "fijos:".Length);

    public static decimal Monto(DatosMes d, string fuente) => fuente switch
    {
        "pago_repartidor" => d.PagoRepartidor,
        "combustible" => d.Combustible,
        "peajes" => d.Peajes,
        "otros_costos" => d.OtrosCostos,
        "fijos" => d.Fijos,
        "margen" => d.Margen,
        _ when fuente.StartsWith("fijos:") => d.FijosPorCategoria.GetValueOrDefault(fuente["fijos:".Length..]),
        _ => 0m,
    };

    public static TramoEvaluado Evaluar(DatosMes d, ObjetivoRentabilidad o)
    {
        var monto = o.Fuentes.Distinct().Sum(f => Monto(d, f));
        if (d.Ingresos <= 0) return new TramoEvaluado(o.Id, o.Nombre, o.PctMin, o.PctMax, o.Fuentes, monto, null, null);

        var pct = Math.Round(monto * 100m / d.Ingresos, 2);
        var estado = pct < o.PctMin ? "debajo" : pct > o.PctMax ? "encima" : "dentro";
        return new TramoEvaluado(o.Id, o.Nombre, o.PctMin, o.PctMax, o.Fuentes, monto, pct, estado);
    }

    /// <summary>"2026-09" → 1/9/2026.</summary>
    public static DateOnly? Mes(string texto) =>
        DateOnly.TryParseExact(texto + "-01", "yyyy-MM-dd", out var d) && d.Year is >= 2000 and <= 2100 ? d : null;
}
