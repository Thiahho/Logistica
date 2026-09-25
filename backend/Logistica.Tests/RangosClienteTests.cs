using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Tests;

// B3 (acta RF-42, diseño_e2_rangos_liquidacion.md §3): rango calculado, efectivo con el ajuste de la
// definición F y % de pagos en término con la imputación FIFO de D12.
public class RangosClienteTests
{
    private static List<Rango> Rangos(Action<List<Rango>>? configurar = null)
    {
        var lista = new List<Rango>
        {
            new() { Codigo = "sin_rango", Nombre = "Sin rango", Orden = 0 },
            new() { Codigo = "bronce", Nombre = "Bronce", Orden = 1 },
            new() { Codigo = "plata", Nombre = "Plata", Orden = 2 },
            new() { Codigo = "oro", Nombre = "Oro", Orden = 3 },
            new() { Codigo = "empresa", Nombre = "Empresa", Orden = 4 },
        };
        configurar?.Invoke(lista);
        return lista;
    }

    private static CriteriosRango Criterios(int envios = 0, decimal facturacion = 0, int antiguedad = 0, int semanas = 0, decimal pct = 100) =>
        new(envios, facturacion, antiguedad, semanas, pct, 0);

    [Fact]
    public void Sin_umbrales_cargados_nadie_sube_de_rango()
    {
        Assert.Equal("sin_rango", RangosCliente.Calcular(Criterios(envios: 999, facturacion: 1e9m, antiguedad: 99), Rangos()));
    }

    [Fact]
    public void Toma_el_rango_mas_alto_que_cumple_todos_sus_umbrales()
    {
        var rangos = Rangos(l =>
        {
            l[1].MinEnviosTrimestre = 10;
            l[2].MinEnviosTrimestre = 50;
            l[2].MinPctPagosEnTermino = 90;
            l[3].MinEnviosTrimestre = 200;
        });
        Assert.Equal("bronce", RangosCliente.Calcular(Criterios(envios: 60, pct: 80), rangos)); // plata falla por pagos
        Assert.Equal("plata", RangosCliente.Calcular(Criterios(envios: 60, pct: 95), rangos));
        Assert.Equal("oro", RangosCliente.Calcular(Criterios(envios: 250, pct: 10), rangos)); // oro no exige pagos
        Assert.Equal("sin_rango", RangosCliente.Calcular(Criterios(envios: 5), rangos));
    }

    [Fact]
    public void Ajuste_vigente_mueve_un_rango_y_respeta_los_extremos()
    {
        var rangos = Rangos();
        var hoy = new DateOnly(2026, 9, 24);
        var manana = hoy.AddDays(1);
        Assert.Equal("oro", RangosCliente.Efectivo("plata", 1, manana, hoy, rangos));
        Assert.Equal("bronce", RangosCliente.Efectivo("plata", -1, manana, hoy, rangos));
        Assert.Equal("sin_rango", RangosCliente.Efectivo("sin_rango", -1, manana, hoy, rangos));
        Assert.Equal("empresa", RangosCliente.Efectivo("empresa", 1, manana, hoy, rangos));
        Assert.Equal("oro", RangosCliente.Efectivo("plata", 1, hoy, hoy, rangos)); // vence hoy: todavía vale
    }

    [Fact]
    public void Ajuste_vencido_no_cuenta()
    {
        var hoy = new DateOnly(2026, 9, 24);
        Assert.Equal("plata", RangosCliente.Efectivo("plata", 1, hoy.AddDays(-1), hoy, Rangos()));
    }

    [Fact]
    public void Pagos_en_termino_con_imputacion_fifo()
    {
        var desde = new DateOnly(2026, 7, 1);
        var hasta = new DateOnly(2026, 9, 30);
        // Tres facturas de 100 que vencen en el trimestre; los pagos cubren a tiempo la primera y la
        // tercera no: el pago de 150 del 20/8 cubre la primera entera y la mitad de la segunda (FIFO).
        var facturas = new (long, DateOnly, DateOnly, decimal)[]
        {
            (1, new(2026, 7, 1), new(2026, 7, 10), 100m),
            (2, new(2026, 8, 1), new(2026, 8, 10), 100m),
            (3, new(2026, 9, 1), new(2026, 9, 10), 100m),
        };
        var pagos = new (DateOnly, decimal)[] { (new(2026, 7, 5), 100m), (new(2026, 8, 20), 150m) };

        var (pct, vencidas) = RangosCliente.PctPagosEnTermino(facturas, pagos, desde, hasta);
        Assert.Equal(3, vencidas);
        Assert.Equal(33.33m, pct);
    }

    [Fact]
    public void Sin_facturas_vencidas_en_el_trimestre_no_hay_incumplimiento()
    {
        var (pct, vencidas) = RangosCliente.PctPagosEnTermino([], [], new(2026, 7, 1), new(2026, 9, 30));
        Assert.Equal(100m, pct);
        Assert.Equal(0, vencidas);
    }

    [Theory]
    [InlineData("2026-T3", 2026, 7, 1, 2026, 9, 30)]
    [InlineData("2026-T1", 2026, 1, 1, 2026, 3, 31)]
    [InlineData("2025-T4", 2025, 10, 1, 2025, 12, 31)]
    public void Trimestre_calendario(string texto, int a1, int m1, int d1, int a2, int m2, int d2)
    {
        Assert.Equal((new DateOnly(a1, m1, d1), new DateOnly(a2, m2, d2)), RangosCliente.Trimestre(texto));
    }

    [Theory]
    [InlineData("2026-T5")]
    [InlineData("2026T3")]
    [InlineData("abc")]
    public void Trimestre_invalido(string texto) => Assert.Null(RangosCliente.Trimestre(texto));

    [Fact]
    public void Meses_cumplidos()
    {
        Assert.Equal(2, RangosCliente.MesesEntre(new(2026, 7, 15), new(2026, 9, 30)));
        Assert.Equal(2, RangosCliente.MesesEntre(new(2026, 7, 31), new(2026, 9, 30)));
        Assert.Equal(0, RangosCliente.MesesEntre(new(2026, 10, 1), new(2026, 9, 30)));
    }
}
