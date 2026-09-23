using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Disponibilidad y carga de trabajo por repartidor (RF-34, acta changelog 4.6). Deriva todo de
/// usuarios/rutas/ruta_paradas, sin tabla propia: el acta §4 mantiene `repartidores` fuera del
/// modelo inicial y ahí sigue — un repartidor es `usuarios.rol='repartidor'`, no una entidad.
///
/// Lo que agrega sobre /jornada: ese monitor parte de las rutas del día y filtra las que tienen
/// repartidor (`JornadaController.Resumen`), con lo cual el repartidor SIN ruta asignada no
/// aparece en ninguna pantalla — y es justamente el que hace falta ver para decidir a quién
/// asignar. Acá la query arranca de `usuarios` y la ruta se cuelga si existe.
///
/// NO es el tablero de indicadores (B2/E3, Anexo I §5, sigue fuera de alcance): no calcula
/// ninguna de sus 10 métricas (entregas/día, km, tiempo, margen, NPS, ocupación de flota), no
/// compara períodos entre sí ni arma series — el acumulado del rango es una sumatoria de paradas
/// recalculada en cada request, sin persistir en ningún lado. Existe para responder "¿a quién le
/// cargo la ruta de mañana?", no para medir rendimiento. Mismo encuadre declarado que /jornada
/// (changelog 4.4) y /cobranza (changelog 4.3).
///
/// Tampoco es liquidación al repartidor (B4/E2, disparador "segundo repartidor" según acta §9.2,
/// bloqueada por la definición abierta §10.2-C del Anexo): no toca `rutas.pago_repartidor` ni
/// calcula ni expone un solo importe.
///
/// [Authorize] a nivel de clase, a diferencia de UsuariosController/ClientesController/
/// VehiculosController: esos lo omiten porque mezclan policies por acción y ASP.NET combina
/// clase+acción con AND, no reemplaza. Acá las dos acciones son consulta operativa para quien
/// planifica, ambas BackOffice — mismo caso que JornadaController, se declara una vez.
/// </summary>
[ApiController]
[Route("api/repartidores")]
[Authorize(Policy = "BackOffice")]
public class RepartidoresController(LogisticaDbContext db) : ControllerBase
{
    /// <summary>Estados de disponibilidad. snake_case como `Ruta.Estado`/`RutaParada.Estado`, que
    /// también son string libre validado en app, no enum de Postgres.</summary>
    private static class Disponibilidades
    {
        public const string Inactivo = "inactivo";
        public const string EnRuta = "en_ruta";
        public const string Asignado = "asignado";
        public const string Libre = "libre";
    }

    public record RepartidorListado(
        Guid Id, string Nombre, bool Activo, string Disponibilidad,
        long? RutaHoyId, string? RutaHoyEstado, string? VehiculoHoyPatente,
        int ParadasHoy, int CompletadasHoy, int FallidasHoy, int PendientesHoy,
        int RutasRango, int ParadasRango, int CompletadasRango, int FallidasRango);

    public record RutaDelRango(
        long RutaId, DateOnly Fecha, string Estado, string? VehiculoPatente,
        int Paradas, int Completadas, int Fallidas, int Pendientes);

    public record RepartidorDetalle(
        Guid Id, string Nombre, string Email, bool Activo, string Disponibilidad,
        DateOnly Desde, DateOnly Hasta,
        int RutasRango, int ParadasRango, int CompletadasRango, int FallidasRango,
        List<RutaDelRango> Rutas);

    /// <summary>Rango por default: últimos 7 días incluyendo hoy. Mismo criterio que
    /// JornadaController.Resumen defaulteando a hoy — la pantalla abre mostrando algo útil sin
    /// obligar a elegir fechas.</summary>
    private static (DateOnly desde, DateOnly hasta) ResolverRango(DateOnly? desde, DateOnly? hasta, DateOnly hoy)
    {
        var hastaValor = hasta ?? hoy;
        var desdeValor = desde ?? hastaValor.AddDays(-6);
        return (desdeValor, hastaValor);
    }

    /// <summary>
    /// Disponibilidad de HOY, nunca del rango: es un estado del presente, el rango solo gobierna
    /// el acumulado. Precedencia fija — inactivo gana sobre cualquier ruta asignada (un usuario
    /// dado de baja no está "en ruta" aunque una ruta vieja lo siga apuntando).
    ///
    /// No hay ausencias declaradas (franco, vacaciones, licencia): no se registran en ninguna
    /// tabla y guardarlas es lo único que justificaría la tabla `repartidores` que el acta §4
    /// mantiene afuera. Costo asumido y documentado: un repartidor de vacaciones figura `libre`.
    /// </summary>
    private static string ResolverDisponibilidad(bool activo, string? estadoRutaHoy) =>
        !activo ? Disponibilidades.Inactivo
        : estadoRutaHoy == "en_curso" ? Disponibilidades.EnRuta
        : estadoRutaHoy == "planificada" ? Disponibilidades.Asignado
        : Disponibilidades.Libre;

    /// <summary>
    /// Cinco queries planas y composición en memoria, mismo patrón anti-N+1 que
    /// JornadaController.Resumen: usuarios repartidor, rutas de hoy, paradas de hoy agrupadas por
    /// ruta, rutas del rango contadas por repartidor, y paradas del rango agrupadas por repartidor.
    /// Round-trips constantes: no crecen con la cantidad de repartidores ni de rutas.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var (desdeValor, hastaValor) = ResolverRango(desde, hasta, hoy);
        if (desdeValor > hastaValor) return BadRequest("El rango de fechas está invertido: 'desde' es posterior a 'hasta'.");

        // Incluye inactivos a propósito: un repartidor dado de baja tiene que seguir figurando con
        // su badge, si no desaparece de la pantalla sin que se entienda por qué.
        var repartidores = await db.Usuarios.AsNoTracking()
            .Where(u => u.Rol == Roles.Repartidor)
            .OrderBy(u => u.Nombre)
            .Select(u => new { u.Id, u.Nombre, u.Activo })
            .ToListAsync(ct);

        var rutasHoy = await db.Rutas.AsNoTracking()
            .Where(r => r.Fecha == hoy && r.RepartidorId != null)
            .Select(r => new
            {
                r.Id,
                RepartidorId = r.RepartidorId!.Value,
                r.Estado,
                VehiculoPatente = r.Vehiculo != null ? r.Vehiculo.Patente : null,
            })
            .ToListAsync(ct);

        var paradasPorRutaHoy = await db.RutaParadas.AsNoTracking()
            .Where(rp => rp.Ruta.Fecha == hoy && rp.Ruta.RepartidorId != null)
            .GroupBy(rp => rp.RutaId)
            .Select(g => new
            {
                RutaId = g.Key,
                Total = g.Count(),
                Pendientes = g.Count(p => p.Estado == "pendiente"),
                Completadas = g.Count(p => p.Estado == "completada"),
                Fallidas = g.Count(p => p.Estado == "fallida"),
            })
            .ToDictionaryAsync(x => x.RutaId, ct);

        // Las rutas del rango se cuentan contra `rutas`, no contra el GroupBy de paradas: una ruta
        // `planificada` sin paradas cargadas todavía no aparece en ruta_paradas, pero es una ruta
        // asignada igual y el detalle la lista. Contarla desde las paradas daba un total menor que
        // el del detalle para el mismo rango.
        var rutasDelRango = await db.Rutas.AsNoTracking()
            .Where(r => r.RepartidorId != null && r.Fecha >= desdeValor && r.Fecha <= hastaValor)
            .GroupBy(r => r.RepartidorId!.Value)
            .Select(g => new { RepartidorId = g.Key, Rutas = g.Count() })
            .ToDictionaryAsync(x => x.RepartidorId, x => x.Rutas, ct);

        var acumulado = await AcumuladoPorRepartidorAsync(desdeValor, hastaValor, ct);

        // Una segunda ruta el mismo día no da error en ningún lado (RutasController.Actualizar no
        // lo impide para `planificada`; ReasignarRepartidor solo valida duplicados de `en_curso`).
        // Se elige la más relevante: en_curso > planificada, y a igualdad la de Id mayor.
        var rutaHoyPorRepartidor = rutasHoy
            .GroupBy(r => r.RepartidorId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Estado == "en_curso")
                      .ThenByDescending(r => r.Id)
                      .First());

        var items = repartidores.Select(u =>
        {
            rutaHoyPorRepartidor.TryGetValue(u.Id, out var rutaHoy);
            var paradas = rutaHoy is not null && paradasPorRutaHoy.TryGetValue(rutaHoy.Id, out var p) ? p : null;
            acumulado.TryGetValue(u.Id, out var acum);
            rutasDelRango.TryGetValue(u.Id, out var rutasRango);

            return new RepartidorListado(
                u.Id, u.Nombre, u.Activo,
                ResolverDisponibilidad(u.Activo, rutaHoy?.Estado),
                rutaHoy?.Id, rutaHoy?.Estado, rutaHoy?.VehiculoPatente,
                paradas?.Total ?? 0, paradas?.Completadas ?? 0, paradas?.Fallidas ?? 0, paradas?.Pendientes ?? 0,
                rutasRango, acum?.Total ?? 0, acum?.Completadas ?? 0, acum?.Fallidas ?? 0);
        }).ToList();

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalle(
        Guid id, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var (desdeValor, hastaValor) = ResolverRango(desde, hasta, hoy);
        if (desdeValor > hastaValor) return BadRequest("El rango de fechas está invertido: 'desde' es posterior a 'hasta'.");

        // 404 también si el usuario existe pero no es repartidor: /repartidores/{id} no es un
        // atajo para espiar la ficha de un administrativo.
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Rol == Roles.Repartidor)
            .Select(u => new { u.Id, u.Nombre, u.Email, u.Activo })
            .SingleOrDefaultAsync(ct);
        if (usuario is null) return NotFound();

        var rutas = await db.Rutas.AsNoTracking()
            .Where(r => r.RepartidorId == id && r.Fecha >= desdeValor && r.Fecha <= hastaValor)
            .OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Fecha,
                r.Estado,
                VehiculoPatente = r.Vehiculo != null ? r.Vehiculo.Patente : null,
            })
            .ToListAsync(ct);

        var rutaIds = rutas.Select(r => r.Id).ToList();

        var paradasPorRuta = await db.RutaParadas.AsNoTracking()
            .Where(rp => rutaIds.Contains(rp.RutaId))
            .GroupBy(rp => rp.RutaId)
            .Select(g => new
            {
                RutaId = g.Key,
                Total = g.Count(),
                Pendientes = g.Count(p => p.Estado == "pendiente"),
                Completadas = g.Count(p => p.Estado == "completada"),
                Fallidas = g.Count(p => p.Estado == "fallida"),
            })
            .ToDictionaryAsync(x => x.RutaId, ct);

        // La disponibilidad es siempre la de hoy. Si el rango pedido incluye hoy ya lo trajimos
        // arriba; si no (ej. "mes pasado"), cuesta una query extra acotada — no se puede derivar
        // del rango sin mentir.
        string? estadoRutaHoy;
        if (hoy >= desdeValor && hoy <= hastaValor)
        {
            estadoRutaHoy = rutas
                .Where(r => r.Fecha == hoy)
                .OrderByDescending(r => r.Estado == "en_curso").ThenByDescending(r => r.Id)
                .Select(r => r.Estado)
                .FirstOrDefault();
        }
        else
        {
            estadoRutaHoy = await db.Rutas.AsNoTracking()
                .Where(r => r.RepartidorId == id && r.Fecha == hoy)
                .OrderByDescending(r => r.Estado == "en_curso").ThenByDescending(r => r.Id)
                .Select(r => r.Estado)
                .FirstOrDefaultAsync(ct);
        }

        var rutasDelRango = rutas.Select(r =>
        {
            paradasPorRuta.TryGetValue(r.Id, out var p);
            return new RutaDelRango(
                r.Id, r.Fecha, r.Estado, r.VehiculoPatente,
                p?.Total ?? 0, p?.Completadas ?? 0, p?.Fallidas ?? 0, p?.Pendientes ?? 0);
        }).ToList();

        return Ok(new RepartidorDetalle(
            usuario.Id, usuario.Nombre, usuario.Email, usuario.Activo,
            ResolverDisponibilidad(usuario.Activo, estadoRutaHoy),
            desdeValor, hastaValor,
            rutasDelRango.Count,
            rutasDelRango.Sum(r => r.Paradas),
            rutasDelRango.Sum(r => r.Completadas),
            rutasDelRango.Sum(r => r.Fallidas),
            rutasDelRango));
    }

    private record Acumulado(int Total, int Completadas, int Fallidas);

    /// <summary>Paradas del rango agrupadas por repartidor, atravesando `rp.Ruta`. Una sola query
    /// para todos los repartidores — el N+1 acá sería una query por persona.</summary>
    private async Task<Dictionary<Guid, Acumulado>> AcumuladoPorRepartidorAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct) =>
        await db.RutaParadas.AsNoTracking()
            .Where(rp => rp.Ruta.RepartidorId != null
                && rp.Ruta.Fecha >= desde && rp.Ruta.Fecha <= hasta)
            .GroupBy(rp => rp.Ruta.RepartidorId!.Value)
            .Select(g => new
            {
                RepartidorId = g.Key,
                Acumulado = new Acumulado(
                    g.Count(),
                    g.Count(p => p.Estado == "completada"),
                    g.Count(p => p.Estado == "fallida")),
            })
            .ToDictionaryAsync(x => x.RepartidorId, x => x.Acumulado, ct);
}
