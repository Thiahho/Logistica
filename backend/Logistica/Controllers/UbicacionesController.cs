using Logistica.Datos;
using Microsoft.AspNetCore.RateLimiting;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

[ApiController]
[Route("api/ubicaciones")]
[Authorize(Policy = "BackOffice")]
public class UbicacionesController(
    UbicacionService ubicaciones, OrigenRutaService origenes, DireccionDesdeMapaService desdeMapa) : ControllerBase
{
    public record ResolverRequest(string CalleNumero, int LocalidadId, string? Referencia, string? UrlMapa = null);
    public record DesdeMapaRequest(string UrlMapa);
    public record UbicacionResuelta(
        long Id, decimal? Lat, decimal? Lng, string? GeoConfianza,
        bool UrlMapaAplicada = false, string? UrlMapaError = null);
    public record CrearDepositoRequest(string Nombre, string CalleNumero, int LocalidadId);
    public record RenombrarDepositoRequest(string Nombre);

    /// <summary>Geocodifica al salir del campo dirección (construccion_v1.md §4.1). Resolver-o-crear:
    /// una dirección ya cargada no vuelve a pegarle al geocoder.</summary>
    [HttpPost]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Resolver(ResolverRequest req, CancellationToken ct)
    {
        var r = await ubicaciones.ResolverConMapaAsync(req.CalleNumero, req.LocalidadId, req.Referencia, req.UrlMapa, ct);
        return Ok(new UbicacionResuelta(
            r.Ubicacion.Id, r.Ubicacion.Lat, r.Ubicacion.Lng, r.Ubicacion.GeoConfianza, r.UrlMapaAplicada, r.UrlMapaError));
    }

    /// <summary>Dirección y localidad propuestas a partir de un link de Google Maps — solo lee, no guarda
    /// nada: la persona las confirma o corrige y recién ahí se resuelve la ubicación (`Resolver`).</summary>
    [HttpPost("desde-mapa")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> DesdeMapa(DesdeMapaRequest req, CancellationToken ct) =>
        Ok(await desdeMapa.LeerAsync(req.UrlMapa, ct));

    /// <summary>Catálogo de depósitos seleccionables al armar una ruta (acta changelog 3.8). Puede
    /// haber varios — a diferencia del resto del controller, de lectura abierta a todo BackOffice
    /// porque operación también arma rutas y necesita elegir de esta lista.</summary>
    [HttpGet("depositos")]
    public async Task<IActionResult> ListarDepositos(CancellationToken ct) => Ok(await origenes.ListarDepositosAsync(ct));

    /// <summary>Alta de un depósito nuevo. Restringido a Administracion, no a todo BackOffice —
    /// es una decisión de la empresa (dónde opera), no del día a día operativo.</summary>
    [HttpPost("depositos")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CrearDeposito(CrearDepositoRequest req, CancellationToken ct)
    {
        try
        {
            return Ok(await origenes.CrearDepositoAsync(req.Nombre, req.CalleNumero, req.LocalidadId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Solo renombra — la dirección de un depósito no se edita en el lugar (mismo criterio
    /// que el resto del sistema): si se mudó, se desactiva este y se carga uno nuevo. Así, una ruta
    /// que ya lo tenía elegido como origen —incluida una cerrada— conserva la dirección exacta que
    /// tenía ese día, sin que un cambio posterior se la reescriba.</summary>
    [HttpPut("depositos/{id:long}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> RenombrarDeposito(long id, RenombrarDepositoRequest req, CancellationToken ct)
    {
        try
        {
            return Ok(await origenes.RenombrarDepositoAsync(id, req.Nombre, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Saca el depósito del catálogo (nunca borra la ubicación): deja de ofrecerse en el
    /// selector de armado, pero cualquier ruta que ya lo tenía elegido —incluidas las cerradas—
    /// sigue resolviendo la misma dirección exacta.</summary>
    [HttpDelete("depositos/{id:long}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> DesactivarDeposito(long id, CancellationToken ct)
    {
        try
        {
            await origenes.DesactivarDepositoAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

[ApiController]
[Route("api/localidades")]
[Authorize(Policy = "BackOffice")]
public class LocalidadesController(
    LogisticaDbContext db, GeocodificacionService geocodificador, ZonaLocalidadService zonasLocalidad) : ControllerBase
{
    public record LocalidadResumen(int Id, string Nombre, string? Partido, int? ZonaId);
    public record CrearLocalidadRequest(string Nombre, string? Partido);
    public record ResultadoBusquedaLocalidad(IReadOnlyList<LocalidadResumen> Existentes, IReadOnlyList<SugerenciaLocalidad> Sugeridas);
    /// <summary>Localidad automática que no pudo quedar con zona. Motivo: "sin_coordenadas" (no se
    /// pudo medir contra el depósito) | "fuera_de_rango" (midió, pero ninguna zona activa la cubre).</summary>
    public record LocalidadPendiente(int Id, string Nombre, string? Partido, decimal? DistanciaKmDeposito, string Motivo);
    public record LocalidadDeZona(int Id, string Nombre, string? Partido, int? ZonaId, string? ZonaCodigo, decimal? DistanciaKmDeposito, bool ZonaManual);
    public record AsignarZonaRequest(int ZonaId);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Localidades.AsNoTracking().OrderBy(l => l.Nombre)
            .Select(l => new { l.Id, l.Nombre, l.Partido, l.ZonaId })
            .ToListAsync(ct));

    /// <summary>Busca localidades mientras se tipea una dirección (acta changelog 3.9): primero en
    /// el catálogo propio (ILIKE), y si el catálogo se queda corto, también en OSM vía
    /// `GeocodificacionService` — el catálogo hoy solo tiene lo que alguien cargó a mano, y una
    /// dirección real en una localidad que no está ahí no tiene forma de entrar sin esto. Las
    /// sugeridas nunca duplican una ya existente (se descartan por nombre, sin distinguir mayúsculas).</summary>
    [HttpGet("buscar")]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Buscar([FromQuery] string q, CancellationToken ct)
    {
        q = q.Trim();
        if (q.Length < 2) return Ok(new ResultadoBusquedaLocalidad([], []));

        // En paralelo: el ILIKE es casi instantáneo, pero Nominatim solo (~1s de ida y vuelta) no
        // tiene por qué duplicarse en serie encima de eso.
        var tareaExistentes = db.Localidades.AsNoTracking()
            .Where(l => EF.Functions.ILike(l.Nombre, $"%{q}%"))
            .OrderBy(l => l.Nombre)
            .Select(l => new LocalidadResumen(l.Id, l.Nombre, l.Partido, l.ZonaId))
            .ToListAsync(ct);
        var tareaSugerencias = geocodificador.BuscarLocalidadesAsync(q, ct);
        await Task.WhenAll(tareaExistentes, tareaSugerencias);

        var existentes = tareaExistentes.Result;
        var nombresExistentes = new HashSet<string>(existentes.Select(l => l.Nombre), StringComparer.OrdinalIgnoreCase);
        var sugeridas = tareaSugerencias.Result
            .Where(s => !nombresExistentes.Contains(s.Nombre))
            .DistinctBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return Ok(new ResultadoBusquedaLocalidad(existentes, sugeridas));
    }

    /// <summary>Resolver-o-crear una localidad a partir de una sugerencia elegida (mismo patrón que
    /// `UbicacionService`). Nace con zona automática: se mide contra el depósito y se le asigna la
    /// zona cuyo rango de km la cubre (acta changelog 4.11). Si no se pudo medir o ninguna zona la
    /// cubre, queda sin zona y aparece en "pendientes" de /tarifas para asignarla a mano.</summary>
    [HttpPost]
    [EnableRateLimiting("geo")]
    public async Task<IActionResult> Crear(CrearLocalidadRequest req, CancellationToken ct)
    {
        var nombre = req.Nombre.Trim();
        if (nombre.Length == 0) return BadRequest("El nombre de la localidad no puede estar vacío.");
        var partido = string.IsNullOrWhiteSpace(req.Partido) ? null : req.Partido.Trim();

        var localidad = await zonasLocalidad.CrearAsync(nombre, partido, ct);
        return Ok(new LocalidadResumen(localidad.Id, localidad.Nombre, localidad.Partido, localidad.ZonaId));
    }

    /// <summary>Localidades automáticas que quedaron sin zona: sin coordenadas (no se pudo medir
    /// contra el depósito) o fuera de todo rango de km. Las que tienen zona —automática o a mano—
    /// no aparecen. Restringido a Administracion: asignar zona fija el precio de la localidad.
    /// Lee lo ya guardado (ZonaLocalidadService) — no mide nada en el request.</summary>
    [HttpGet("pendientes")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var sinZona = await db.Localidades.AsNoTracking()
            .Where(l => l.ZonaId == null)
            .OrderBy(l => l.Nombre)
            .Select(l => new LocalidadPendiente(
                l.Id, l.Nombre, l.Partido, l.DistanciaKmDeposito,
                l.DistanciaKmDeposito == null ? "sin_coordenadas" : "fuera_de_rango"))
            .ToListAsync(ct);
        return Ok(sinZona);
    }

    /// <summary>Todas las localidades con su zona y si fue automática o manual, para revisarlas y
    /// volver a automática las que se corrigieron a mano.</summary>
    [HttpGet("con-zona")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ConZona(CancellationToken ct) =>
        Ok(await db.Localidades.AsNoTracking()
            .OrderBy(l => l.Nombre)
            .Select(l => new LocalidadDeZona(
                l.Id, l.Nombre, l.Partido, l.ZonaId, l.Zona != null ? l.Zona.Codigo : null,
                l.DistanciaKmDeposito, l.ZonaManual))
            .ToListAsync(ct));

    /// <summary>Confirma la zona de una localidad a mano. Queda como manual: ningún recálculo la
    /// pisa hasta que se pida volver a automática.</summary>
    [HttpPut("{id:int}/zona")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> AsignarZona(int id, AsignarZonaRequest req, CancellationToken ct)
    {
        var localidad = await db.Localidades.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (localidad is null) return NotFound();
        var zona = await db.Zonas.SingleOrDefaultAsync(z => z.Id == req.ZonaId, ct);
        if (zona is null) return BadRequest("La zona no existe.");

        localidad.ZonaId = zona.Id;
        localidad.ZonaManual = true;
        await db.SaveChangesAsync(ct);
        return Ok(new LocalidadResumen(localidad.Id, localidad.Nombre, localidad.Partido, localidad.ZonaId));
    }

    /// <summary>Descarta la zona manual y vuelve a medir contra el depósito.</summary>
    [HttpPut("{id:int}/zona/automatica")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> VolverAAutomatica(int id, CancellationToken ct)
    {
        var localidad = await db.Localidades.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (localidad is null) return NotFound();
        await zonasLocalidad.VolverAAutomaticaAsync(localidad, ct);
        return Ok(new LocalidadResumen(localidad.Id, localidad.Nombre, localidad.Partido, localidad.ZonaId));
    }

    /// <summary>Vuelve a medir todas las localidades automáticas contra el depósito principal y
    /// reasigna su zona. Es lo que hay que correr si cambió el depósito, o para completar las
    /// localidades que quedaron sin coordenadas. Lento a propósito (Nominatim ~1 req/s).</summary>
    [HttpPost("recalcular")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Recalcular(CancellationToken ct) =>
        Ok(await zonasLocalidad.RecalcularTodasAsync(soloZona: false, ct));

    /// <summary>Precio orientativo (solo tarifa de zona) al elegir la localidad en el alta interna.
    /// Sin `clienteId` usa la lista general.</summary>
    [HttpGet("{id:int}/precio-sugerido")]
    public async Task<IActionResult> PrecioSugerido(int id, [FromQuery] int? clienteId, CancellationToken ct)
    {
        var precio = await zonasLocalidad.PrecioSugeridoAsync(id, clienteId ?? 0, ct);
        return precio is null ? NotFound() : Ok(precio);
    }
}
