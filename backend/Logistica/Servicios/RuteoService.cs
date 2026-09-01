using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Logistica.Servicios;

public record PuntoRuta(decimal Lat, decimal Lng);

public record Recorrido(IReadOnlyList<PuntoRuta> Linea, int DistanciaMetros, int DuracionSegundos);

/// <summary>
/// construccion_v1.md §1: ruteo por calles solo desde el servidor, mismo criterio que
/// GeocodificacionService (regla 3.2: ninguna credencial ni proveedor externo va al bundle del
/// navegador). Usa OSRM; en desarrollo, contra el demo público (router.project-osrm.org), cuya
/// política de uso no admite producción — ahí "Ruteo:BaseUrl" apunta a un contenedor propio.
///
/// Cacheado en memoria (sin tabla nueva, acta changelog 3.4): el planificador reordena las mismas
/// paradas una y otra vez al probar "sugerir orden" o mover flechas, y las combinaciones de
/// coordenadas se repiten mucho dentro de una misma sesión de armado.
/// </summary>
public class RuteoService(HttpClient http, IMemoryCache cache)
{
    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(30);

    private record RespuestaOsrm(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("routes")] List<RutaOsrm>? Routes);

    private record RutaOsrm(
        [property: JsonPropertyName("geometry")] GeometriaOsrm Geometry,
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("duration")] double Duration);

    private record GeometriaOsrm(
        [property: JsonPropertyName("coordinates")] List<List<double>> Coordinates);

    public async Task<Recorrido?> TrazarAsync(IReadOnlyList<PuntoRuta> puntos, CancellationToken ct = default)
    {
        if (puntos.Count < 2) return null;

        var clave = ClaveCache(puntos);
        if (cache.TryGetValue<Recorrido?>(clave, out var enCache)) return enCache;

        var coordenadas = string.Join(";", puntos.Select(p =>
            $"{p.Lng.ToString(CultureInfo.InvariantCulture)},{p.Lat.ToString(CultureInfo.InvariantCulture)}"));
        // overview=simplified (no "full"): Douglas-Peucker del lado de OSRM. Con 8-25 paradas la
        // geometría baja de miles de puntos a unos pocos cientos sin diferencia visible a zoom de
        // ciudad — importa porque este payload viaja embebido en JornadaDelDia y de ahí a Dexie
        // (fase de cola offline).
        var url = $"route/v1/driving/{coordenadas}?overview=simplified&geometries=geojson";

        Recorrido? resultado;
        try
        {
            var respuesta = await http.GetFromJsonAsync<RespuestaOsrm>(url, ct);
            var mejor = respuesta?.Code == "Ok" ? respuesta.Routes?.FirstOrDefault() : null;
            resultado = mejor is null
                ? null
                : new Recorrido(
                    mejor.Geometry.Coordinates.Select(c => new PuntoRuta((decimal)c[1], (decimal)c[0])).ToList(),
                    (int)Math.Round(mejor.Distance),
                    (int)Math.Round(mejor.Duration));
        }
        catch (HttpRequestException)
        {
            // El mapa degrada a línea recta entre los puntos (ver Mapa.tsx): un proveedor externo
            // caído no puede tirar abajo la pantalla de armado ni la del repartidor.
            resultado = null;
        }

        cache.Set(clave, resultado, DuracionCache);
        return resultado;
    }

    /// Redondea a 5 decimales (~1 m de precisión) para que reordenamientos que no cambian
    /// realmente la geometría (o vueltas atrás sobre el mismo conjunto) reusen la misma entrada.
    private static string ClaveCache(IReadOnlyList<PuntoRuta> puntos) =>
        "recorrido:" + string.Join("|", puntos.Select(p =>
            $"{Math.Round(p.Lat, 5)},{Math.Round(p.Lng, 5)}"));
}
