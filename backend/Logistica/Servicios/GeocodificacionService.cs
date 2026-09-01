using System.Text.Json.Serialization;

namespace Logistica.Servicios;

public record ResultadoGeocodificacion(decimal? Lat, decimal? Lng, string GeoConfianza, string GeoProveedor);

/// <summary>Localidad real (según OSM) que todavía no está en el catálogo propio — candidata a
/// alta automática mientras se tipea una dirección (acta changelog 3.9).</summary>
public record SugerenciaLocalidad(string Nombre, string? Partido);

/// <summary>
/// construccion_v1.md §1: geocodificación solo desde el servidor. Usa Nominatim (OSM) en vez de
/// Google Geocoding API: no requiere key ni facturación, suficiente para desarrollo. El campo
/// geo_proveedor en `ubicaciones` ya distingue el proveedor, así que cambiarlo después no toca
/// el resto del sistema (decidido con el usuario).
/// </summary>
public class GeocodificacionService(HttpClient http)
{
    private record ResultadoNominatim(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("address")] DireccionNominatim? Address);

    private record DireccionNominatim(
        [property: JsonPropertyName("house_number")] string? HouseNumber,
        [property: JsonPropertyName("road")] string? Road);

    private record ResultadoLugarNominatim(
        [property: JsonPropertyName("address")] DireccionLugarNominatim? Address);

    private record DireccionLugarNominatim(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("state")] string? State);

    public async Task<ResultadoGeocodificacion> GeocodificarAsync(
        string calleNumero, string localidadNombre, CancellationToken ct = default)
    {
        var query = $"{calleNumero}, {localidadNombre}, Argentina";
        var url = $"search?format=jsonv2&addressdetails=1&limit=1&q={Uri.EscapeDataString(query)}";

        List<ResultadoNominatim>? resultados;
        try
        {
            resultados = await http.GetFromJsonAsync<List<ResultadoNominatim>>(url, ct);
        }
        catch (HttpRequestException)
        {
            return new ResultadoGeocodificacion(null, null, "fallida", "nominatim");
        }

        var primero = resultados?.FirstOrDefault();
        if (primero is null)
            return new ResultadoGeocodificacion(null, null, "fallida", "nominatim");

        var confianza = primero.Address?.HouseNumber is not null
            ? "alta"
            : primero.Address?.Road is not null
                ? "media"
                : "baja";

        return new ResultadoGeocodificacion(
            decimal.Parse(primero.Lat, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(primero.Lon, System.Globalization.CultureInfo.InvariantCulture),
            confianza,
            "nominatim");
    }

    /// <summary>Busca localidades reales por texto parcial (para sugerir mientras se tipea, más
    /// allá de lo que ya haya en el catálogo propio). `featureType=settlement` restringe los
    /// resultados a ciudades/pueblos/parajes en vez de calles o comercios — sin esto, buscar
    /// "Tigre" también trae rutas y negocios con ese nombre.</summary>
    public async Task<IReadOnlyList<SugerenciaLocalidad>> BuscarLocalidadesAsync(string texto, CancellationToken ct = default)
    {
        var url = $"search?format=jsonv2&addressdetails=1&limit=8&countrycodes=ar&featureType=settlement&q={Uri.EscapeDataString(texto)}";

        List<ResultadoLugarNominatim>? resultados;
        try
        {
            resultados = await http.GetFromJsonAsync<List<ResultadoLugarNominatim>>(url, ct);
        }
        catch (HttpRequestException)
        {
            return [];
        }
        if (resultados is null) return [];

        var sugerencias = new List<SugerenciaLocalidad>();
        foreach (var r in resultados)
        {
            var nombre = r.Address?.City ?? r.Address?.Town ?? r.Address?.Village ?? r.Address?.Municipality;
            if (string.IsNullOrWhiteSpace(nombre)) continue;
            sugerencias.Add(new SugerenciaLocalidad(nombre, r.Address?.County ?? r.Address?.State));
        }
        return sugerencias;
    }
}
