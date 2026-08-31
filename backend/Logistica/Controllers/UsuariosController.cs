using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Alta y administración de usuarios de personal interno (administracion, operacion,
/// repartidor). Sin esto el admin no puede dar de alta al repartidor (bloquea las fases de
/// operador y repartidor). Los logins de cliente son otra tabla — ver ClienteUsuario y los
/// endpoints anidados bajo ClientesController.
///
/// Sin [Authorize] a nivel de clase a propósito (mismo motivo que ClientesController): ASP.NET
/// Core combina el [Authorize] de clase y el de acción con AND, no lo reemplaza. Seleccion
/// necesita BackOffice (también operación); si la clase exigiera Administracion, ese endpoint
/// quedaría inalcanzable para operación pese al [Authorize(Policy="BackOffice")] propio.
/// </summary>
[ApiController]
[Route("api/usuarios")]
public class UsuariosController(LogisticaDbContext db) : ControllerBase
{
    public record UsuarioResumen(Guid Id, string Nombre, string Email, string Rol, bool Activo);
    public record UsuarioSeleccion(Guid Id, string Nombre);
    public record CrearUsuarioRequest(string Nombre, string Email, string Password, string Rol);
    public record ActualizarUsuarioRequest(string Nombre, string Rol, bool Activo);
    public record CambiarPasswordRequest(string Password);
    public record ActivoRequest(bool Activo);

    [HttpGet]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Usuarios.AsNoTracking()
            .OrderBy(u => u.Nombre)
            .Select(u => new UsuarioResumen(u.Id, u.Nombre, u.Email, u.Rol, u.Activo))
            .ToListAsync(ct));

    /// <summary>Para selectores (ej. repartidor al armar una ruta — RutasController). BackOffice:
    /// operación también arma rutas y necesita elegir repartidor sin ver el resto de Usuarios.</summary>
    [HttpGet("seleccion")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Seleccion([FromQuery] string rol, CancellationToken ct) =>
        Ok(await db.Usuarios.AsNoTracking()
            .Where(u => u.Rol == rol && u.Activo)
            .OrderBy(u => u.Nombre)
            .Select(u => new UsuarioSeleccion(u.Id, u.Nombre))
            .ToListAsync(ct));

    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Crear(CrearUsuarioRequest req, CancellationToken ct)
    {
        if (!Roles.Todos.Contains(req.Rol)) return BadRequest("Rol inválido.");
        if (req.Password.Length < 8) return BadRequest("La contraseña debe tener al menos 8 caracteres.");

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nombre = req.Nombre,
            Email = req.Email,
            Rol = req.Rol,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        usuario.PasswordHash = AuthService.Hashear(usuario, req.Password);

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Listar), new { },
            new UsuarioResumen(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.Activo));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Actualizar(Guid id, ActualizarUsuarioRequest req, CancellationToken ct)
    {
        if (!Roles.Todos.Contains(req.Rol)) return BadRequest("Rol inválido.");

        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return NotFound();

        usuario.Nombre = req.Nombre;
        usuario.Rol = req.Rol;
        usuario.Activo = req.Activo;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Alta/baja rápida desde el listado, igual que ClientesController.CambiarActivo.
    /// Desactivar en vez de borrar: no hay borrado de usuarios, el historial de pedido_eventos
    /// referencia actor_usuario_id.</summary>
    [HttpPut("{id:guid}/activo")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarActivo(Guid id, ActivoRequest req, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return NotFound();

        usuario.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reset de contraseña por el admin (acta: "nunca pedir contraseña en la calle" —
    /// el repartidor no puede autoservirse un cambio).</summary>
    [HttpPut("{id:guid}/password")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarPassword(Guid id, CambiarPasswordRequest req, CancellationToken ct)
    {
        if (req.Password.Length < 8) return BadRequest("La contraseña debe tener al menos 8 caracteres.");

        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return NotFound();

        usuario.PasswordHash = AuthService.Hashear(usuario, req.Password);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
