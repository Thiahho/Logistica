using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Resolver-o-crear: una sola llamada al geocoder por dirección nueva (construccion_v1.md §1).
/// El unique index sobre (lower(calle_numero), localidad_id) es la misma clave que se usa acá
/// para encontrar una ubicación ya geocodificada.
/// </summary>
public class UbicacionService(LogisticaDbContext db, GeocodificacionService geocodificador)
{
    public async Task<Ubicacion> ResolverOCrearAsync(
        string calleNumero, int localidadId, string? referencia, CancellationToken ct = default)
    {
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
}
