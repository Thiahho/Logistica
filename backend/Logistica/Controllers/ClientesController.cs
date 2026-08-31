using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Solo administración: acá viven los semáforos internos (RF-32/RF-33) y las tarifas por
/// cliente, que operación no debe ver (no debe conocer el margen). El rol 'cliente' ni siquiera
/// puede pegarle a este controller. Operación elige cliente para el alta de pedido a través de
/// GET /seleccion, que no expone colores ni tarifas.
///
/// Sin [Authorize] a nivel de clase a propósito: ASP.NET Core combina el [Authorize] de clase
/// y el de acción con AND, no lo reemplaza — un [Authorize(Policy="BackOffice")] en Seleccion no
/// alcanzaría a operación si la clase ya exige Administracion. Por eso cada acción declara su
/// propia política.
/// </summary>
[ApiController]
[Route("api/clientes")]
public class ClientesController(LogisticaDbContext db, TarifaService tarifas) : ControllerBase
{
    public record TarifaZona(int ZonaId, string ZonaCodigo, string ZonaNombre, decimal? PrecioGeneral, decimal? PrecioCliente);
    public record ClienteSeleccion(int Id, string RazonSocial);

    public record EventoResumen(
        long Id, string TipoCodigo, string TipoDescripcion, string Dimension,
        decimal? ValorNum, string? Nota, DateTimeOffset OcurridoEn, string? RegistradoPorNombre);

    public record ClienteDetalle(
        int Id, string RazonSocial, string? Cuit, string? Contacto, string? Telefono, string? Email, bool Activo,
        string ColorPago, string ColorTrato, string ColorOper,
        List<TarifaZona> Tarifas, Dictionary<string, int> ContadorEventos, List<EventoResumen> UltimosEventos);

    public record ActualizarClienteRequest(
        string RazonSocial, string? Cuit, string? Contacto, string? Telefono, string? Email, bool Activo,
        string ColorPago, string ColorTrato, string ColorOper);

    public record FijarTarifaRequest(decimal? Precio);

    public record CrearEventoRequest(int TipoId, decimal? ValorNum, string? Nota, long? PedidoId);

    public record ClienteUsuarioResumen(Guid Id, string Nombre, string Email, bool Activo);
    public record CrearClienteUsuarioRequest(string Nombre, string Email, string Password);
    public record CambiarPasswordClienteUsuarioRequest(string Password);

    public record CrearClienteRequest(string RazonSocial, string? Cuit, string? Contacto, string? Telefono, string? Email);

    public record ActivoRequest(bool Activo);

    [HttpGet]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await db.Clientes.AsNoTracking().OrderBy(c => c.RazonSocial).ToListAsync(ct));

    /// <summary>Para el selector de cliente en el alta de pedido (BackOffice: también operación).
    /// Sin colores ni tarifas — eso es privativo de administración.</summary>
    [HttpGet("seleccion")]
    [Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Seleccion(CancellationToken ct) =>
        Ok(await db.Clientes.AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.RazonSocial)
            .Select(c => new ClienteSeleccion(c.Id, c.RazonSocial))
            .ToListAsync(ct));

    /// <summary>Colores por defecto para un cliente nuevo (RF-32: "un cliente sin historial no es
    /// neutro, es desconocido" — pago en rojo, trato y operación en amarillo). Ya son los valores
    /// por defecto de la entidad, no hace falta pisarlos acá.</summary>
    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Crear(CrearClienteRequest req, CancellationToken ct)
    {
        var cliente = new Cliente
        {
            RazonSocial = req.RazonSocial,
            Cuit = req.Cuit,
            Contacto = req.Contacto,
            Telefono = req.Telefono,
            Email = req.Email,
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Detalle), new { id = cliente.Id }, new { cliente.Id });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var zonas = await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct);

        var tarifas = new List<TarifaZona>();
        foreach (var zona in zonas)
        {
            var precioGeneral = await db.Database
                .SqlQuery<decimal?>($"select tarifa_vigente(null, {zona.Id}, {hoy}) as \"Value\"")
                .SingleAsync(ct);
            var precioCliente = await db.Tarifas.AsNoTracking()
                .Where(t => t.ClienteId == id && t.ZonaId == zona.Id && t.VigenteHasta == null)
                .Select(t => (decimal?)t.Precio)
                .SingleOrDefaultAsync(ct);
            tarifas.Add(new TarifaZona(zona.Id, zona.Codigo, zona.Nombre, precioGeneral, precioCliente));
        }

        var contador = await db.EventosCliente.AsNoTracking()
            .Where(e => e.ClienteId == id)
            .GroupBy(e => e.Tipo.Dimension)
            .Select(g => new { Dimension = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(g => g.Dimension, g => g.Cantidad, ct);
        foreach (var dimension in new[] { "pago", "trato", "operacion" })
            contador.TryAdd(dimension, 0);

        var ultimos = await db.EventosCliente.AsNoTracking()
            .Where(e => e.ClienteId == id)
            .OrderByDescending(e => e.OcurridoEn)
            .Take(10)
            .Select(e => new EventoResumen(
                e.Id, e.Tipo.Codigo, e.Tipo.Descripcion, e.Tipo.Dimension,
                e.ValorNum, e.Nota, e.OcurridoEn, e.RegistradoPor != null ? e.RegistradoPor.Nombre : null))
            .ToListAsync(ct);

        return Ok(new ClienteDetalle(
            cliente.Id, cliente.RazonSocial, cliente.Cuit, cliente.Contacto, cliente.Telefono, cliente.Email,
            cliente.Activo, cliente.ColorPago, cliente.ColorTrato, cliente.ColorOper, tarifas, contador, ultimos));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Actualizar(int id, ActualizarClienteRequest req, CancellationToken ct)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        cliente.RazonSocial = req.RazonSocial;
        cliente.Cuit = req.Cuit;
        cliente.Contacto = req.Contacto;
        cliente.Telefono = req.Telefono;
        cliente.Email = req.Email;
        cliente.Activo = req.Activo;
        cliente.ColorPago = req.ColorPago;
        cliente.ColorTrato = req.ColorTrato;
        cliente.ColorOper = req.ColorOper;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Alta/baja rápida desde el listado, sin pasar por el formulario completo de edición.</summary>
    [HttpPut("{id:int}/activo")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarActivo(int id, ActivoRequest req, CancellationToken ct)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        cliente.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Borrado real, solo para un alta hecha por error: si el cliente ya tiene pedidos, tarifas,
    /// eventos o usuarios asociados (todas las FK son Restrict, no cascada, a propósito — el
    /// historial no se pierde), se rechaza con 409 y el camino correcto es desactivar, no borrar.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        var tieneDatos = await db.Pedidos.AnyAsync(p => p.ClienteId == id, ct)
            || await db.Tarifas.AnyAsync(t => t.ClienteId == id, ct)
            || await db.EventosCliente.AnyAsync(e => e.ClienteId == id, ct)
            || await db.ClientesUsuarios.AnyAsync(u => u.ClienteId == id, ct);
        if (tieneDatos)
            return Conflict("El cliente tiene pedidos, tarifas, eventos o usuarios asociados; desactivalo en vez de eliminarlo.");

        db.Clientes.Remove(cliente);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Fija o borra el precio específico del cliente para una zona, sin pisar el historial de
    /// tarifas: si ya hay una vigente de hoy la actualiza, si es de un día anterior la cierra y
    /// abre una nueva. precio=null cierra la vigente y el cliente vuelve a la lista general.
    /// </summary>
    [HttpPut("{id:int}/tarifas/{zonaId:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> FijarTarifa(int id, int zonaId, FijarTarifaRequest req, CancellationToken ct)
    {
        await tarifas.FijarAsync(id, zonaId, req.Precio, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/eventos")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ListarEventos(int id, [FromQuery] string? dimension, CancellationToken ct)
    {
        var query = db.EventosCliente.AsNoTracking().Where(e => e.ClienteId == id);
        if (dimension is not null) query = query.Where(e => e.Tipo.Dimension == dimension);

        var eventos = await query
            .OrderByDescending(e => e.OcurridoEn)
            .Select(e => new EventoResumen(
                e.Id, e.Tipo.Codigo, e.Tipo.Descripcion, e.Tipo.Dimension,
                e.ValorNum, e.Nota, e.OcurridoEn, e.RegistradoPor != null ? e.RegistradoPor.Nombre : null))
            .ToListAsync(ct);

        return Ok(eventos);
    }

    [HttpPost("{id:int}/eventos")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CrearEvento(int id, CrearEventoRequest req, CancellationToken ct)
    {
        var clienteExiste = await db.Clientes.AnyAsync(c => c.Id == id, ct);
        if (!clienteExiste) return NotFound();

        var evento = new EventoCliente
        {
            ClienteId = id,
            TipoId = req.TipoId,
            PedidoId = req.PedidoId,
            ValorNum = req.ValorNum,
            Nota = req.Nota,
            OcurridoEn = DateTimeOffset.UtcNow,
            RegistradoPorId = User.UsuarioId(),
        };
        db.EventosCliente.Add(evento);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(ListarEventos), new { id }, null);
    }

    /// <summary>
    /// CRUD del login de consulta de un cliente (tabla clientes_usuarios, separada de usuarios
    /// de personal interno a propósito — ver Entidades/ClienteUsuario.cs). Mismo patrón que
    /// UsuariosController: solo Administracion, contraseña mínima de 8 caracteres.
    /// </summary>
    [HttpGet("{id:int}/usuarios")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ListarUsuarios(int id, CancellationToken ct) =>
        Ok(await db.ClientesUsuarios.AsNoTracking()
            .Where(u => u.ClienteId == id)
            .OrderBy(u => u.Nombre)
            .Select(u => new ClienteUsuarioResumen(u.Id, u.Nombre, u.Email, u.Activo))
            .ToListAsync(ct));

    [HttpPost("{id:int}/usuarios")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CrearUsuario(int id, CrearClienteUsuarioRequest req, CancellationToken ct)
    {
        var clienteExiste = await db.Clientes.AnyAsync(c => c.Id == id, ct);
        if (!clienteExiste) return NotFound();
        if (req.Password.Length < 8) return BadRequest("La contraseña debe tener al menos 8 caracteres.");

        var usuario = new ClienteUsuario
        {
            Id = Guid.NewGuid(),
            ClienteId = id,
            Nombre = req.Nombre,
            Email = req.Email,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        usuario.PasswordHash = AuthService.HashearCliente(usuario, req.Password);

        db.ClientesUsuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(ListarUsuarios), new { id },
            new ClienteUsuarioResumen(usuario.Id, usuario.Nombre, usuario.Email, usuario.Activo));
    }

    [HttpPut("{id:int}/usuarios/{usuarioId:guid}/activo")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarActivoUsuario(int id, Guid usuarioId, ActivoRequest req, CancellationToken ct)
    {
        var usuario = await db.ClientesUsuarios.SingleOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == id, ct);
        if (usuario is null) return NotFound();

        usuario.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/usuarios/{usuarioId:guid}/password")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CambiarPasswordUsuario(
        int id, Guid usuarioId, CambiarPasswordClienteUsuarioRequest req, CancellationToken ct)
    {
        if (req.Password.Length < 8) return BadRequest("La contraseña debe tener al menos 8 caracteres.");

        var usuario = await db.ClientesUsuarios.SingleOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == id, ct);
        if (usuario is null) return NotFound();

        usuario.PasswordHash = AuthService.HashearCliente(usuario, req.Password);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
