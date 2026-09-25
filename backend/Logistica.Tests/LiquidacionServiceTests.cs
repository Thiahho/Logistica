using Logistica.Entidades;
using Logistica.Servicios;

namespace Logistica.Tests;

// B4 (acta RF-41, diseño_e2_rangos_liquidacion.md §2.2): pago por parada, bono con mínimo de éxito
// sobre entregas + fallidas imputables.
public class LiquidacionServiceTests
{
    private static readonly ParametroLiquidacion Moto = new()
    {
        TipoVehiculo = "moto", PagoPorEntrega = 1500m, BonoRuta = 5000m, PctMinimoExitosas = 90m,
    };

    [Fact]
    public void Paga_entregas_y_bono_si_alcanza_el_minimo()
    {
        var d = LiquidacionService.Calcular(Moto, entregas: 18, fallidasImputables: 2, fallidasNoImputables: 0);
        Assert.Equal(90m, d.PctExito);
        Assert.Equal(27000m, d.PagoEntregas);
        Assert.Equal(5000m, d.Bono);
        Assert.Equal(32000m, d.Total);
    }

    [Fact]
    public void Sin_bono_por_debajo_del_minimo()
    {
        var d = LiquidacionService.Calcular(Moto, entregas: 17, fallidasImputables: 3, fallidasNoImputables: 0);
        Assert.Equal(85m, d.PctExito);
        Assert.Equal(0m, d.Bono);
        Assert.Equal(25500m, d.Total);
    }

    [Fact]
    public void Las_fallidas_no_imputables_no_cuentan_en_contra()
    {
        // 10 entregas y 5 fallos por destinatario ausente: el % es sobre 10, no sobre 15.
        var d = LiquidacionService.Calcular(Moto, entregas: 10, fallidasImputables: 0, fallidasNoImputables: 5);
        Assert.Equal(100m, d.PctExito);
        Assert.Equal(5000m, d.Bono);
        Assert.Equal(5, d.FallidasNoImputables);
    }

    [Fact]
    public void Ruta_sin_ninguna_entrega_no_cobra_bono()
    {
        var d = LiquidacionService.Calcular(Moto, entregas: 0, fallidasImputables: 0, fallidasNoImputables: 3);
        Assert.Equal(100m, d.PctExito);
        Assert.Equal(0m, d.PagoEntregas);
        Assert.Equal(0m, d.Bono);
    }

    [Fact]
    public void Porcentaje_se_redondea_a_dos_decimales()
    {
        var d = LiquidacionService.Calcular(Moto, entregas: 2, fallidasImputables: 1, fallidasNoImputables: 0);
        Assert.Equal(66.67m, d.PctExito);
    }
}
