using System.Text.Json.Serialization;

namespace Logistica.Servicios;

public record ResultadoGeocodificacion(decimal? Lat, decimal? Lng, string GeoConfianza, string GeoProveedor);

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
}
