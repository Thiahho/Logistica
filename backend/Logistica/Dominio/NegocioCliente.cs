using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>
/// "Mi negocio" del dueño de una empresa cliente: períodos de control (día, semana, mes), cómo se
/// agrupan los estados de un envío para contarlos y el estado de cuenta con saldo acumulado. Sin base
/// de datos, para testearlo solo.
/// </summary>
public static class NegocioCliente
{
    public const string Dia = "dia";
    public const string Semana = "semana";
    public const string Mes = "mes";

    public record RangoFechas(DateOnly Desde, DateOnly Hasta)
    {
        public int Dias => Hasta.DayNumber - Desde.DayNumber + 1;
    }

    public record Periodos(RangoFechas Actual, RangoFechas Anterior);

    /// <summary>El período de tipo `tipo` que contiene a `fecha`, y el inmediato anterior. La semana
    /// va de lunes a domingo; el mes es el calendario. null si el tipo no existe.</summary>
    public static Periodos? Periodo(string tipo, DateOnly fecha)
    {
        switch (tipo)
        {
            case Dia:
                return new(new(fecha, fecha), new(fecha.AddDays(-1), fecha.AddDays(-1)));
            case Semana:
            {
                var desdeLunes = ((int)fecha.DayOfWeek + 6) % 7; // lunes = 0 … domingo = 6
                var lunes = fecha.AddDays(-desdeLunes);
                return new(new(lunes, lunes.AddDays(6)), new(lunes.AddDays(-7), lunes.AddDays(-1)));
            }
            case Mes:
            {
                var primero = new DateOnly(fecha.Year, fecha.Month, 1);
                var anterior = primero.AddMonths(-1);
                return new(new(primero, primero.AddMonths(1).AddDays(-1)), new(anterior, primero.AddDays(-1)));
            }
            default:
                return null;
        }
    }

    /// <summary>Cómo se cuenta un envío en los resúmenes del portal.</summary>
    public enum GrupoEstado { Entregado, Fallido, Cancelado, EnCurso }

    /// <summary>"Fallido" suma Fallido y Devuelto; "En curso" es todo lo que todavía no terminó
    /// (Borrador, Confirmado, EnRuta, Reprogramado).</summary>
    public static GrupoEstado Grupo(EstadoPedido estado) => estado switch
    {
        EstadoPedido.Entregado => GrupoEstado.Entregado,
        EstadoPedido.Fallido or EstadoPedido.Devuelto => GrupoEstado.Fallido,
        EstadoPedido.Cancelado => GrupoEstado.Cancelado,
        _ => GrupoEstado.EnCurso,
    };

    public record ConteoEnvios(int Total, int Entregados, int Fallidos, int Cancelados, int EnCurso);

    public static ConteoEnvios Contar(IEnumerable<EstadoPedido> estados)
    {
        var lista = estados.Select(Grupo).ToList();
        return new ConteoEnvios(
            lista.Count,
            lista.Count(g => g == GrupoEstado.Entregado),
            lista.Count(g => g == GrupoEstado.Fallido),
            lista.Count(g => g == GrupoEstado.Cancelado),
            lista.Count(g => g == GrupoEstado.EnCurso));
    }

    public record Cargo(long FacturaId, DateOnly Fecha, DateOnly PeriodoDesde, DateOnly PeriodoHasta, decimal Monto);
    public record Abono(long PagoId, DateOnly Fecha, string Medio, string? Nota, decimal Monto);

    /// <summary>Una línea del estado de cuenta. Debe = factura emitida; Haber = pago imputado. Saldo
    /// es el acumulado después de esta línea (positivo = el cliente debe).</summary>
    public record Movimiento(DateOnly Fecha, string Tipo, long Referencia, string Descripcion,
        decimal Debe, decimal Haber, decimal Saldo);

    /// <summary>Facturas y pagos del período `rango` en orden de fecha, partiendo de `saldoInicial` (el
    /// saldo al cierre del día anterior a `rango.Desde`). En un mismo día la factura va antes que el
    /// pago: así un pago del día de emisión se lee como cancelándola. Mismo criterio de saldo que
    /// saldo_cliente() en la base (facturas − pagos).</summary>
    public static List<Movimiento> EstadoDeCuenta(decimal saldoInicial, IEnumerable<Cargo> cargos, IEnumerable<Abono> abonos)
    {
        var lineas = cargos
            .Select(c => (c.Fecha, Orden: 0, c.FacturaId, Tipo: "factura",
                Descripcion: $"Factura #{c.FacturaId} · {c.PeriodoDesde:dd/MM} al {c.PeriodoHasta:dd/MM}", Debe: c.Monto, Haber: 0m))
            .Concat(abonos.Select(a => (a.Fecha, Orden: 1, FacturaId: a.PagoId, Tipo: "pago",
                Descripcion: a.Monto < 0
                    ? $"Corrección de pago{(string.IsNullOrWhiteSpace(a.Nota) ? "" : $" · {a.Nota}")}"
                    : $"Pago · {a.Medio}", Debe: 0m, Haber: a.Monto)))
            .OrderBy(l => l.Fecha).ThenBy(l => l.Orden).ThenBy(l => l.FacturaId);

        var saldo = saldoInicial;
        var movimientos = new List<Movimiento>();
        foreach (var l in lineas)
        {
            saldo += l.Debe - l.Haber;
            movimientos.Add(new Movimiento(l.Fecha, l.Tipo, l.FacturaId, l.Descripcion, l.Debe, l.Haber, saldo));
        }
        return movimientos;
    }

    public static readonly string[] MediosPago = ["transferencia", "efectivo", "cheque", "otro"];
}
