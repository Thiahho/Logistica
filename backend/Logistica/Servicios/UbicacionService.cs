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
