using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Origen resuelto de una ruta — el depósito elegido o cualquier otra dirección. Puede
/// no ser un depósito del catálogo (`EsDeposito=false`, `NombreDeposito=null`): el planificador
/// también puede tipear una dirección puntual ("otra dirección" — la camioneta quedó ahí).</summary>
public record OrigenRuta(
    long UbicacionId, string CalleNumero, string? Localidad, int? LocalidadId,
    decimal? Lat, decimal? Lng, string? GeoConfianza, bool EsDeposito, string? NombreDeposito);

/// <summary>Un depósito del catálogo (acta changelog 3.8): una Ubicacion con NombreDeposito no
/// nulo. Puede haber varios — el planificador elige cuál al armar cada ruta.</summary>
public record Deposito(
    long UbicacionId, string Nombre, string CalleNumero, string? Localidad, int? LocalidadId,
    decimal? Lat, decimal? Lng, string? GeoConfianza);

/// <summary>
/// Origen de una ruta y catálogo de depósitos, resueltos en un solo lugar (acta changelog 3.6,
/// 3.7, 3.8). Ya no hay un único "el depósito": el planificador siempre elige a mano de un
/// catálogo (o tipea otra dirección) al armar — decisión tomada con el usuario. `Ruta.CerrarPlanificacion`
/// exige que el origen esté elegido antes de salir a la calle (RF-17); por eso una ruta `en_curso`
/// o `cerrada` siempre tiene un origen resuelto, nunca null.
/// </summary>
public class OrigenRutaService(LogisticaDbContext db, UbicacionService ubicaciones)
{
    /// <summary>Origen ya elegido para esta ruta, o null si el planificador todavía no eligió
    /// nada (solo posible mientras la ruta sigue "planificada" — ver CerrarPlanificacion).</summary>
    public async Task<OrigenRuta?> ResolverAsync(Ruta ruta, CancellationToken ct = default) =>
        ruta.OrigenUbicacionId is null ? null : await DeUbicacionAsync(ruta.OrigenUbicacionId.Value, ct);

    private async Task<OrigenRuta> DeUbicacionAsync(long ubicacionId, CancellationToken ct)
    {
        var u = await Consulta().SingleOrDefaultAsync(x => x.Id == ubicacionId, ct)
            ?? throw new InvalidOperationException($"La ubicación {ubicacionId} no existe.");
        return Mapear(u);
    }

    /// <summary>Origen por defecto de un pedido nuevo (el alta de pedido no elige depósito
    /// explícito todavía — fuera de este alcance, ver Fase 2 del retiro por pedido). Determinístico
    /// sin necesitar un flag "principal" separado: el depósito más antiguo del catálogo. Si se
    /// desactiva, el siguiente más antiguo pasa a serlo — sin intervención manual.</summary>
    public async Task<Deposito> PrincipalParaPedidosAsync(CancellationToken ct = default)
    {
        var u = await ConsultaDepositos().OrderBy(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No hay ningún depósito cargado. Cargá al menos uno en /depositos.");
        return MapearDeposito(u);
    }

    public async Task<IReadOnlyList<Deposito>> ListarDepositosAsync(CancellationToken ct = default) =>
        await ConsultaDepositos().OrderBy(u => u.NombreDeposito)
            .Select(u => new Deposito(u.Id, u.NombreDeposito!, u.CalleNumero,
                u.Localidad != null ? u.Localidad.Nombre : null, u.LocalidadId, u.Lat, u.Lng, u.GeoConfianza))
            .ToListAsync(ct);

    /// <summary>Resolver-o-crear la dirección (mismo camino que el destino de un pedido) y
    /// promoverla a depósito con este nombre. Si la ubicación ya era un depósito con otro nombre,
    /// es un error de uso (renombrar es una acción separada), no un renombre implícito.</summary>
    public async Task<Deposito> CrearDepositoAsync(string nombre, string calleNumero, int localidadId, CancellationToken ct = default)
    {
        nombre = nombre.Trim();
        if (nombre.Length == 0) throw new InvalidOperationException("El nombre del depósito no puede estar vacío.");
        if (await db.Ubicaciones.AnyAsync(u => u.NombreDeposito == nombre, ct))
            throw new InvalidOperationException($"Ya existe un depósito llamado \"{nombre}\".");

        var resuelta = await ubicaciones.ResolverOCrearAsync(calleNumero, localidadId, referencia: null, ct);
        if (resuelta.NombreDeposito is not null)
            throw new InvalidOperationException($"Esa dirección ya es el depósito \"{resuelta.NombreDeposito}\".");

        resuelta.NombreDeposito = nombre;
        await db.SaveChangesAsync(ct);

        var conLocalidad = await ConsultaDepositos().SingleAsync(u => u.Id == resuelta.Id, ct);
        return MapearDeposito(conLocalidad);
    }

    public async Task<Deposito> RenombrarDepositoAsync(long ubicacionId, string nuevoNombre, CancellationToken ct = default)
    {
        nuevoNombre = nuevoNombre.Trim();
        if (nuevoNombre.Length == 0) throw new InvalidOperationException("El nombre del depósito no puede estar vacío.");

        var u = await db.Ubicaciones.SingleOrDefaultAsync(x => x.Id == ubicacionId, ct)
            ?? throw new InvalidOperationException($"La ubicación {ubicacionId} no existe.");
        if (u.NombreDeposito is null) throw new InvalidOperationException("Esa ubicación no es un depósito.");

        if (u.NombreDeposito != nuevoNombre && await db.Ubicaciones.AnyAsync(x => x.NombreDeposito == nuevoNombre, ct))
            throw new InvalidOperationException($"Ya existe un depósito llamado \"{nuevoNombre}\".");

        u.NombreDeposito = nuevoNombre;
        await db.SaveChangesAsync(ct);

        var conLocalidad = await ConsultaDepositos().SingleAsync(x => x.Id == ubicacionId, ct);
        return MapearDeposito(conLocalidad);
    }

    /// <summary>Saca la ubicación del catálogo (nunca borra la fila): las rutas que ya la tenían
    /// elegida como origen —incluidas las cerradas— la conservan intacta, solo deja de aparecer
    /// como opción para armados nuevos.</summary>
    public async Task DesactivarDepositoAsync(long ubicacionId, CancellationToken ct = default)
    {
        var u = await db.Ubicaciones.SingleOrDefaultAsync(x => x.Id == ubicacionId, ct)
            ?? throw new InvalidOperationException($"La ubicación {ubicacionId} no existe.");
        if (u.NombreDeposito is null) throw new InvalidOperationException("Esa ubicación no es un depósito.");
        u.NombreDeposito = null;
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Ubicacion> Consulta() => db.Ubicaciones.AsNoTracking().Include(u => u.Localidad);
    private IQueryable<Ubicacion> ConsultaDepositos() => Consulta().Where(u => u.NombreDeposito != null);

    private static OrigenRuta Mapear(Ubicacion u) => new(
        u.Id, u.CalleNumero, u.Localidad?.Nombre, u.LocalidadId, u.Lat, u.Lng, u.GeoConfianza,
        EsDeposito: u.NombreDeposito is not null, u.NombreDeposito);

    private static Deposito MapearDeposito(Ubicacion u) =>
        new(u.Id, u.NombreDeposito!, u.CalleNumero, u.Localidad?.Nombre, u.LocalidadId, u.Lat, u.Lng, u.GeoConfianza);

    /// <summary>Punto para trazar el recorrido, o null si el origen no está geocodificado — mismo
    /// criterio que las paradas sin coordenada: se degrada, no se inventa posición.</summary>
    public static PuntoRuta? Punto(OrigenRuta origen) =>
        origen.Lat is { } lat && origen.Lng is { } lng ? new PuntoRuta(lat, lng) : null;
}
