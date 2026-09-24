using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Tests;

// B7 (acta RF-43, diseño_e2_rangos_liquidacion.md §5.2): el mes contra los tramos de la estructura objetivo.
public class RentabilidadTests
{
    private static readonly DatosMes Mes = new(
        Ingresos: 1_000_000m, PagoRepartidor: 450_000m, Combustible: 120_000m, Peajes: 30_000m, OtrosCostos: 20_000m,
        FijosPorCategoria: new Dictionary<string, decimal> { ["alquiler"] = 80_000m, ["software"] = 20_000m });

    private static ObjetivoRentabilidad Tramo(string nombre, decimal min, decimal max, params string[] fuentes) =>
        new() { Nombre = nombre, PctMin = min, PctMax = max, Fuentes = fuentes };

    [Fact]
    public void Totales_del_mes()
    {
        Assert.Equal(620_000m, Mes.Variables);
        Assert.Equal(100_000m, Mes.Fijos);
        Assert.Equal(280_000m, Mes.Margen);
    }

    [Fact]
    public void Tramo_dentro_debajo_y_encima()
    {
        var operativos = Rentabilidad.Evaluar(Mes, Tramo("Operativos", 60, 65, "pago_repartidor", "combustible", "peajes", "otros_costos"));
        Assert.Equal(62m, operativos.Pct);
        Assert.Equal("dentro", operativos.Estado);

        var fijos = Rentabilidad.Evaluar(Mes, Tramo("Fijos", 10, 15, "fijos"));
        Assert.Equal(10m, fijos.Pct);
        Assert.Equal("dentro", fijos.Estado);

        var margen = Rentabilidad.Evaluar(Mes, Tramo("Margen", 30, 40, "margen"));
        Assert.Equal(28m, margen.Pct);
        Assert.Equal("debajo", margen.Estado);

        var soloAlquiler = Rentabilidad.Evaluar(Mes, Tramo("Alquiler", 0, 5, "fijos:alquiler"));
        Assert.Equal(80_000m, soloAlquiler.Monto);
        Assert.Equal("encima", soloAlquiler.Estado);
    }

    [Fact]
    public void Una_fuente_repetida_no_se_suma_dos_veces()
    {
        Assert.Equal(120_000m, Rentabilidad.Evaluar(Mes, Tramo("x", 0, 100, "combustible", "combustible")).Monto);
    }

    [Fact]
    public void Sin_ingresos_no_hay_porcentaje()
    {
        var vacio = Mes with { Ingresos = 0 };
        var t = Rentabilidad.Evaluar(vacio, Tramo("Fijos", 10, 15, "fijos"));
        Assert.Null(t.Pct);
        Assert.Null(t.Estado);
        Assert.Equal(100_000m, t.Monto);
    }

    [Theory]
    [InlineData("margen", true)]
    [InlineData("fijos:alquiler", true)]
    [InlineData("fijos:", false)]
    [InlineData("sueldos", false)]
    public void Fuentes_validas(string fuente, bool valida) => Assert.Equal(valida, Rentabilidad.FuenteValida(fuente));

    [Theory]
    [InlineData("2026-09", 2026, 9)]
    [InlineData("2025-12", 2025, 12)]
    public void Mes_valido(string texto, int anio, int mes) => Assert.Equal(new DateOnly(anio, mes, 1), Rentabilidad.Mes(texto));

    [Theory]
    [InlineData("2026-13")]
    [InlineData("setiembre")]
    [InlineData("2026-9-1")]
    public void Mes_invalido(string texto) => Assert.Null(Rentabilidad.Mes(texto));
}
