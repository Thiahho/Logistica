using System.Text.Json;
using Logistica.Servicios;

namespace Logistica.Tests;

// Mapeo de la respuesta de OpenRouteService al mismo Recorrido que devuelve OSRM.
public class RuteoServiceTests
{
    private static RuteoService.RespuestaOrs? Leer(string json) =>
        JsonSerializer.Deserialize<RuteoService.RespuestaOrs>(json);

    [Fact]
    public void Mapea_linea_distancia_y_duracion()
    {
        var recorrido = RuteoService.DesdeOrs(Leer("""
            {"type":"FeatureCollection","features":[{"type":"Feature",
              "properties":{"summary":{"distance":12345.6,"duration":987.4}},
              "geometry":{"type":"LineString","coordinates":[[-58.38,-34.60],[-58.40,-34.61]]}}]}
            """));

        Assert.NotNull(recorrido);
        Assert.Equal(12346, recorrido.DistanciaMetros);
        Assert.Equal(987, recorrido.DuracionSegundos);
        // GeoJSON es [lng, lat]: el primer punto queda Lat -34.60, Lng -58.38.
        Assert.Equal(new PuntoRuta(-34.60m, -58.38m), recorrido.Linea[0]);
        Assert.Equal(2, recorrido.Linea.Count);
    }

    [Theory]
    [InlineData("""{"features":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"features":[{"geometry":{"coordinates":[[-58.38,-34.60]]}}]}""")]
    public void Respuesta_incompleta_da_null(string json)
    {
        Assert.Null(RuteoService.DesdeOrs(Leer(json)));
    }
}
