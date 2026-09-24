using System.ComponentModel.DataAnnotations;
using Logistica.Web;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
public class ClientesController(
    LogisticaDbContext db, TarifaService tarifas, CuentaCorrienteService cuentaCorriente, AvisosCobranzaService avisosCobranza)
    : ControllerBase
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

    /// <summary>
    /// Panel de cobranza: clientes con deuda vencida o con una factura por vencer dentro de
    /// CuentaCorrienteService.DiasPreavisoDefault días (15 — regla de negocio fija, no un filtro
    /// ajustable: si fuera ajustable acá, un cliente visible con una ventana más ancha podría
    /// salir "Omitido" al intentar mandarle un aviso, porque POST /avisos usa siempre la misma
    /// constante. Listar y enviar comparten la misma ventana a propósito, para que nunca
    /// diverjan. RiesgoAsync hace el trabajo pesado en SQL (una sola consulta agrupada, nunca
    /// N+1); acá solo queda filtrar por categoría/texto, ordenar y paginar en memoria — el
    /// conjunto de clientes "en riesgo" es un subconjunto chico del total, no todo el padrón.
    /// </summary>
    [HttpGet("riesgo")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> ListarRiesgo(
        [FromQuery] string? categoria,
        [FromQuery] string? q,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanioPagina,
        CancellationToken ct)
    {
        if (categoria is not null && categoria is not ("vencido" or "por_vencer"))
            return BadRequest("Categoría inválida.");

        var clientes = await cuentaCorriente.RiesgoAsync(
            Reloj.HoyLocal(), CuentaCorrienteService.DiasPreavisoDefault,
            clienteIds: null, soloActivos: true, ct);

        if (categoria is not null)
            clientes = clientes.Where(c => c.Categoria == categoria).ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var texto = q.Trim().ToLower();
            clientes = clientes.Where(c => c.RazonSocial.ToLower().Contains(texto)).ToList();
        }

        // Sin `orden`, se mantiene el orden que ya trae RiesgoAsync (deuda vencida desc, próximo
        // vencimiento asc, razón social) — es el orden de prioridad de cobranza, no alfabético.
        clientes = orden switch
        {
            "deuda" => clientes.OrderBy(c => c.DeudaVencida).ThenBy(c => c.RazonSocial).ToList(),
            "-deuda" => clientes.OrderByDescending(c => c.DeudaVencida).ThenBy(c => c.RazonSocial).ToList(),
            "vencimiento" => clientes.OrderBy(c => c.ProximoVencimiento).ThenBy(c => c.RazonSocial).ToList(),
            "cliente" => clientes.OrderBy(c => c.RazonSocial).ToList(),
            _ => clientes,
        };

        var total = clientes.Count;
        tamanioPagina = Paginacion.TamanioEfectivo(tamanioPagina);
        if (tamanioPagina is > 0)
        {
            var paginaActual = pagina is > 0 ? Math.Min(pagina.Value, 1_000_000) : 1;
            clientes = clientes.Skip((paginaActual - 1) * tamanioPagina.Value).Take(tamanioPagina.Value).ToList();
        }

        return Ok(new ListaPaginada<ClienteEnRiesgo>(clientes, total));
    }

    public record AvisosRequest(List<int> ClienteIds, bool EnviarEmail = true);

    /// <summary>Previsualización — sin efectos. Devuelve el asunto/mensaje YA armados por el
    /// servidor para cada cliente (nunca lo que mande el front), así el admin ve exactamente qué
    /// se va a mandar antes de confirmar nada.</summary>
    [HttpPost("avisos/previsualizacion")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> PrevisualizarAvisos(AvisosRequest req, CancellationToken ct)
    {
        var error = ValidarClienteIds(req.ClienteIds);
        if (error is not null) return BadRequest(error);

        return Ok(await avisosCobranza.PrevisualizarAsync(req.ClienteIds, Reloj.HoyLocal(), ct));
    }

    /// <summary>Envía los avisos — email (si `EnviarEmail` y el cliente tiene casilla) + siempre
    /// arma el link de WhatsApp si hay teléfono. Nunca 500 por un fallo de Resend: el error de
    /// un destinatario no debe tumbar la respuesta de los demás (mismo criterio que
    /// CerrarCiclosAsync devolviendo `Omitido` en vez de tirar).</summary>
    [HttpPost("avisos")]
    [Authorize(Policy = "Administracion")]
    [EnableRateLimiting("avisos")]
    public async Task<IActionResult> EnviarAvisos(AvisosRequest req, CancellationToken ct)
    {
        var error = ValidarClienteIds(req.ClienteIds);
        if (error is not null) return BadRequest(error);

        var resultado = await avisosCobranza.EnviarAsync(req.ClienteIds, req.EnviarEmail, User.UsuarioId(), Reloj.HoyLocal(), ct);
        return Ok(resultado);
    }

    /// <summary>Único guardarraíl real contra un envío masivo por error: sin esto, nada impide
    /// seleccionar "todos" y mandarle un aviso a la cartera entera de un click.</summary>
    private static string? ValidarClienteIds(List<int> clienteIds)
    {
        if (clienteIds is not { Count: > 0 }) return "Elegí al menos un cliente.";
        if (clienteIds.Count > AvisosCobranzaService.MaxClientesPorLote)
            return $"Máximo {AvisosCobranzaService.MaxClientesPorLote} clientes por envío.";
        return null;
    }

    /// <summary>Hallazgo 3 de auditoria_seguridad.md: los tres son opcionales, pero si vienen tienen que
    /// tener formato válido. El CUIT se guarda normalizado (solo dígitos); vacío se guarda como null.</summary>
    private static (string? Error, string? Cuit, string? Telefono, string? Email) ValidarContacto(
        string? cuit, string? telefono, string? email)
    {
        string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var (c, t, e) = (Limpio(cuit), Limpio(telefono), Limpio(email));

        string? cuitNormalizado = null;
        if (c is not null && (cuitNormalizado = Validaciones.NormalizarCuit(c)) is null)
            return ("El CUIT no es válido: son 11 dígitos con el verificador correcto (por ejemplo 30-12345678-1).", null, null, null);
        if (t is not null && !Validaciones.TelefonoValido(t))
            return ("El teléfono no es válido: tiene que tener entre 8 y 15 dígitos.", null, null, null);
        if (e is not null && !Validaciones.EmailValido(e))
            return ("El email no es válido.", null, null, null);
        return (null, cuitNormalizado, t, e);
    }

    /// <summary>Colores por defecto para un cliente nuevo (RF-32: "un cliente sin historial no es
    /// neutro, es desconocido" — pago en rojo, trato y operación en amarillo). Ya son los valores
    /// por defecto de la entidad, no hace falta pisarlos acá.</summary>
    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Crear(CrearClienteRequest req, CancellationToken ct)
    {
        var (error, cuit, telefono, email) = ValidarContacto(req.Cuit, req.Telefono, req.Email);
        if (error is not null) return BadRequest(error);

        var cliente = new Cliente
        {
            RazonSocial = req.RazonSocial,
            Cuit = cuit,
            Contacto = req.Contacto,
            Telefono = telefono,
            Email = email,
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

        var hoy = Reloj.HoyLocal();
        var zonas = await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct);

        // Antes eran 4 round-trips por zona (tarifa_vigente x2 tipos + tarifa de cliente x2
        // tipos) dentro del foreach. Acá se traen las dos fuentes de una vez, fuera del loop, y
        // se arma cada TarifaZona leyendo de los diccionarios — mismos valores, sin awaits
        // adentro del foreach. Ver TarifaService.PreciosGeneralesAsync.
        var preciosGenerales = await tarifas.PreciosGeneralesAsync(hoy, ct);
        var preciosCliente = (await db.Tarifas.AsNoTracking()
                .Where(t => t.ClienteId == id && t.VigenteHasta == null)
                .Select(t => new { t.ZonaId, t.TipoVehiculo, t.Precio })
                .ToListAsync(ct))
            .ToDictionary(t => (t.ZonaId, t.TipoVehiculo), t => (decimal?)t.Precio);

        var tarifasPorZona = zonas.Select(zona => new TarifaZona(
                zona.Id, zona.Codigo, zona.Nombre,
                preciosGenerales.GetValueOrDefault((zona.Id, "camioneta")),
                preciosCliente.GetValueOrDefault((zona.Id, "camioneta")),
                preciosGenerales.GetValueOrDefault((zona.Id, "moto")),
                preciosCliente.GetValueOrDefault((zona.Id, "moto"))))
            .ToList();

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
            saldoCliente, deudaVencida, tarifasPorZona, contador, ultimos));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Actualizar(int id, ActualizarClienteRequest req, CancellationToken ct)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return NotFound();

        var (error, cuit, telefono, email) = ValidarContacto(req.Cuit, req.Telefono, req.Email);
        if (error is not null) return BadRequest(error);

        cliente.RazonSocial = req.RazonSocial;
        cliente.Cuit = cuit;
        cliente.Contacto = req.Contacto;
        cliente.Telefono = telefono;
        cliente.Email = email;
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

        // Antes eran hasta 6 round-trips en serie (uno por tabla, cortando apenas alguno daba
        // true). Union all + un solo AnyAsync: una sola consulta, el planner de Postgres corta
        // apenas encuentra la primera fila.
        var tieneDatos = await db.Pedidos.Where(p => p.ClienteId == id).Select(_ => 1)
            .Concat(db.Tarifas.Where(t => t.ClienteId == id).Select(_ => 1))
            .Concat(db.EventosCliente.Where(e => e.ClienteId == id).Select(_ => 1))
            .Concat(db.ClientesUsuarios.Where(u => u.ClienteId == id).Select(_ => 1))
            .Concat(db.Facturas.Where(f => f.ClienteId == id).Select(_ => 1))
            .Concat(db.Pagos.Where(p => p.ClienteId == id).Select(_ => 1))
            .AnyAsync(ct);
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
        if (!Validaciones.EmailValido(req.Email)) return BadRequest("El email no es válido.");
        if (PoliticaContrasena.Validar(req.Password, req.Email) is { } errorClave) return BadRequest(errorClave);

        var usuario = new ClienteUsuario
        {
            Id = Guid.NewGuid(),
            ClienteId = id,
            Nombre = req.Nombre,
            Email = req.Email.Trim(),
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
        var usuario = await db.ClientesUsuarios.SingleOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == id, ct);
        if (usuario is null) return NotFound();
        if (PoliticaContrasena.Validar(req.Password, usuario.Email) is { } errorClave) return BadRequest(errorClave);

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
        var servicioCortado = await cuentaCorriente.ServicioCortadoAsync(id, hoy, deudaVencida, ct);

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
