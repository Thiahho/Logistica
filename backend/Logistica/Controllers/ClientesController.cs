using System.ComponentModel.DataAnnotations;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
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
public class ClientesController(LogisticaDbContext db, TarifaService tarifas, CuentaCorrienteService cuentaCorriente) : ControllerBase
{
    public record TarifaZona(
        int ZonaId, string ZonaCodigo, string ZonaNombre,
        decimal? PrecioGeneralCamioneta, decimal? PrecioClienteCamioneta,
        decimal? PrecioGeneralMoto, decimal? PrecioClienteMoto);
    public record ClienteSeleccion(int Id, string RazonSocial);

    public record EventoResumen(
        long Id, string TipoCodigo, string TipoDescripcion, string Dimension,
        decimal? ValorNum, string? Nota, DateTimeOffset OcurridoEn, string? RegistradoPorNombre);

    public record ClienteDetalle(
        int Id, string RazonSocial, string? Cuit, string? Contacto, string? Telefono, string? Email, bool Activo,
        string ColorPago, string ColorTrato, string ColorOper, string CicloFacturacion,
        decimal SaldoCliente, decimal DeudaVencida,
        List<TarifaZona> Tarifas, Dictionary<string, int> ContadorEventos, List<EventoResumen> UltimosEventos);

    public record ActualizarClienteRequest(
        string RazonSocial, string? Cuit, string? Contacto, string? Telefono, string? Email, bool Activo,
        string ColorPago, string ColorTrato, string ColorOper, string CicloFacturacion);

    // ---- E1: cuenta corriente ----
    public record FacturaClienteResumen(
        long Id, string Ciclo, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
        DateOnly FechaEmision, DateOnly FechaVencimiento, decimal Total, decimal Pagado, decimal Saldo, string Estado);
    public record PagoResumen(
        long Id, decimal Monto, DateOnly FechaPago, string Medio, string? Nota,
        string? RegistradoPorNombre, DateTimeOffset RegistradoEn);
    public record CuentaCorrienteCliente(
        decimal Saldo, decimal DeudaVencida, bool ServicioCortado,
        DateOnly? CorteSuspendidoHasta, string? CorteSuspendidoMotivo,
        string? CorteSuspendidoPorNombre, DateTimeOffset? CorteSuspendidoEn,
        decimal PendienteDeFacturar, int AjustesPendientes,
        List<FacturaClienteResumen> Facturas, List<PagoResumen> Pagos);
    public record RegistrarPagoRequest(
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero.")] decimal Monto,
        DateOnly? FechaPago, string Medio, string? Nota);
    public record CorteSuspendidoRequest(DateOnly? Hasta, string? Motivo);

    /// <summary>pendiente | parcial | pagada | vencida — mismo criterio que
    /// FacturasController.EstadoDe (derivado de v_facturas_saldo, no persistido). Duplicado a
    /// propósito: son 4 líneas en dos controllers, extraer un helper compartido para esto sería
    /// más ceremonia que la propia regla.</summary>
    private static string EstadoDe(decimal saldo, decimal total, DateOnly vencimiento, DateOnly hoy)
    {
        if (saldo <= 0) return "pagada";
        if (vencimiento < hoy) return "vencida";
        return saldo < total ? "parcial" : "pendiente";
    }

    public record FijarTarifaRequest(
        string TipoVehiculo,
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")] decimal? Precio);

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
            async Task<decimal?> PrecioGeneral(string tipo) => await db.Database
                .SqlQuery<decimal?>($"select tarifa_vigente(null, {zona.Id}, {hoy}, {tipo}) as \"Value\"")
                .SingleAsync(ct);
            async Task<decimal?> PrecioCliente(string tipo) => await db.Tarifas.AsNoTracking()
                .Where(t => t.ClienteId == id && t.ZonaId == zona.Id && t.TipoVehiculo == tipo && t.VigenteHasta == null)
                .Select(t => (decimal?)t.Precio)
                .SingleOrDefaultAsync(ct);

            tarifas.Add(new TarifaZona(
                zona.Id, zona.Codigo, zona.Nombre,
                await PrecioGeneral("camioneta"), await PrecioCliente("camioneta"),
                await PrecioGeneral("moto"), await PrecioCliente("moto")));
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

        // E1: dos escalares, baratos — el detalle completo (facturas, pagos, corte suspendido)
        // vive en GET .../cuenta-corriente, esto es solo para que el semáforo de color se lea
        // al lado del número real.
        var saldoCliente = await cuentaCorriente.SaldoAsync(id, ct);
        var deudaVencida = await cuentaCorriente.DeudaVencidaAsync(id, Reloj.HoyLocal(), ct);

        return Ok(new ClienteDetalle(
            cliente.Id, cliente.RazonSocial, cliente.Cuit, cliente.Contacto, cliente.Telefono, cliente.Email,
            cliente.Activo, cliente.ColorPago, cliente.ColorTrato, cliente.ColorOper, cliente.CicloFacturacion,
            saldoCliente, deudaVencida, tarifas, contador, ultimos));
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
        cliente.CicloFacturacion = req.CicloFacturacion;

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
            || await db.ClientesUsuarios.AnyAsync(u => u.ClienteId == id, ct)
            || await db.Facturas.AnyAsync(f => f.ClienteId == id, ct)
            || await db.Pagos.AnyAsync(p => p.ClienteId == id, ct);
        if (tieneDatos)
            return Conflict("El cliente tiene pedidos, tarifas, eventos, usuarios, facturas o pagos asociados; desactivalo en vez de eliminarlo.");

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
        if (req.TipoVehiculo is not ("camioneta" or "moto")) return BadRequest("Tipo de vehículo inválido.");
        await tarifas.FijarAsync(id, zonaId, req.TipoVehiculo, req.Precio, ct);
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

    /// <summary>E1: saldo, deuda vencida, estado del corte, facturas (con saldo derivado) y
    /// pagos del cliente — todo lo que la Card de cuenta corriente necesita en un solo request.</summary>
    [HttpGet("{id:int}/cuenta-corriente")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> CuentaCorriente(int id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        var hoy = Reloj.HoyLocal();
        var saldo = await cuentaCorriente.SaldoAsync(id, ct);
        var deudaVencida = await cuentaCorriente.DeudaVencidaAsync(id, hoy, ct);
        var servicioCortado = await cuentaCorriente.ServicioCortadoAsync(id, hoy, ct);

        var corteSuspendidoPorNombre = cliente.CorteSuspendidoPor is null
            ? null
            : await db.Usuarios.Where(u => u.Id == cliente.CorteSuspendidoPor).Select(u => u.Nombre).SingleOrDefaultAsync(ct);

        var facturas = (await db.Set<FacturaSaldo>().AsNoTracking()
            .Where(f => f.ClienteId == id)
            .OrderByDescending(f => f.FechaEmision)
            .ToListAsync(ct))
            .Select(f => new FacturaClienteResumen(
                f.Id, f.Ciclo, f.PeriodoDesde, f.PeriodoHasta, f.FechaEmision, f.FechaVencimiento,
                f.Total, f.Pagado, f.Saldo, EstadoDe(f.Saldo, f.Total, f.FechaVencimiento, hoy)))
            .ToList();

        var pagos = await db.Pagos.AsNoTracking()
            .Where(p => p.ClienteId == id)
            .OrderByDescending(p => p.FechaPago).ThenByDescending(p => p.Id)
            .Select(p => new PagoResumen(p.Id, p.Monto, p.FechaPago, p.Medio, p.Nota,
                p.RegistradoPorUsuario != null ? p.RegistradoPorUsuario.Nombre : null, p.RegistradoEn))
            .ToListAsync(ct);

        var pendienteDeFacturar = await db.FacturaItems.AsNoTracking()
            .Where(i => i.FacturaId == null && i.Estado == "aprobado" && i.Pedido!.ClienteId == id)
            .SumAsync(i => (decimal?)i.Monto, ct) ?? 0m;

        var ajustesPendientes = await db.FacturaItems.AsNoTracking()
            .CountAsync(i => i.FacturaId == null && i.Estado == "pendiente" && i.Pedido!.ClienteId == id, ct);

        return Ok(new CuentaCorrienteCliente(
            saldo, deudaVencida, servicioCortado,
            cliente.CorteSuspendidoHasta, cliente.CorteSuspendidoMotivo, corteSuspendidoPorNombre, cliente.CorteSuspendidoEn,
            pendienteDeFacturar, ajustesPendientes, facturas, pagos));
    }

    /// <summary>Registra un pago (§10.2-B/D12). Insert-only (trg_pagos_inmutable) — un pago mal
    /// cargado se corrige con un pago de monto negativo y nota, no editando este.</summary>
    [HttpPost("{id:int}/pagos")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> RegistrarPago(int id, RegistrarPagoRequest req, CancellationToken ct)
    {
        if (req.Medio is not ("transferencia" or "efectivo" or "cheque" or "otro"))
            return BadRequest("Medio de pago inválido.");

        var clienteExiste = await db.Clientes.AnyAsync(c => c.Id == id, ct);
        if (!clienteExiste) return NotFound();

        db.Pagos.Add(new Pago
        {
            ClienteId = id,
            Monto = req.Monto,
            FechaPago = req.FechaPago ?? Reloj.HoyLocal(),
            Medio = req.Medio,
            Nota = req.Nota,
            RegistradoPor = User.UsuarioId(),
            RegistradoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>E1 / §10.2-L4: plan de cuotas. `Hasta = null` levanta la suspensión (vuelve a
    /// evaluarse la deuda vencida normalmente). Con `Hasta` en el futuro, el corte se ignora
    /// hasta esa fecha; si no se extiende con la próxima cuota, el corte vuelve solo, sin job.</summary>
    [HttpPut("{id:int}/corte-suspendido")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ActualizarCorteSuspendido(int id, CorteSuspendidoRequest req, CancellationToken ct)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        if (req.Hasta is null)
        {
            cliente.CorteSuspendidoHasta = null;
            cliente.CorteSuspendidoMotivo = null;
            cliente.CorteSuspendidoPor = null;
            cliente.CorteSuspendidoEn = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Motivo))
                return BadRequest("El motivo es obligatorio para suspender el corte.");

            cliente.CorteSuspendidoHasta = req.Hasta;
            cliente.CorteSuspendidoMotivo = req.Motivo;
            cliente.CorteSuspendidoPor = User.UsuarioId();
            cliente.CorteSuspendidoEn = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
