using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;

namespace Logistica.Servicios;

/// <summary>Coordenadas sacadas de un link de Google Maps, o el motivo legible por el que no se pudo.</summary>
public record ResultadoEnlace(decimal? Lat, decimal? Lng, string? Error)
{
    public bool Ok => Lat is not null && Lng is not null;
}

/// <summary>
/// Saca las coordenadas de un link de Google Maps (o de un "lat, lng" pegado) para fijar el punto
/// exacto de una dirección de entrega. Nunca lanza: un link inservible devuelve un error legible y
/// quien llama sigue con la geocodificación normal.
///
/// Los links cortos (maps.app.goo.gl) no traen el punto: hay que seguir el redirect. Se hace a mano,
/// con un máximo de saltos y solo hacia hosts de Google (lista blanca) — el servidor nunca hace un
/// pedido a un host que puso el usuario (SSRF).
/// </summary>
public class EnlaceMapaService(HttpClient http)
{
    private const int MaxSaltos = 4;

    private static readonly HashSet<string> HostsPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "maps.app.goo.gl", "goo.gl",
        "google.com", "www.google.com", "google.com.ar", "www.google.com.ar",
        "maps.google.com", "maps.google.com.ar",
    };

    private const string Numero = @"-?\d{1,3}(?:\.\d+)?";
    private static readonly Regex ParTexto = new($@"^\s*({Numero})\s*[,;]?\s*({Numero})\s*$", RegexOptions.Compiled);
    private static readonly Regex ParPin = new(@"!3d(-?\d{1,3}\.\d+)!4d(-?\d{1,3}\.\d+)", RegexOptions.Compiled);
    private static readonly Regex ParVista = new(@"@(-?\d{1,3}\.\d+),(-?\d{1,3}\.\d+)", RegexOptions.Compiled);
    private static readonly Regex ParConsulta = new($@"^\s*({Numero})\s*,\s*({Numero})", RegexOptions.Compiled);
    private static readonly string[] ParametrosConPunto = ["q", "ll", "query", "destination", "daddr", "center"];

    /// <summary>Pura, sin red. Prefiere el pin (`!3d/!4d`) sobre `@`, que es el centro de la vista
    /// del mapa y no necesariamente el lugar marcado.</summary>
    public static (decimal Lat, decimal Lng)? ExtraerCoordenadas(string texto)
    {
        texto = texto.Trim();

        var m = ParTexto.Match(texto);
        if (m.Success) return Validar(m);

        var decodificado = Uri.UnescapeDataString(texto);

        m = ParPin.Match(decodificado);
        if (m.Success) return Validar(m);

        if (Uri.TryCreate(texto, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            foreach (var clave in ParametrosConPunto)
            {
                var valor = query[clave];
                if (valor is null) continue;
                m = ParConsulta.Match(valor);
                if (m.Success) return Validar(m);
            }
        }

        m = ParVista.Match(decodificado);
        return m.Success ? Validar(m) : null;
    }

    private static (decimal Lat, decimal Lng)? Validar(Match m)
    {
        var lat = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var lng = decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return lat is >= -90 and <= 90 && lng is >= -180 and <= 180 ? (lat, lng) : null;
    }

    public async Task<ResultadoEnlace> ResolverAsync(string texto, CancellationToken ct = default)
    {
        texto = texto.Trim();

        // Un "lat, lng" pegado como texto se acepta tal cual; cualquier otra cosa tiene que ser un
        // link https de Google (antes de interpretar nada: un link de otro sitio no se toca).
        var esUrl = Uri.TryCreate(texto, UriKind.Absolute, out var uri);
        if (esUrl && (uri!.Scheme != Uri.UriSchemeHttps || !HostsPermitidos.Contains(uri.Host)))
            return new ResultadoEnlace(null, null, "El link no es de Google Maps.");

        var directo = ExtraerCoordenadas(texto);
        if (directo is not null) return new ResultadoEnlace(directo.Value.Lat, directo.Value.Lng, null);

        if (!esUrl)
            return new ResultadoEnlace(null, null, "No parece un link de Google Maps. Pegá el link de «Compartir» del lugar.");

        try
        {
            for (var salto = 0; salto < MaxSaltos; salto++)
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Get, uri);
                using var respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);

                var destino = respuesta.Headers.Location;
                if (destino is null) break;
                if (!destino.IsAbsoluteUri) destino = new Uri(uri!, destino);

                var punto = ExtraerCoordenadas(destino.ToString());
                if (punto is not null) return new ResultadoEnlace(punto.Value.Lat, punto.Value.Lng, null);

                // Solo se sigue mientras el redirect siga dentro de Google.
                if (destino.Scheme != Uri.UriSchemeHttps || !HostsPermitidos.Contains(destino.Host)) break;
                uri = destino;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new ResultadoEnlace(null, null, "No pude abrir el link de Google Maps. Probá pegando el link largo o las coordenadas.");
        }

        return new ResultadoEnlace(null, null,
            "No pude sacar el punto del link (parece un lugar sin coordenadas). Abrí el lugar en Google Maps y compartilo de nuevo.");
    }
}
