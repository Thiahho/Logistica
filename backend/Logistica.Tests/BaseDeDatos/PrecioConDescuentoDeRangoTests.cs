using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// El descuento por rango (definición J) solo existe cuando el precio sale de la lista general, y eso
// pasa por tarifa_vigente(): es el único componente del desglose que no se alcanza sin base.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class PrecioConDescuentoDeRangoTests(BaseDePrueba baseDatos)
{
    // Tarifa general de un solo día, lejos de las fechas del resto de las pruebas: la zona es compartida.
    private static readonly DateOnly DiaDeLaTarifa = new(2031, 3, 3);

    [Fact]
    public async Task El_descuento_por_rango_se_redondea_y_el_total_suma_los_componentes_redondeados()
    {
        var cliente = await baseDatos.ClienteAsync();
        var zonaId = await baseDatos.EscalarAsync<int>("select id from zonas where codigo = 'Z'");
        await baseDatos.EjecutarAsync("update rangos set descuento_pct = 7.5 where codigo = 'bronce'");
        await baseDatos.EjecutarAsync($"update clientes set rango_calculado = 'bronce' where id = {cliente.Id}");
        await using var db = baseDatos.CrearContexto();
        db.Tarifas.Add(new Tarifa
        {
            ZonaId = zonaId, TipoVehiculo = "camioneta", Precio = 1000.33m,
            VigenteDesde = DiaDeLaTarifa, VigenteHasta = DiaDeLaTarifa, CreadaEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var servicio = new PrecioService(db, Options.Create(new OpcionesPrecio { FactorUrgencia = 0.20m }));

        var desglose = await servicio.CotizarAsync(
            cliente.Id, zonaId, DiaDeLaTarifa, urgente: true, peajes: 0m, descuentoRuta: false, "camioneta");

        // Sin redondear: 1000,33 + 200,066 − 75,02475 = 1125,37125, que la base guardaría como 1125,37
        // al lado de componentes que suman 1125,38.
        Assert.Equal(1000.33m, desglose.PrecioBase);
        Assert.Equal(200.07m, desglose.RecargoUrgencia);
        Assert.Equal(75.02m, desglose.DescuentoRango);
        Assert.Equal(1125.38m, desglose.Total);
        Assert.Equal(
            desglose.PrecioBase + desglose.RecargoUrgencia - desglose.DescuentoRango, desglose.Total);
    }
}
