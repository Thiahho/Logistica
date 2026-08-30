using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// ABM de la flota. Reemplaza el texto libre que traía rutas.vehiculo por un catálogo con patente
/// única y ficha propia (vencimientos, costo/km, capacidad de paradas) — ver acta_sistema_v3.md §4.
///
/// Sin [Authorize] a nivel de clase a propósito (mismo motivo que ClientesController/
/// UsuariosController): ASP.NET Core combina el [Authorize] de clase y el de acción con AND, no lo
/// reemplaza. Seleccion necesita BackOffice (operación también arma rutas y necesita elegir
/// vehículo); si la clase exigiera Administracion, ese endpoint quedaría inalcanzable para
/// operación pese al [Authorize(Policy="BackOffice")] propio.
/// </summary>
[ApiController]
[Route("api/vehiculos")]
public class VehiculosController(LogisticaDbContext db) : ControllerBase
{
    public record VehiculoResumen(
        long Id, string Patente, string? Descripcion, string? Marca, string? Modelo, int? Anio,
        int? KmActual, DateOnly? VenceVtv, DateOnly? VenceSeguro, decimal? CostoKm,
        int CapacidadParadas, bool Activo);

    /// <summary>Para el selector al armar una ruta (RutasController).</summary>
    public record VehiculoSeleccion(long Id, string Patente, string? Descripcion, int CapacidadParadas);

    public record CrearVehiculoRequest(
        string Patente, string? Descripcion, string? Marca, string? Modelo, int? Anio,
        int? KmActual, DateOnly? VenceVtv, DateOnly? VenceSeguro, decimal? CostoKm, int CapacidadParadas);

    public record ActualizarVehiculoRequest(
        string Patente, string? Descripcion, string? Marca, string? Modelo, int? Anio,
        int? KmActual, DateOnly? VenceVtv, DateOnly? VenceSeguro, decimal? CostoKm,
        int CapacidadParadas, bool Activo);

    public record ActivoRequest(bool Activo);

    [HttpGet]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Vehiculos.AsNoTracking()
            .OrderBy(v => v.Patente)
            .Select(v => new VehiculoResumen(v.Id, v.Patente, v.Descripcion, v.Marca, v.Modelo, v.Anio,
                v.KmActual, v.VenceVtv, v.VenceSeguro, v.CostoKm, v.CapacidadParadas, v.Activo))
            .ToListAsync(ct));

    /// <summary>Para selectores (ej. armado de ruta — RutasController). BackOffice: operación
    /// también arma rutas y necesita elegir vehículo sin ver el resto del ABM.</summary>
    [HttpGet("seleccion")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Seleccion(CancellationToken ct) =>
        Ok(await db.Vehiculos.AsNoTracking()
            .Where(v => v.Activo)
            .OrderBy(v => v.Patente)
            .Select(v => new VehiculoSeleccion(v.Id, v.Patente, v.Descripcion, v.CapacidadParadas))
            .ToListAsync(ct));

    [HttpGet("{id:long}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new VehiculoResumen(v.Id, v.Patente, v.Descripcion, v.Marca, v.Modelo, v.Anio,
                v.KmActual, v.VenceVtv, v.VenceSeguro, v.CostoKm, v.CapacidadParadas, v.Activo))
            .SingleOrDefaultAsync(ct);

        return vehiculo is null ? NotFound() : Ok(vehiculo);
    }

    /// <summary>No se revalida la unicidad de patente en C#: el unique index ya lo hace, y
    /// ManejadorExcepciones lo traduce a 409 con el mensaje de Postgres — duplicar la regla acá
    /// violaría construccion_v1.md §3 regla 3.</summary>
    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Crear(CrearVehiculoRequest req, CancellationToken ct)
    {
        var patente = req.Patente.Trim().ToUpperInvariant();
        if (patente.Length == 0) return BadRequest("La patente es obligatoria.");

        var vehiculo = new Vehiculo
        {
            Patente = patente,
            Descripcion = req.Descripcion,
            Marca = req.Marca,
            Modelo = req.Modelo,
            Anio = req.Anio,
            KmActual = req.KmActual,
            VenceVtv = req.VenceVtv,
            VenceSeguro = req.VenceSeguro,
            CostoKm = req.CostoKm,
            CapacidadParadas = req.CapacidadParadas,
            CreadoEn = DateTimeOffset.UtcNow,
        };

        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Detalle), new { id = vehiculo.Id },
            new VehiculoResumen(vehiculo.Id, vehiculo.Patente, vehiculo.Descripcion, vehiculo.Marca,
                vehiculo.Modelo, vehiculo.Anio, vehiculo.KmActual, vehiculo.VenceVtv, vehiculo.VenceSeguro,
                vehiculo.CostoKm, vehiculo.CapacidadParadas, vehiculo.Activo));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Actualizar(long id, ActualizarVehiculoRequest req, CancellationToken ct)
    {
        var patente = req.Patente.Trim().ToUpperInvariant();
        if (patente.Length == 0) return BadRequest("La patente es obligatoria.");

        var vehiculo = await db.Vehiculos.SingleOrDefaultAsync(v => v.Id == id, ct);
        if (vehiculo is null) return NotFound();

        vehiculo.Patente = patente;
        vehiculo.Descripcion = req.Descripcion;
        vehiculo.Marca = req.Marca;
        vehiculo.Modelo = req.Modelo;
        vehiculo.Anio = req.Anio;
        vehiculo.KmActual = req.KmActual;
        vehiculo.VenceVtv = req.VenceVtv;
        vehiculo.VenceSeguro = req.VenceSeguro;
        vehiculo.CostoKm = req.CostoKm;
        vehiculo.CapacidadParadas = req.CapacidadParadas;
        vehiculo.Activo = req.Activo;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Alta/baja rápida desde el listado, igual que Usuarios/Clientes. Desactivar en vez
    /// de borrar: las rutas históricas referencian vehiculo_id.</summary>
    [HttpPut("{id:long}/activo")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarActivo(long id, ActivoRequest req, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.SingleOrDefaultAsync(v => v.Id == id, ct);
        if (vehiculo is null) return NotFound();

        vehiculo.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
