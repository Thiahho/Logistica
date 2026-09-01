using Logistica.Datos;
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
public class UbicacionesController(UbicacionService ubicaciones, OrigenRutaService origenes) : ControllerBase
{
    public record ResolverRequest(string CalleNumero, int LocalidadId, string? Referencia);
    public record UbicacionResuelta(long Id, decimal? Lat, decimal? Lng, string? GeoConfianza);
    public record CrearDepositoRequest(string Nombre, string CalleNumero, int LocalidadId);
    public record RenombrarDepositoRequest(string Nombre);

    /// <summary>Geocodifica al salir del campo dirección (construccion_v1.md §4.1). Resolver-o-crear:
    /// una dirección ya cargada no vuelve a pegarle al geocoder.</summary>
    [HttpPost]
    public async Task<IActionResult> Resolver(ResolverRequest req, CancellationToken ct)
    {
        var ubicacion = await ubicaciones.ResolverOCrearAsync(req.CalleNumero, req.LocalidadId, req.Referencia, ct);
        return Ok(new UbicacionResuelta(ubicacion.Id, ubicacion.Lat, ubicacion.Lng, ubicacion.GeoConfianza));
    }

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
public class LocalidadesController(LogisticaDbContext db, GeocodificacionService geocodificador, OrigenRutaService origenes) : ControllerBase
{
    public record LocalidadResumen(int Id, string Nombre, string? Partido, int? ZonaId);
    public record CrearLocalidadRequest(string Nombre, string? Partido);
    public record ResultadoBusquedaLocalidad(IReadOnlyList<LocalidadResumen> Existentes, IReadOnlyList<SugerenciaLocalidad> Sugeridas);
    public record LocalidadPendiente(int Id, string Nombre, string? Partido, decimal? DistanciaKmDeposito, int? ZonaSugeridaId, string? ZonaSugeridaCodigo, string? ZonaSugeridaNombre);
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
    /// `UbicacionService`). Siempre nace sin zona (`ZonaId = null`): queda disponible al instante
    /// para depósitos y direcciones, pero un pedido ahí sigue bloqueado en /pedidos hasta que
    /// administración le asigne zona a mano — la decisión de precio nunca se infiere sola.</summary>
    [HttpPost]
    public async Task<IActionResult> Crear(CrearLocalidadRequest req, CancellationToken ct)
    {
        var nombre = req.Nombre.Trim();
        if (nombre.Length == 0) return BadRequest("El nombre de la localidad no puede estar vacío.");
        var partido = string.IsNullOrWhiteSpace(req.Partido) ? null : req.Partido.Trim();

        var existente = await db.Localidades.FirstOrDefaultAsync(l =>
            l.Nombre.ToLower() == nombre.ToLower() &&
            (partido == null ? l.Partido == null : l.Partido!.ToLower() == partido.ToLower()), ct);
        if (existente is not null)
            return Ok(new LocalidadResumen(existente.Id, existente.Nombre, existente.Partido, existente.ZonaId));

        var nueva = new Localidad { Nombre = nombre, Partido = partido, ZonaId = null };
        db.Localidades.Add(nueva);
        await db.SaveChangesAsync(ct);
        return Ok(new LocalidadResumen(nueva.Id, nueva.Nombre, nueva.Partido, nueva.ZonaId));
    }

    /// <summary>Localidades sin zona (ninguna pantalla las creaba antes de acta changelog 3.9;
    /// ahora el buscador de direcciones sí, y quedan bloqueadas para cotizar hasta que alguien
    /// pase por acá). Restringido a Administracion, no a todo BackOffice — asignar zona fija el
    /// precio de la localidad, misma decisión de empresa que el alta de un depósito.
    ///
    /// La zona sugerida es eso, una sugerencia: se calcula la distancia real (haversine,
    /// `Dominio.Geo`, mismo cálculo que ya usa la prueba de entrega) desde el depósito que usa el
    /// alta de pedido hoy (`PrincipalParaPedidosAsync`) hasta una dirección ya geocodificada de
    /// esa localidad, y se busca qué zona activa la cubre según el rango de km que administración
    /// ya cargó en /tarifas (`zonas.km_desde/km_hasta`) — dato que existía desde antes y no se
    /// usaba para nada. Nunca se aplica sola: `AsignarZona` exige que alguien la confirme.</summary>
    [HttpGet("pendientes")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var sinZona = await db.Localidades.AsNoTracking()
            .Where(l => l.ZonaId == null)
            .OrderBy(l => l.Nombre)
            .ToListAsync(ct);
        if (sinZona.Count == 0) return Ok(Array.Empty<LocalidadPendiente>());

        // Sin depósito cargado no hay desde dónde medir distancia — la pantalla igual tiene que
        // poder listar las localidades pendientes para asignarlas a mano.
        Deposito? deposito;
        try
        {
            deposito = await origenes.PrincipalParaPedidosAsync(ct);
        }
        catch (InvalidOperationException)
        {
            deposito = null;
        }

        var zonas = deposito?.Lat is null || deposito?.Lng is null
            ? []
            : await db.Zonas.AsNoTracking()
                .Where(z => z.Activa && z.KmDesde != null)
                .OrderBy(z => z.KmDesde)
                .ToListAsync(ct);

        var resultado = new List<LocalidadPendiente>();
        foreach (var l in sinZona)
        {
            decimal? distanciaKm = null;
            int? zonaSugeridaId = null;
            string? zonaSugeridaCodigo = null;
            string? zonaSugeridaNombre = null;

            if (deposito?.Lat is not null && deposito.Lng is not null)
            {
                var referencia = await db.Ubicaciones.AsNoTracking()
                    .Where(u => u.LocalidadId == l.Id && u.Lat != null && u.Lng != null)
                    .OrderByDescending(u => u.Id)
                    .FirstOrDefaultAsync(ct);
                if (referencia is not null)
                {
                    var metros = Geo.DistanciaMetros(deposito.Lat.Value, deposito.Lng.Value, referencia.Lat!.Value, referencia.Lng!.Value);
                    distanciaKm = Math.Round(metros / 1000m, 1);
                    var zona = zonas.FirstOrDefault(z => distanciaKm >= z.KmDesde! && (z.KmHasta == null || distanciaKm <= z.KmHasta));
                    if (zona is not null)
                    {
                        zonaSugeridaId = zona.Id;
                        zonaSugeridaCodigo = zona.Codigo;
                        zonaSugeridaNombre = zona.Nombre;
                    }
                }
            }
            resultado.Add(new LocalidadPendiente(l.Id, l.Nombre, l.Partido, distanciaKm, zonaSugeridaId, zonaSugeridaCodigo, zonaSugeridaNombre));
        }
        return Ok(resultado);
    }

    /// <summary>Confirma la zona de una localidad (sugerida o elegida a mano) — la única forma de
    /// sacarla de "pendientes". No es cotizar en el momento: pedidos futuros ahí recién cotizan
    /// una vez asignada.</summary>
    [HttpPut("{id:int}/zona")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> AsignarZona(int id, AsignarZonaRequest req, CancellationToken ct)
    {
        var localidad = await db.Localidades.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (localidad is null) return NotFound();
        var zona = await db.Zonas.SingleOrDefaultAsync(z => z.Id == req.ZonaId, ct);
        if (zona is null) return BadRequest("La zona no existe.");

        localidad.ZonaId = zona.Id;
        await db.SaveChangesAsync(ct);
        return Ok(new LocalidadResumen(localidad.Id, localidad.Nombre, localidad.Partido, localidad.ZonaId));
    }
}
