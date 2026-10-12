using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.Extensions.Options;

namespace Logistica.Tests;

// Las columnas de precio de `pedidos` son numeric(12,2): Postgres redondea cada una por su cuenta. Si
// el desglose sale sin redondear, el total guardado puede quedar a un centavo de la suma de sus partes.
// Con precio manual (B9) CotizarAsync no consulta la base, así que estas pruebas no la necesitan.
public class PrecioServiceTests
{
    private static PrecioService Servicio(
        decimal factorUrgencia = 0.20m, decimal factorDescuentoRuta = 0.10m, params TramoKm[] tramos) =>
        new(null!, Options.Create(new OpcionesPrecio
        {
            FactorUrgencia = factorUrgencia, FactorDescuentoRuta = factorDescuentoRuta, TramosKm = tramos,
        }));

    private static Task<DesglosePrecio> CotizarAsync(
        PrecioService servicio, decimal precioManual, bool urgente = false, bool descuentoRuta = false,
        decimal peajes = 0m, decimal? km = null) =>
        servicio.CotizarAsync(
            clienteId: 1, zonaId: 1, new DateOnly(2026, 10, 5), urgente, peajes, descuentoRuta, "camioneta",
            precioManual, km is null ? null : new DistanciaResuelta(km.Value, "manual"));

    private static void EsSumaExactaDeCentavos(DesglosePrecio d)
    {
        decimal[] importes = [d.PrecioBase, d.RecargoKm, d.RecargoUrgencia, d.DescuentoRuta, d.DescuentoRango, d.Peajes, d.Total];
        Assert.All(importes, importe => Assert.Equal(decimal.Round(importe, 2), importe));
        Assert.Equal(
            d.PrecioBase + d.RecargoKm + d.RecargoUrgencia - d.DescuentoRuta - d.DescuentoRango + d.Peajes,
            d.Total);
    }

    [Fact]
    public async Task El_total_es_la_suma_de_los_componentes_ya_redondeados()
    {
        // Sin redondear: 150,0495 + 200,066 − 100,033 sobre 1000,33 = 1250,4125, que la base guardaría
        // como 1250,41 al lado de componentes que suman 1250,42.
        var desglose = await CotizarAsync(
            Servicio(tramos: new TramoKm(0m, 0.15m)), precioManual: 1000.33m, urgente: true, descuentoRuta: true, km: 12m);

        Assert.Equal(150.05m, desglose.RecargoKm);
        Assert.Equal(200.07m, desglose.RecargoUrgencia);
        Assert.Equal(100.03m, desglose.DescuentoRuta);
        Assert.Equal(1250.42m, desglose.Total);
        EsSumaExactaDeCentavos(desglose);
    }

    // numeric de Postgres redondea el medio centavo alejándose del cero; el redondeo bancario, que es
    // el de decimal.Round por defecto, daría 0,12.
    [Fact]
    public async Task El_medio_centavo_redondea_igual_que_la_base()
    {
        var desglose = await CotizarAsync(Servicio(factorUrgencia: 0.25m), precioManual: 0.50m, urgente: true);

        Assert.Equal(0.13m, desglose.RecargoUrgencia);
        Assert.Equal(0.63m, desglose.Total);
        EsSumaExactaDeCentavos(desglose);
    }

    [Fact]
    public async Task Un_precio_manual_y_peajes_con_mas_de_dos_decimales_tambien_se_redondean()
    {
        var desglose = await CotizarAsync(Servicio(), precioManual: 1000.335m, peajes: 10.005m);

        Assert.Equal(1000.34m, desglose.PrecioBase);
        Assert.Equal(10.01m, desglose.Peajes);
        Assert.Equal(1010.35m, desglose.Total);
        EsSumaExactaDeCentavos(desglose);
    }
}
