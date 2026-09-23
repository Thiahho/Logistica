using Logistica.Datos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Lo que se pudo leer de un link de Google Maps: el punto y, si hubo suerte, la dirección
/// y la localidad de ese punto. Todo es una propuesta para que la persona la confirme o la corrija —
/// nada de esto se guarda hasta que confirma. `Error` != null = no hay punto utilizable.</summary>
public record DireccionDesdeMapa(
    decimal? Lat, decimal? Lng, string? CalleNumero,
    int? LocalidadId, string? LocalidadNombre, string? Partido, string? Error);

/// <summary>
/// Del link de Google Maps a una dirección propuesta: saca las coordenadas (EnlaceMapaService), las
/// invierte a calle/número/localidad con Nominatim, y busca esa localidad en el catálogo. Si no está,
/// la da de alta con zona automática (mismo camino que elegir una sugerencia en SelectorLocalidad).
/// Servicio aparte de UbicacionService a propósito: ZonaLocalidadService ya depende (vía
/// OrigenRutaService) de UbicacionService, y lo contrario sería una dependencia circular.
/// </summary>
public class DireccionDesdeMapaService(
    LogisticaDbContext db, EnlaceMapaService enlaces, GeocodificacionService geocodificador,
    ZonaLocalidadService zonasLocalidad)
{
    public async Task<DireccionDesdeMapa> LeerAsync(string urlMapa, CancellationToken ct = default)
    {
        var enlace = await enlaces.ResolverAsync(urlMapa, ct);
        if (!enlace.Ok) return Falla(enlace.Error);
        var (lat, lng) = (enlace.Lat!.Value, enlace.Lng!.Value);

        if (!UbicacionService.EnArgentina(lat, lng))
            return Falla("El punto del link queda fuera de la Argentina: revisá que sea la dirección correcta.");

        var inversa = await geocodificador.InvertirAsync(lat, lng, ct);
        if (inversa is null) return new DireccionDesdeMapa(lat, lng, null, null, null, null, null);

        var calleNumero = string.IsNullOrWhiteSpace(inversa.Calle)
            ? null
            : $"{inversa.Calle} {inversa.Numero}".Trim();

        // La primera candidata que ya esté en el catálogo (sin distinguir mayúsculas). La Ciudad de
        // Buenos Aires figura en el catálogo como "CABA" pero OSM la nombra "Buenos Aires": sin este
        // alias cada link porteño daba de alta una localidad duplicada.
        var candidatas = inversa.Localidades.Select(n => EsCiudadDeBuenosAires(n) ? "CABA" : n);
        foreach (var nombre in candidatas)
        {
            var existente = await db.Localidades.AsNoTracking()
                .Where(l => EF.Functions.ILike(l.Nombre, nombre))
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
                return new DireccionDesdeMapa(lat, lng, calleNumero, existente.Id, existente.Nombre, existente.Partido, null);
        }

        // Ninguna está: se da de alta la más específica que Nominatim nombra como ciudad/pueblo.
        var nueva = candidatas.FirstOrDefault();
        if (nueva is null) return new DireccionDesdeMapa(lat, lng, calleNumero, null, null, inversa.Partido, null);

        var creada = await zonasLocalidad.CrearAsync(nueva, inversa.Partido, ct);
        return new DireccionDesdeMapa(lat, lng, calleNumero, creada.Id, creada.Nombre, creada.Partido, null);
    }

    private static bool EsCiudadDeBuenosAires(string nombre) =>
        nombre.Trim().ToLowerInvariant() is "buenos aires" or "ciudad autónoma de buenos aires" or "ciudad autonoma de buenos aires";

    private static DireccionDesdeMapa Falla(string? error) =>
        new(null, null, null, null, null, null, error ?? "No pude leer el link.");
}
