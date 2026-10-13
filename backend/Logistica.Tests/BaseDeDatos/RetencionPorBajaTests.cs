using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// Baja de una empresa por falta de pago: "los pedidos quedan retenidos o en su caso devueltos, pero ya no
// deben permanecer en la ruta o pendientes para entregar". Al quedar inactiva, sus pedidos pendientes
// pasan a Retenido y salen de sus rutas sin cobrar nada; lo ya resuelto no se toca, y las rutas que los
// llevaban tienen que poder seguir y cerrarse para los pedidos de las demás empresas.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class RetencionPorBajaTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly Dia = new(2026, 10, 5); // la fecha de BaseDePrueba.RutaAsync

    private static readonly IOptions<OpcionesPruebaEntrega> OpcionesDePrueba =
        Options.Create(new OpcionesPruebaEntrega { RaizFotos = "no-se-usa", MotivosFallo = ["ausente"] });

    private sealed class SinFotos() : AlmacenamientoFotos(OpcionesDePrueba)
    {
        public override Task<Stream?> AbrirAsync(string rutaRelativa, CancellationToken ct) => Task.FromResult<Stream?>(null);
        protected override Task EscribirAsync(string rutaRelativa, Stream contenido, CancellationToken ct) => Task.CompletedTask;
    }

    // De cada controller solo se usa la base y lo que se le pasa: el resto no se toca.
    private static ClientesController Clientes(LogisticaDbContext db, Guid usuarioId) =>
        new ClientesController(db, null!, null!, null!, null!, null!, new RetencionPedidosService(db)).ComoUsuario(usuarioId);

    private static RutasController Rutas(LogisticaDbContext db, Guid usuarioId) =>
        new RutasController(db, null!, null!, null!, null!, new LiquidacionService(db), null!).ComoUsuario(usuarioId);

    private static PedidosController Pedidos(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!, null!, null!, new RetencionPedidosService(db))
            .ComoUsuario(usuarioId);

    private async Task<Pedido> PedidoDeAsync(int clienteId, EstadoPedido estado, long? destinoId = null, long? viajeId = null)
    {
        var origen = await baseDatos.UbicacionAsync();
        destinoId ??= (await baseDatos.UbicacionAsync()).Id;

        await using var db = baseDatos.CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = clienteId,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destinoId.Value,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = Dia,
            Estado = estado,
            ViajeId = viajeId,
        };
        if (estado != EstadoPedido.Borrador)
        {
            pedido.PrecioBase = 1000m;
            pedido.Total = 1000m;
        }
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    /// <summary>La baja por cualquiera de los dos caminos que escriben Cliente.Activo: el botón del
    /// listado (PUT {id}/activo) o el formulario completo de edición (PUT {id}).</summary>
    private async Task CambiarActivoAsync(int clienteId, Guid actor, bool activo = false, bool porEdicion = false)
    {
        await using var db = baseDatos.CrearContexto();
        IActionResult respuesta;
        if (porEdicion)
        {
            var c = await db.Clientes.AsNoTracking().SingleAsync(x => x.Id == clienteId);
            respuesta = await Clientes(db, actor).Actualizar(
                clienteId,
                new ClientesController.ActualizarClienteRequest(
                    c.RazonSocial, c.Cuit, c.Contacto, c.Telefono, c.Email, activo,
                    c.ColorPago, c.ColorTrato, c.ColorOper, c.CicloFacturacion),
                CancellationToken.None);
        }
        else
        {
            respuesta = await Clientes(db, actor).CambiarActivo(
                clienteId, new ClientesController.ActivoRequest(activo), CancellationToken.None);
        }
        Assert.IsType<NoContentResult>(respuesta);
    }

    private async Task<RutaParada> ParadaConAsync(long rutaId, int orden, long ubicacionId, params Pedido[] pedidos)
    {
        await using var db = baseDatos.CrearContexto();
        var parada = new RutaParada { RutaId = rutaId, UbicacionId = ubicacionId, Tipo = "entrega", Orden = orden };
        db.RutaParadas.Add(parada);
        await db.SaveChangesAsync();
        foreach (var pedido in pedidos)
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedido.Id });
        await db.SaveChangesAsync();
        return parada;
    }

    /// <summary>Una ruta que el repartidor ya tiene en la calle, con el retiro firmado.</summary>
    private async Task<(Ruta Ruta, Guid RepartidorId)> RutaEnCursoAsync()
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        await baseDatos.EjecutarAsync(
            "update rutas set estado = 'en_curso', retiro_confirmado_en = now(), retiro_bultos_esperados = 2, " +
            $"retiro_bultos_contados = 2 where id = {ruta.Id}");
        return (ruta, repartidor.Id);
    }

    private async Task<EstadoPedido> EstadoAsync(long pedidoId)
    {
        await using var db = baseDatos.CrearContexto();
        return (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Estado;
    }

    private async Task<string> EstadoParadaAsync(long paradaId)
    {
        await using var db = baseDatos.CrearContexto();
        return (await db.RutaParadas.AsNoTracking().SingleAsync(p => p.Id == paradaId)).Estado;
    }

    private async Task<IActionResult> CerrarRutaAsync(long rutaId, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        return await Rutas(db, actor).Cerrar(
            rutaId,
            new RutasController.CerrarRutaRequest(
                KmInicial: 1000, KmFinal: 1080, CombustibleMonto: 5000m, PeajesMonto: 0m, OtrosCostos: 0m,
                PagoRepartidor: 0m, NotasCierre: null, SinDeclaracionDelRepartidor: true),
            CancellationToken.None);
    }

    // ---- Qué pedidos se retienen ----

    [Theory]
    [InlineData(EstadoPedido.Borrador, false)]
    [InlineData(EstadoPedido.Confirmado, false)]
    [InlineData(EstadoPedido.EnRuta, false)]
    [InlineData(EstadoPedido.Reprogramado, false)]
    [InlineData(EstadoPedido.Borrador, true)]
    [InlineData(EstadoPedido.EnRuta, true)]
    public async Task La_baja_retiene_el_pedido_pendiente_y_deja_registrado_por_que(EstadoPedido estado, bool porEdicion)
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, estado);

        await CambiarActivoAsync(cliente.Id, actor.Id, porEdicion: porEdicion);

        await using var db = baseDatos.CrearContexto();
        Assert.False((await db.Clientes.SingleAsync(c => c.Id == cliente.Id)).Activo);
        Assert.Equal(EstadoPedido.Retenido, (await db.Pedidos.SingleAsync(p => p.Id == pedido.Id)).Estado);

        var evento = await db.PedidoEventos.Where(e => e.PedidoId == pedido.Id).OrderBy(e => e.Id).LastAsync();
        Assert.Equal(estado, evento.EstadoAnterior);
        Assert.Equal(EstadoPedido.Retenido, evento.EstadoNuevo);
        Assert.Equal(actor.Id, evento.ActorUsuarioId);
        Assert.Contains("falta de pago", evento.Motivo);
    }

    [Theory]
    [InlineData(EstadoPedido.Entregado)]
    [InlineData(EstadoPedido.Fallido)]
    [InlineData(EstadoPedido.Devuelto)]
    [InlineData(EstadoPedido.Cancelado)]
    public async Task La_baja_no_toca_lo_que_ya_esta_resuelto(EstadoPedido estado)
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, estado);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using var db = baseDatos.CrearContexto();
        Assert.Equal(estado, (await db.Pedidos.SingleAsync(p => p.Id == pedido.Id)).Estado);
        Assert.Equal(1, await db.PedidoEventos.CountAsync(e => e.PedidoId == pedido.Id)); // solo el alta
    }

    [Fact]
    public async Task La_baja_no_toca_los_pedidos_de_otra_empresa()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal(EstadoPedido.EnRuta, await EstadoAsync(ajeno.Id));
    }

    [Fact]
    public async Task Reactivar_la_empresa_no_reanuda_nada()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.Confirmado);
        await CambiarActivoAsync(cliente.Id, actor.Id);

        await CambiarActivoAsync(cliente.Id, actor.Id, activo: true);

        await using var db = baseDatos.CrearContexto();
        Assert.True((await db.Clientes.SingleAsync(c => c.Id == cliente.Id)).Activo);
        Assert.Equal(EstadoPedido.Retenido, (await db.Pedidos.SingleAsync(p => p.Id == pedido.Id)).Estado);
    }

    // ---- Empresas que ya estaban inactivas antes de esta regla ----

    /// <summary>Una empresa dada de baja antes de que existiera la retención: inactiva en la base, sin
    /// haber pasado por el controller, con sus pedidos pendientes todavía vivos.</summary>
    private async Task<Cliente> ClienteInactivoDeAntesAsync()
    {
        var cliente = await baseDatos.ClienteAsync();
        await baseDatos.EjecutarAsync($"update clientes set activo = false where id = {cliente.Id}");
        return cliente;
    }

    private async Task<ClientesController.RetencionDeInactivas> RetenerPendientesDeInactivasAsync(Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(
            await Clientes(db, actor).RetenerPendientesDeInactivas(CancellationToken.None));
        return Assert.IsType<ClientesController.RetencionDeInactivas>(respuesta.Value);
    }

    [Fact]
    public async Task La_pasada_retiene_los_pendientes_de_la_empresa_que_ya_estaba_inactiva_y_deja_registrado_quien_y_por_que()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await ClienteInactivoDeAntesAsync();
        var confirmado = await PedidoDeAsync(cliente.Id, EstadoPedido.Confirmado);
        var enRuta = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        var entregado = await PedidoDeAsync(cliente.Id, EstadoPedido.Entregado);

        var resultado = await RetenerPendientesDeInactivasAsync(actor.Id);

        var empresa = Assert.Single(resultado.Detalle, e => e.ClienteId == cliente.Id);
        Assert.Equal(cliente.RazonSocial, empresa.RazonSocial);
        Assert.Equal(2, empresa.Pedidos);
        Assert.Equal(resultado.Detalle.Count, resultado.Empresas);
        Assert.Equal(resultado.Detalle.Sum(e => e.Pedidos), resultado.Pedidos);

        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(confirmado.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(enRuta.Id));
        Assert.Equal(EstadoPedido.Entregado, await EstadoAsync(entregado.Id)); // lo resuelto no se toca

        await using var db = baseDatos.CrearContexto();
        var evento = await db.PedidoEventos.Where(e => e.PedidoId == enRuta.Id).OrderBy(e => e.Id).LastAsync();
        Assert.Equal(EstadoPedido.EnRuta, evento.EstadoAnterior);
        Assert.Equal(EstadoPedido.Retenido, evento.EstadoNuevo);
        Assert.Equal(actor.Id, evento.ActorUsuarioId);
        Assert.Contains("falta de pago", evento.Motivo);
    }

    [Fact]
    public async Task La_pasada_saca_al_retenido_del_armado_igual_que_una_baja_comun()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await ClienteInactivoDeAntesAsync();
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.Reprogramado);
        var consolidado = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador, ajeno.DestinoUbicacionId);
        var ruta = await baseDatos.RutaAsync();
        await ParadaConAsync(ruta.Id, 1, ajeno.DestinoUbicacionId, consolidado, ajeno);

        await RetenerPendientesDeInactivasAsync(actor.Id);

        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(consolidado.Id));
        await using var db = baseDatos.CrearContexto();
        var filas = await db.ParadaPedidos.Where(pp => pp.Parada.RutaId == ruta.Id).Select(pp => pp.PedidoId).ToListAsync();
        Assert.Equal([ajeno.Id], filas);
    }

    [Fact]
    public async Task La_pasada_no_toca_a_una_empresa_activa()
    {
        var actor = await baseDatos.UsuarioAsync();
        var inactiva = await ClienteInactivoDeAntesAsync();
        await PedidoDeAsync(inactiva.Id, EstadoPedido.Confirmado);
        var activa = await baseDatos.ClienteAsync();
        var vivo = await PedidoDeAsync(activa.Id, EstadoPedido.Confirmado);

        var resultado = await RetenerPendientesDeInactivasAsync(actor.Id);

        Assert.Contains(resultado.Detalle, e => e.ClienteId == inactiva.Id);
        Assert.DoesNotContain(resultado.Detalle, e => e.ClienteId == activa.Id);
        Assert.Equal(EstadoPedido.Confirmado, await EstadoAsync(vivo.Id));
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(1, await db.PedidoEventos.CountAsync(e => e.PedidoId == vivo.Id)); // solo el alta
    }

    [Fact]
    public async Task Repetir_la_pasada_no_retiene_nada_mas()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await ClienteInactivoDeAntesAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.Reprogramado);
        var primera = await RetenerPendientesDeInactivasAsync(actor.Id);
        Assert.Contains(primera.Detalle, e => e.ClienteId == cliente.Id);

        var segunda = await RetenerPendientesDeInactivasAsync(actor.Id);

        Assert.Equal(0, segunda.Empresas);
        Assert.Equal(0, segunda.Pedidos);
        Assert.Empty(segunda.Detalle);
        await using var db = baseDatos.CrearContexto();
        // El alta y la retención: la segunda pasada no dejó otro evento.
        Assert.Equal(2, await db.PedidoEventos.CountAsync(e => e.PedidoId == pedido.Id));
    }

    // ---- Sin cargo ----

    [Fact]
    public async Task Retener_no_cobra_nada_ni_cuenta_como_fallo_o_reprogramacion()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var enRuta = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        var reprogramado = await PedidoDeAsync(cliente.Id, EstadoPedido.Reprogramado);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using var db = baseDatos.CrearContexto();
        long[] ids = [enRuta.Id, reprogramado.Id];
        Assert.Equal(0, await db.FacturaItems.CountAsync(i => i.PedidoId != null && ids.Contains(i.PedidoId.Value)));
        Assert.Equal(0, await db.PedidoEventos.CountAsync(e => ids.Contains(e.PedidoId) && e.EstadoNuevo == EstadoPedido.Fallido));
        // El reprogramado ya traía una (la de su alta en esta prueba): retenerlo no suma otra.
        Assert.Equal(1, await db.PedidoEventos.CountAsync(
            e => e.PedidoId == reprogramado.Id && e.EstadoNuevo == EstadoPedido.Reprogramado));
    }

    // ---- Ruta en planificación ----

    [Fact]
    public async Task El_retenido_sale_del_armado_y_la_planificacion_cierra_para_el_pedido_de_otra_empresa()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var solo = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador);
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.Reprogramado); // con precio: el cierre no cotiza
        var consolidado = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador, ajeno.DestinoUbicacionId);
        var ruta = await baseDatos.RutaAsync();
        var paradaSola = await ParadaConAsync(ruta.Id, 1, solo.DestinoUbicacionId, solo);
        var paradaCompartida = await ParadaConAsync(ruta.Id, 2, ajeno.DestinoUbicacionId, consolidado, ajeno);
        await CompletarRutaAsync(ruta.Id);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using (var db = baseDatos.CrearContexto())
        {
            // La parada que solo tenía al retenido desaparece; la compartida queda con el ajeno, primera.
            var paradas = await db.RutaParadas.Where(p => p.RutaId == ruta.Id).ToListAsync();
            var queda = Assert.Single(paradas);
            Assert.Equal(paradaCompartida.Id, queda.Id);
            Assert.Equal(1, queda.Orden);
            Assert.DoesNotContain(paradas, p => p.Id == paradaSola.Id);
            var filas = await db.ParadaPedidos.Where(pp => pp.Parada.RutaId == ruta.Id).Select(pp => pp.PedidoId).ToListAsync();
            Assert.Equal([ajeno.Id], filas);

            Assert.IsType<NoContentResult>(await Rutas(db, actor.Id).CerrarPlanificacion(ruta.Id, CancellationToken.None));
        }

        Assert.Equal(EstadoPedido.EnRuta, await EstadoAsync(ajeno.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(solo.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(consolidado.Id));
    }

    /// <summary>Lo que CerrarPlanificacion exige de la ruta: vehículo, repartidor y punto de partida.</summary>
    private async Task CompletarRutaAsync(long rutaId)
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var origen = await baseDatos.UbicacionAsync();
        await using var db = baseDatos.CrearContexto();
        var vehiculo = new Vehiculo { Patente = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant() };
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync();
        var ruta = await db.Rutas.SingleAsync(r => r.Id == rutaId);
        ruta.VehiculoId = vehiculo.Id;
        ruta.RepartidorId = repartidor.Id;
        ruta.OrigenUbicacionId = origen.Id;
        await db.SaveChangesAsync();
    }

    // ---- Ruta en curso ----

    [Fact]
    public async Task En_una_ruta_en_curso_el_repartidor_recibe_el_aviso_y_la_parada_compartida_y_la_ruta_se_pueden_cerrar()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);
        var retenido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta, ajeno.DestinoUbicacionId);
        var (ruta, repartidorId) = await RutaEnCursoAsync();
        var parada = await ParadaConAsync(ruta.Id, 1, ajeno.DestinoUbicacionId, retenido, ajeno);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(retenido.Id));
        Assert.Equal("pendiente", await EstadoParadaAsync(parada.Id)); // sigue habiendo algo para entregar
        await using (var db = baseDatos.CrearContexto())
        {
            var aviso = await db.Novedades.SingleAsync(n => n.PedidoId == retenido.Id);
            Assert.Equal(ruta.Id, aviso.RutaId);
            Assert.Equal(parada.Id, aviso.ParadaId);
            Assert.Equal("cancelacion", aviso.Tipo);
            Assert.Equal("operacion", aviso.Origen);
            Assert.Equal(actor.Id, aviso.CreadaPor);
            Assert.Contains("suspendi", aviso.Descripcion);
            Assert.Contains("falta de pago", aviso.Descripcion);
        }

        // El repartidor entrega lo que queda de la parada: el retenido no se entrega ni la bloquea.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        await using (var db = baseDatos.CrearContexto())
        {
            var respuesta = Assert.IsType<OkObjectResult>(
                await new MisParadasController(db, OpcionesDePrueba, new SinFotos(), null!, new CuentaCorrienteService(db))
                    .ComoUsuario(repartidorId)
                    .Cerrar(parada.Id, new MisParadasController.CerrarParadaRequest
                    {
                        DeviceUuid = Guid.NewGuid().ToString(),
                        Resultado = "entregado",
                        CapturadaEn = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero),
                        ReceptorNombre = "Quien recibe",
                        DocumentoNumero = "12345678",
                        Foto = new FormFile(new MemoryStream(jpeg), 0, jpeg.Length, "foto", "foto.jpg"),
                    }, CancellationToken.None));
            var cierre = Assert.IsType<MisParadasController.CierreResultado>(respuesta.Value);
            Assert.Equal(1, cierre.PedidosActualizados);
            Assert.Equal("completada", cierre.EstadoParada);
        }
        Assert.Equal(EstadoPedido.Entregado, await EstadoAsync(ajeno.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(retenido.Id));

        Assert.IsType<OkObjectResult>(await CerrarRutaAsync(ruta.Id, actor.Id));
        await using var lectura = baseDatos.CrearContexto();
        Assert.Equal("cerrada", (await lectura.Rutas.SingleAsync(r => r.Id == ruta.Id)).Estado);
        Assert.Equal(0, await lectura.FacturaItems.CountAsync(i => i.PedidoId == retenido.Id));
    }

    [Fact]
    public async Task La_parada_que_se_queda_sin_nada_para_entregar_se_cancela_y_no_bloquea_el_cierre_de_la_ruta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var retenido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        var (ruta, _) = await RutaEnCursoAsync();
        var parada = await ParadaConAsync(ruta.Id, 1, retenido.DestinoUbicacionId, retenido);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal("cancelada", await EstadoParadaAsync(parada.Id));
        await using (var db = baseDatos.CrearContexto())
            Assert.True(await db.ParadaPedidos.AnyAsync(pp => pp.ParadaId == parada.Id && pp.PedidoId == retenido.Id)); // historia
        Assert.IsType<OkObjectResult>(await CerrarRutaAsync(ruta.Id, actor.Id));
    }

    [Fact]
    public async Task La_parada_del_intento_que_ya_fallo_no_se_toca_ni_genera_aviso()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var reprogramado = await PedidoDeAsync(cliente.Id, EstadoPedido.Reprogramado);
        var (ruta, _) = await RutaEnCursoAsync();
        var parada = await ParadaConAsync(ruta.Id, 1, reprogramado.DestinoUbicacionId, reprogramado);
        await baseDatos.EjecutarAsync($"update ruta_paradas set estado = 'fallida' where id = {parada.Id}");

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(reprogramado.Id));
        Assert.Equal("fallida", await EstadoParadaAsync(parada.Id));
        await using var db = baseDatos.CrearContexto();
        Assert.False(await db.Novedades.AnyAsync(n => n.PedidoId == reprogramado.Id));
    }

    [Fact]
    public async Task Cancelar_el_ultimo_pedido_vivo_de_una_parada_con_un_retenido_cancela_la_parada()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);
        var retenido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta, ajeno.DestinoUbicacionId);
        var (ruta, _) = await RutaEnCursoAsync();
        var parada = await ParadaConAsync(ruta.Id, 1, ajeno.DestinoUbicacionId, retenido, ajeno);
        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Pedidos(db, actor.Id).CambiarEstado(
                ajeno.Id, new PedidosController.CambiarEstadoRequest("Cancelado", "El cliente lo pidió", null), CancellationToken.None));

        Assert.Equal("cancelada", await EstadoParadaAsync(parada.Id));
    }

    // ---- Viajes ----

    private async Task<Viaje> ViajeAsync(int clienteId, Guid creadoPor, long? rutaId)
    {
        var origen = await baseDatos.UbicacionAsync();
        await using var db = baseDatos.CrearContexto();
        var viaje = new Viaje
        {
            ClienteId = clienteId, FechaEntrega = Dia, OrigenUbicacionId = origen.Id, RutaId = rutaId,
            CreadoPorUsuarioId = creadoPor, CreadoEn = DateTimeOffset.UtcNow,
        };
        db.Viajes.Add(viaje);
        await db.SaveChangesAsync();
        return viaje;
    }

    [Fact]
    public async Task El_viaje_con_la_ruta_todavia_planificada_pierde_la_ruta_propuesta_y_sus_envios_quedan_retenidos()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var ruta = await baseDatos.RutaAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id, ruta.Id);
        var primero = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador, viajeId: viaje.Id);
        var segundo = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador, viajeId: viaje.Id);
        await ParadaConAsync(ruta.Id, 1, primero.DestinoUbicacionId, primero);
        await ParadaConAsync(ruta.Id, 2, segundo.DestinoUbicacionId, segundo);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(primero.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(segundo.Id));
        await using var db = baseDatos.CrearContexto();
        Assert.False(await db.Rutas.AnyAsync(r => r.Id == ruta.Id));
        Assert.False(await db.RutaParadas.AnyAsync(p => p.RutaId == ruta.Id));
        var guardado = await db.Viajes.SingleAsync(v => v.Id == viaje.Id);
        Assert.Null(guardado.RutaId);
        Assert.Equal(EstadosViaje.Activo, guardado.Estado); // retenido no es cancelado: se rearma al reanudar
    }

    [Fact]
    public async Task El_viaje_con_la_ruta_en_curso_conserva_la_ruta_y_sus_envios_pendientes_quedan_retenidos()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var (ruta, _) = await RutaEnCursoAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id, ruta.Id);
        var entregado = await PedidoDeAsync(cliente.Id, EstadoPedido.Entregado, viajeId: viaje.Id);
        var pendiente = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta, viajeId: viaje.Id);
        var paradaHecha = await ParadaConAsync(ruta.Id, 1, entregado.DestinoUbicacionId, entregado);
        await baseDatos.EjecutarAsync($"update ruta_paradas set estado = 'completada' where id = {paradaHecha.Id}");
        var paradaPendiente = await ParadaConAsync(ruta.Id, 2, pendiente.DestinoUbicacionId, pendiente);

        await CambiarActivoAsync(cliente.Id, actor.Id);

        Assert.Equal(EstadoPedido.Entregado, await EstadoAsync(entregado.Id));
        Assert.Equal(EstadoPedido.Retenido, await EstadoAsync(pendiente.Id));
        Assert.Equal("completada", await EstadoParadaAsync(paradaHecha.Id));
        Assert.Equal("cancelada", await EstadoParadaAsync(paradaPendiente.Id));
        await using (var db = baseDatos.CrearContexto())
            Assert.Equal(ruta.Id, (await db.Viajes.SingleAsync(v => v.Id == viaje.Id)).RutaId);
        Assert.IsType<OkObjectResult>(await CerrarRutaAsync(ruta.Id, actor.Id));
    }

    // ---- Nada lo devuelve a la calle ----

    [Fact]
    public async Task Un_retenido_no_es_candidato_de_ruta_ni_entra_en_un_armado()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador);
        await CambiarActivoAsync(cliente.Id, actor.Id);
        var ruta = await baseDatos.RutaAsync();

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(
            await Pedidos(db, actor.Id).CandidatosRuta(Dia, null, soloBorrador: false, CancellationToken.None));
        var candidatos = Assert.IsAssignableFrom<IEnumerable<PedidosController.CandidatoRuta>>(respuesta.Value);
        Assert.DoesNotContain(candidatos, c => c.PedidoId == pedido.Id);

        Assert.IsType<BadRequestObjectResult>(await Rutas(db, actor.Id).GuardarParadas(
            ruta.Id,
            new RutasController.GuardarParadasRequest(
                [new RutasController.ParadaArmadoRequest(pedido.DestinoUbicacionId, false, [pedido.Id])]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Un_fallido_de_una_empresa_inactiva_no_se_reprograma()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var fallido = await PedidoDeAsync(cliente.Id, EstadoPedido.Fallido);
        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<ConflictObjectResult>(await Pedidos(db, actor.Id).CambiarEstado(
            fallido.Id, new PedidosController.CambiarEstadoRequest("Reprogramado", null, Dia.AddDays(2)), CancellationToken.None));

        Assert.Contains("baja", Assert.IsType<string>(respuesta.Value));
        Assert.Equal(EstadoPedido.Fallido, await EstadoAsync(fallido.Id));
    }

    [Fact]
    public async Task Un_retenido_no_admite_ajustes()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        await CambiarActivoAsync(cliente.Id, actor.Id);

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<ConflictObjectResult>(await Pedidos(db, actor.Id).SolicitarAjuste(
            pedido.Id, new PedidosController.SolicitarAjusteRequest(BultosReales: 3, "Vino un bulto de más."), CancellationToken.None));

        Assert.Contains("suspendido", Assert.IsType<string>(respuesta.Value));
        Assert.Equal(0, await db.FacturaItems.CountAsync(i => i.PedidoId == pedido.Id));
    }
}
