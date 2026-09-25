using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Logistica.Opciones;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

public record PuntoRuta(decimal Lat, decimal Lng);

public record Recorrido(IReadOnlyList<PuntoRuta> Linea, int DistanciaMetros, int DuracionSegundos);

/// <summary>
/// construccion_v1.md §1: ruteo por calles solo desde el servidor, mismo criterio que
/// GeocodificacionService (regla 3.2: ninguna credencial ni proveedor externo va al bundle del
/// navegador). Proveedor según "Ruteo:Proveedor" (Opciones/OpcionesRuteo.cs): OSRM en desarrollo
/// (demo público, no admite producción), OpenRouteService en producción, o ninguno. Hacia afuera
/// devuelve siempre el mismo Recorrido, sea cual sea el proveedor.
///
/// Cacheado en memoria (sin tabla nueva, acta changelog 3.4): el planificador reordena las mismas
/// paradas una y otra vez al probar "sugerir orden" o mover flechas, y las combinaciones de
/// coordenadas se repiten mucho dentro de una misma sesión de armado. En ORS además cuida la cuota
/// diaria del plan gratuito.
/// </summary>
public class RuteoService(HttpClient http, IMemoryCache cache, IOptions<OpcionesRuteo> opciones)
{
    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(30);

    /// <summary>Tope de puntos por recorrido de OpenRouteService. Por encima, línea recta en vez de
    /// un error del proveedor.</summary>
    public const int MaxPuntosOrs = 50;

    private record RespuestaOsrm(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("routes")] List<RutaOsrm>? Routes);

    private record RutaOsrm(
        [property: JsonPropertyName("geometry")] Geometria Geometry,
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("duration")] double Duration);

    internal record Geometria(
        [property: JsonPropertyName("coordinates")] List<List<double>> Coordinates);

    // POST /v2/directions/driving-car/geojson: un FeatureCollection con un LineString por recorrido.
    internal record RespuestaOrs(
        [property: JsonPropertyName("features")] List<FeatureOrs>? Features);

    internal record FeatureOrs(
        [property: JsonPropertyName("geometry")] Geometria? Geometry,
        [property: JsonPropertyName("properties")] PropiedadesOrs? Properties);

    internal record PropiedadesOrs(
        [property: JsonPropertyName("summary")] ResumenOrs? Summary);

    internal record ResumenOrs(
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("duration")] double Duration);

    public async Task<Recorrido?> TrazarAsync(IReadOnlyList<PuntoRuta> puntos, CancellationToken ct = default)
    {
        if (puntos.Count < 2) return null;

        var proveedor = opciones.Value.Proveedor;
        if (proveedor == "ninguno") return null;

        var clave = ClaveCache(puntos);
        if (cache.TryGetValue<Recorrido?>(clave, out var enCache)) return enCache;

        Recorrido? resultado;
        try
        {
            resultado = proveedor == "ors" ? await TrazarOrsAsync(puntos, ct) : await TrazarOsrmAsync(puntos, ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException
                                     || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // El mapa degrada a línea recta entre los puntos (ver Mapa.tsx): un proveedor externo
            // caído, lento (timeout) o que responde algo inesperado no puede tirar abajo la pantalla
            // de armado ni la del repartidor. Una cancelación del propio request sí se propaga.
            resultado = null;
        }

        cache.Set(clave, resultado, DuracionCache);
        return resultado;
    }

    private async Task<Recorrido?> TrazarOsrmAsync(IReadOnlyList<PuntoRuta> puntos, CancellationToken ct)
    {
        var coordenadas = string.Join(";", puntos.Select(p =>
            $"{p.Lng.ToString(CultureInfo.InvariantCulture)},{p.Lat.ToString(CultureInfo.InvariantCulture)}"));
        // overview=simplified (no "full"): Douglas-Peucker del lado de OSRM. Con 8-25 paradas la
        // geometría baja de miles de puntos a unos pocos cientos sin diferencia visible a zoom de
        // ciudad — importa porque este payload viaja embebido en JornadaDelDia y de ahí a Dexie
        // (fase de cola offline).
        var url = $"route/v1/driving/{coordenadas}?overview=simplified&geometries=geojson";

        var respuesta = await http.GetFromJsonAsync<RespuestaOsrm>(url, ct);
        var mejor = respuesta?.Code == "Ok" ? respuesta.Routes?.FirstOrDefault() : null;
        return mejor is null
            ? null
            : new Recorrido(ALinea(mejor.Geometry), (int)Math.Round(mejor.Distance), (int)Math.Round(mejor.Duration));
    }

    private async Task<Recorrido?> TrazarOrsAsync(IReadOnlyList<PuntoRuta> puntos, CancellationToken ct)
    {
        if (puntos.Count > MaxPuntosOrs) return null;

        using var pedido = new HttpRequestMessage(HttpMethod.Post, "v2/directions/driving-car/geojson")
        {
            Content = JsonContent.Create(new
            {
                coordinates = puntos.Select(p => new[] { (double)p.Lng, (double)p.Lat }),
            }),
        };
        pedido.Headers.TryAddWithoutValidation("Authorization", opciones.Value.ApiKey);

        using var respuesta = await http.SendAsync(pedido, ct);
        // 4xx de ORS (punto fuera de la red de calles, cuota agotada, key inválida): línea recta.
        if (!respuesta.IsSuccessStatusCode) return null;

        return DesdeOrs(await respuesta.Content.ReadFromJsonAsync<RespuestaOrs>(ct));
    }

    /// <summary>Separado para poder probar el mapeo sin red.</summary>
    internal static Recorrido? DesdeOrs(RespuestaOrs? respuesta)
    {
        var feature = respuesta?.Features?.FirstOrDefault();
        if (feature?.Geometry is null || feature.Properties?.Summary is not { } resumen) return null;
        return new Recorrido(ALinea(feature.Geometry), (int)Math.Round(resumen.Distance), (int)Math.Round(resumen.Duration));
    }

    // GeoJSON viene [lng, lat].
    private static List<PuntoRuta> ALinea(Geometria geometria) =>
        geometria.Coordinates.Select(c => new PuntoRuta((decimal)c[1], (decimal)c[0])).ToList();

    /// Redondea a 5 decimales (~1 m de precisión) para que reordenamientos que no cambian
    /// realmente la geometría (o vueltas atrás sobre el mismo conjunto) reusen la misma entrada.
    private static string ClaveCache(IReadOnlyList<PuntoRuta> puntos) =>
        "recorrido:" + string.Join("|", puntos.Select(p =>
            $"{Math.Round(p.Lat, 5)},{Math.Round(p.Lng, 5)}"));
}
