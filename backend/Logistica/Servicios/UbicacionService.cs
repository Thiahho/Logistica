using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Resolver-o-crear: una sola llamada al geocoder por dirección nueva (construccion_v1.md §1).
/// El unique index sobre (lower(calle_numero), localidad_id) es la misma clave que se usa acá
/// para encontrar una ubicación ya geocodificada.
/// </summary>
public record ResultadoUbicacion(Ubicacion Ubicacion, bool UrlMapaAplicada, string? UrlMapaError);

public class UbicacionService(LogisticaDbContext db, GeocodificacionService geocodificador, EnlaceMapaService enlaces)
{
    /// <summary>Caja de la Argentina continental e insular cercana: un punto fuera de acá es un link equivocado.</summary>
    private const decimal LatMin = -56m, LatMax = -21m, LngMin = -74m, LngMax = -53m;

    /// <summary>Un partido grande mide ~50 km de punta a punta; más lejos del centro de la localidad
    /// que esto, el link casi seguro es de otro lugar (o un intento de mover el destino).</summary>
    private const double MaxKmDeLaLocalidad = 50;

    /// <summary>
    /// Como `ResolverOCrearAsync`, pero si llega un link de Google Maps (o "lat, lng") fija el punto
    /// exacto de la dirección (`geo_proveedor = google_maps`, confianza alta) sin pasar por el
    /// geocoder. Nunca tumba el alta: un link inservible, lejano a la localidad o fuera de la
    /// Argentina cae a la geocodificación normal y devuelve el motivo en `UrlMapaError`. Una
    /// ubicación `Verificada` (confirmada por administración) no se mueve.
    /// </summary>
    public async Task<ResultadoUbicacion> ResolverConMapaAsync(
        string calleNumero, int localidadId, string? referencia, string? urlMapa, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlMapa))
            return new ResultadoUbicacion(await ResolverOCrearAsync(calleNumero, localidadId, referencia, ct), false, null);

        var (punto, error) = await PuntoValidoAsync(urlMapa, localidadId, ct);
        if (punto is null)
            return new ResultadoUbicacion(await ResolverOCrearAsync(calleNumero, localidadId, referencia, ct), false, error);

        calleNumero = NormalizarCalle(calleNumero);
        if (calleNumero.Length == 0)
            throw new InvalidOperationException("La calle y número no puede estar vacío.");
        referencia = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim();

        var existente = await db.Ubicaciones.FirstOrDefaultAsync(
            u => u.LocalidadId == localidadId && u.CalleNumero.ToLower() == calleNumero.ToLower(), ct);

        if (existente is not null)
        {
            if (existente.Verificada)
                return new ResultadoUbicacion(existente, false,
                    "Esa dirección ya fue confirmada por la empresa: se mantiene su ubicación.");
            AplicarPunto(existente, punto.Value);
            await db.SaveChangesAsync(ct);
            return new ResultadoUbicacion(existente, true, null);
        }

        var ubicacion = new Ubicacion { CalleNumero = calleNumero, LocalidadId = localidadId, Referencia = referencia };
        AplicarPunto(ubicacion, punto.Value);
        db.Ubicaciones.Add(ubicacion);
        await db.SaveChangesAsync(ct);
        return new ResultadoUbicacion(ubicacion, true, null);
    }

    private async Task<((decimal Lat, decimal Lng)? Punto, string? Error)> PuntoValidoAsync(
        string urlMapa, int localidadId, CancellationToken ct)
    {
        var enlace = await enlaces.ResolverAsync(urlMapa, ct);
        if (!enlace.Ok) return (null, enlace.Error);
        var (lat, lng) = (enlace.Lat!.Value, enlace.Lng!.Value);

        if (!EnArgentina(lat, lng))
            return (null, "El punto del link queda fuera de la Argentina: revisá que sea la dirección correcta.");

        var localidad = await db.Localidades.AsNoTracking().SingleOrDefaultAsync(l => l.Id == localidadId, ct);
        if (localidad?.Lat is { } latLoc && localidad.Lng is { } lngLoc)
        {
            var km = Dominio.Geo.DistanciaMetros(latLoc, lngLoc, lat, lng) / 1000;
            if (km > MaxKmDeLaLocalidad)
                return (null, $"El punto del link queda a {km:0} km de {localidad.Nombre}: revisá que sea la dirección correcta.");
        }
        return ((lat, lng), null);
    }

    public static bool EnArgentina(decimal lat, decimal lng) =>
        lat is >= LatMin and <= LatMax && lng is >= LngMin and <= LngMax;

    private static void AplicarPunto(Ubicacion u, (decimal Lat, decimal Lng) punto)
    {
        u.Lat = punto.Lat;
        u.Lng = punto.Lng;
        u.GeoConfianza = "alta";
        u.GeoProveedor = "google_maps";
        u.GeoFecha = DateTimeOffset.UtcNow;
    }

    public async Task<Ubicacion> ResolverOCrearAsync(
        string calleNumero, int localidadId, string? referencia, CancellationToken ct = default)
    {
        // El unique index (lower(calle_numero), localidad_id) es case-insensitive pero no
        // whitespace-insensitive: sin normalizar acá, " Corrientes  1234 " y "Corrientes 1234"
        // generan dos filas distintas — y con ellas, dos sugerencias de destinatario_frecuente
        // para lo que en la calle es la misma dirección.
        calleNumero = NormalizarCalle(calleNumero);
        if (calleNumero.Length == 0)
            throw new InvalidOperationException("La calle y número no puede estar vacío.");
        referencia = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim();

        var existente = await db.Ubicaciones.FirstOrDefaultAsync(
            u => u.LocalidadId == localidadId && u.CalleNumero.ToLower() == calleNumero.ToLower(), ct);
        if (existente is not null) return existente;

        var localidad = await db.Localidades.FindAsync([localidadId], ct)
            ?? throw new InvalidOperationException($"La localidad {localidadId} no existe.");

        var geo = await geocodificador.GeocodificarAsync(calleNumero, localidad.Nombre, ct);

        var ubicacion = new Ubicacion
        {
            CalleNumero = calleNumero,
            LocalidadId = localidadId,
            Referencia = referencia,
            Lat = geo.Lat,
            Lng = geo.Lng,
            GeoConfianza = geo.GeoConfianza,
            GeoProveedor = geo.GeoProveedor,
            GeoFecha = DateTimeOffset.UtcNow,
        };
        db.Ubicaciones.Add(ubicacion);
        await db.SaveChangesAsync(ct);
        return ubicacion;
    }

    /// <summary>Trim + colapso de espacios repetidos a uno solo. No toca mayúsculas ni acentos:
    /// el match ya es case-insensitive vía ToLower(), y normalizar más que esto (abreviaturas,
    /// unaccent) es una mejora de otra escala, no la que este fix ataca.</summary>
    private static string NormalizarCalle(string calleNumero) =>
        System.Text.RegularExpressions.Regex.Replace(calleNumero.Trim(), @"\s+", " ");
}
