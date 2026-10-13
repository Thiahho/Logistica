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

// Qué se hace con un pedido Retenido (empresa dada de baja por falta de pago). Lo decide operación,
// pedido por pedido: reanudarlo — solo con la empresa otra vez activa, con el precio que ya tenía
// congelado y sin gastar una reprogramación sin cargo — o marcarlo devuelto. Cancelarlo no: cancelar
// un pedido ya confirmado cobra el 100%, y retener no cobra nada.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class ReanudacionDeRetenidosTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly Dia = new(2026, 10, 5); // la fecha de BaseDePrueba.RutaAsync
    private static readonly DateOnly NuevaFecha = new(2026, 10, 20);

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

    private static PedidosController Pedidos(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(
                db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!,
                Options.Create(new OpcionesPortal()), null!, new RetencionPedidosService(db))
            .ComoUsuario(usuarioId);

    private async Task<Pedido> PedidoDeAsync(int clienteId, EstadoPedido estado, long? destinoId = null)
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
        };
        if (estado != EstadoPedido.Borrador)
        {
            pedido.PrecioBase = 1000m;
            pedido.Total = 1000m;
            pedido.PrecioCongeladoEn = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        }
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private async Task CambiarActivoAsync(int clienteId, Guid actor, bool activo)
    {
        await using var db = baseDatos.CrearContexto();
        Assert.IsType<NoContentResult>(await Clientes(db, actor).CambiarActivo(
            clienteId, new ClientesController.ActivoRequest(activo), CancellationToken.None));
    }

    /// <summary>Un pedido que estaba en `estado` y quedó Retenido por la baja de su empresa, que sigue
    /// inactiva salvo que se pida reactivarla.</summary>
    private async Task<Pedido> RetenidoAsync(Guid actor, EstadoPedido estado, bool reactivar = true)
    {
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, estado);
        await CambiarActivoAsync(cliente.Id, actor, activo: false);
        if (reactivar) await CambiarActivoAsync(cliente.Id, actor, activo: true);
        return pedido;
    }

    private async Task<IActionResult> CambiarEstadoAsync(long pedidoId, Guid actor, string estado, DateOnly? fecha = null, string? motivo = null)
    {
        await using var db = baseDatos.CrearContexto();
        return await Pedidos(db, actor).CambiarEstado(
            pedidoId, new PedidosController.CambiarEstadoRequest(estado, motivo, fecha), CancellationToken.None);
    }

    private async Task<Pedido> LeerAsync(long pedidoId)
    {
        await using var db = baseDatos.CrearContexto();
        return await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
    }

    private async Task<PedidosController.PedidoDetalle> DetalleAsync(long pedidoId, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(await Pedidos(db, actor).Detalle(pedidoId, CancellationToken.None));
        return Assert.IsType<PedidosController.PedidoDetalle>(respuesta.Value);
    }

    // ---- Reanudar ----

    [Theory]
    [InlineData(EstadoPedido.EnRuta, "Reprogramado")]
    [InlineData(EstadoPedido.Borrador, "Borrador")]
    public async Task Con_la_empresa_todavia_inactiva_no_se_reanuda(EstadoPedido estado, string destino)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, estado, reactivar: false);

        var respuesta = Assert.IsType<ConflictObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, destino, NuevaFecha));

        Assert.Contains("baja por falta de pago", Assert.IsType<string>(respuesta.Value));
        Assert.Equal(EstadoPedido.Retenido, (await LeerAsync(pedido.Id)).Estado);
    }

    [Theory]
    [InlineData(EstadoPedido.Confirmado)]
    [InlineData(EstadoPedido.EnRuta)]
    [InlineData(EstadoPedido.Reprogramado)]
    public async Task El_que_ya_tenia_precio_se_reanuda_como_reprogramado_con_la_fecha_nueva_y_el_mismo_precio(EstadoPedido estado)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, estado);
        Assert.Equal("Reprogramado", (await DetalleAsync(pedido.Id, actor.Id)).ReanudaEn);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha, "La empresa pagó."));

        var guardado = await LeerAsync(pedido.Id);
        Assert.Equal(EstadoPedido.Reprogramado, guardado.Estado);
        Assert.Equal(NuevaFecha, guardado.FechaEntrega);
        Assert.Equal(1000m, guardado.PrecioBase);
        Assert.Equal(1000m, guardado.Total);
        Assert.Equal(pedido.PrecioCongeladoEn, guardado.PrecioCongeladoEn);

        await using var db = baseDatos.CrearContexto();
        var evento = await db.PedidoEventos.Where(e => e.PedidoId == pedido.Id).OrderBy(e => e.Id).LastAsync();
        Assert.Equal(EstadoPedido.Retenido, evento.EstadoAnterior);
        Assert.Equal(EstadoPedido.Reprogramado, evento.EstadoNuevo);
        Assert.Equal(actor.Id, evento.ActorUsuarioId);
        Assert.Equal("La empresa pagó.", evento.Motivo);
        Assert.Equal(0, await db.FacturaItems.CountAsync(i => i.PedidoId == pedido.Id));
        Assert.Null((await DetalleAsync(pedido.Id, actor.Id)).ReanudaEn); // ya no está retenido
    }

    [Fact]
    public async Task Reanudar_como_reprogramado_exige_la_fecha_nueva()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, EstadoPedido.EnRuta);

        Assert.IsType<BadRequestObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado"));

        Assert.Equal(EstadoPedido.Retenido, (await LeerAsync(pedido.Id)).Estado);
    }

    [Fact]
    public async Task El_que_nunca_se_confirmo_vuelve_a_borrador_y_a_ser_candidato_de_ruta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, EstadoPedido.Borrador);
        Assert.Equal("Borrador", (await DetalleAsync(pedido.Id, actor.Id)).ReanudaEn);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Borrador", NuevaFecha));

        var guardado = await LeerAsync(pedido.Id);
        Assert.Equal(EstadoPedido.Borrador, guardado.Estado);
        Assert.Equal(NuevaFecha, guardado.FechaEntrega);
        Assert.Null(guardado.Total);
        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(
            await Pedidos(db, actor.Id).CandidatosRuta(NuevaFecha, null, soloBorrador: true, CancellationToken.None));
        var candidatos = Assert.IsAssignableFrom<IEnumerable<PedidosController.CandidatoRuta>>(respuesta.Value);
        Assert.Contains(candidatos, c => c.PedidoId == pedido.Id);
    }

    [Fact]
    public async Task Volver_a_borrador_sin_fecha_conserva_la_que_tenia()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, EstadoPedido.Borrador);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Borrador"));

        Assert.Equal(Dia, (await LeerAsync(pedido.Id)).FechaEntrega);
    }

    [Theory]
    [InlineData(EstadoPedido.Borrador, "Reprogramado", "borrador")]
    [InlineData(EstadoPedido.EnRuta, "Borrador", "reprogramado")]
    public async Task Cada_retenido_se_reanuda_solo_hacia_donde_le_corresponde(EstadoPedido estado, string pedidoPorError, string correcto)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, estado);

        var respuesta = Assert.IsType<ConflictObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, pedidoPorError, NuevaFecha));

        Assert.Contains(correcto, Assert.IsType<string>(respuesta.Value));
        var guardado = await LeerAsync(pedido.Id);
        Assert.Equal(EstadoPedido.Retenido, guardado.Estado);
        Assert.Equal(Dia, guardado.FechaEntrega);
    }

    // D13 (§10.2-M): tres reprogramaciones sin cargo, la cuarta genera un reintento con precio propio.
    [Fact]
    public async Task Retener_y_reanudar_no_consume_una_reprogramacion_sin_cargo()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.Fallido);
        async Task FallaDeNuevoAsync() =>
            await baseDatos.EjecutarAsync($"update pedidos set estado = 'fallido' where id = {pedido.Id}");

        // Dos reprogramaciones de verdad.
        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));
        await FallaDeNuevoAsync();
        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));

        // La empresa se da de baja con el pedido reprogramado, paga, y operación lo reanuda.
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: false);
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: true);
        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));
        Assert.Equal(2, (await DetalleAsync(pedido.Id, actor.Id)).VecesReprogramado);

        // La tercera sigue siendo sin cargo: si la reanudación contara, esta ya generaría el reintento.
        await FallaDeNuevoAsync();
        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));
        Assert.Equal(EstadoPedido.Reprogramado, (await LeerAsync(pedido.Id)).Estado);

        await FallaDeNuevoAsync();
        var cuarta = Assert.IsType<OkObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));
        Assert.Equal(3, Assert.IsType<PedidosController.ReintentoCreado>(cuarta.Value).Reprogramaciones);
    }

    // ---- Devolver ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Un_retenido_se_marca_devuelto_sin_generar_el_pedido_de_retorno_ni_cobrar(bool empresaReactivada)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, EstadoPedido.EnRuta, reactivar: empresaReactivada);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Devuelto", motivo: "Se devolvió la mercadería."));

        Assert.Equal(EstadoPedido.Devuelto, (await LeerAsync(pedido.Id)).Estado);
        await using (var db = baseDatos.CrearContexto())
        {
            Assert.False(await db.Pedidos.AnyAsync(p => p.PedidoOrigenId == pedido.Id));
            Assert.Equal(0, await db.FacturaItems.CountAsync(i => i.PedidoId == pedido.Id));
        }
        // Terminal: de Devuelto no se sale.
        Assert.IsType<BadRequestObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Reprogramado", NuevaFecha));
        Assert.IsType<BadRequestObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Borrador"));
    }

    // El retorno es un pedido nuevo en Borrador y con precio: una empresa inactiva no admite pedidos nuevos.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Devolver_un_fallido_genera_el_retorno_solo_si_la_empresa_esta_activa(bool activa, bool hayRetorno)
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var pedido = await PedidoDeAsync(cliente.Id, EstadoPedido.Fallido);
        if (!activa) await CambiarActivoAsync(cliente.Id, actor.Id, activo: false);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Devuelto"));

        Assert.Equal(EstadoPedido.Devuelto, (await LeerAsync(pedido.Id)).Estado);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(hayRetorno, await db.Pedidos.AnyAsync(p => p.PedidoOrigenId == pedido.Id && p.Tipo == "retorno"));
    }

    // ---- Cancelar ----

    [Theory]
    [InlineData(EstadoPedido.EnRuta)]
    [InlineData(EstadoPedido.Borrador)]
    public async Task Un_retenido_no_se_cancela_ni_factura_nada(EstadoPedido estado)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await RetenidoAsync(actor.Id, estado);

        Assert.IsType<BadRequestObjectResult>(await CambiarEstadoAsync(pedido.Id, actor.Id, "Cancelado", motivo: "El cliente lo pidió"));

        Assert.Equal(EstadoPedido.Retenido, (await LeerAsync(pedido.Id)).Estado);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(0, await db.FacturaItems.CountAsync(i => i.PedidoId == pedido.Id));
    }

    // ---- La parada donde quedó ----

    /// <summary>Al dejar de estar retenido con su ruta todavía en la calle, el pedido sale de la parada que
    /// sigue pendiente: si no, su estado nuevo le impediría al repartidor cerrarla para el otro pedido.</summary>
    [Theory]
    [InlineData("Reprogramado")]
    [InlineData("Devuelto")]
    public async Task Al_reanudarlo_o_devolverlo_sale_de_la_parada_pendiente_y_el_repartidor_la_puede_cerrar(string destino)
    {
        var actor = await baseDatos.UsuarioAsync();
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var cliente = await baseDatos.ClienteAsync();
        var ajeno = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);
        var retenido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta, ajeno.DestinoUbicacionId);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        var parada = await baseDatos.ParadaAsync(ruta.Id, ajeno.DestinoUbicacionId);
        await using (var db = baseDatos.CrearContexto())
        {
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = retenido.Id });
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = ajeno.Id });
            await db.SaveChangesAsync();
        }
        await baseDatos.EjecutarAsync(
            "update rutas set estado = 'en_curso', retiro_confirmado_en = now(), retiro_bultos_esperados = 2, " +
            $"retiro_bultos_contados = 2 where id = {ruta.Id}");
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: false);
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: true);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(retenido.Id, actor.Id, destino, NuevaFecha));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        await using var lectura = baseDatos.CrearContexto();
        Assert.Equal([ajeno.Id], await lectura.ParadaPedidos.Where(pp => pp.ParadaId == parada.Id).Select(pp => pp.PedidoId).ToListAsync());
        var respuesta = Assert.IsType<OkObjectResult>(
            await new MisParadasController(lectura, OpcionesDePrueba, new SinFotos(), null!, new CuentaCorrienteService(lectura))
                .ComoUsuario(repartidor.Id)
                .Cerrar(parada.Id, new MisParadasController.CerrarParadaRequest
                {
                    DeviceUuid = Guid.NewGuid().ToString(),
                    Resultado = "entregado",
                    CapturadaEn = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero),
                    ReceptorNombre = "Quien recibe",
                    DocumentoNumero = "12345678",
                    Foto = new FormFile(new MemoryStream(jpeg), 0, jpeg.Length, "foto", "foto.jpg"),
                }, CancellationToken.None));
        Assert.Equal(1, Assert.IsType<MisParadasController.CierreResultado>(respuesta.Value).PedidosActualizados);
    }

    [Fact]
    public async Task La_fila_de_una_parada_ya_cancelada_queda_como_historia_al_reanudar()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var retenido = await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        var ruta = await baseDatos.RutaAsync();
        var parada = await baseDatos.ParadaAsync(ruta.Id, retenido.DestinoUbicacionId);
        await using (var db = baseDatos.CrearContexto())
        {
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = retenido.Id });
            await db.SaveChangesAsync();
        }
        await baseDatos.EjecutarAsync($"update rutas set estado = 'en_curso' where id = {ruta.Id}");
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: false);
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: true);

        Assert.IsType<NoContentResult>(await CambiarEstadoAsync(retenido.Id, actor.Id, "Reprogramado", NuevaFecha));

        await using var lectura = baseDatos.CrearContexto();
        Assert.True(await lectura.ParadaPedidos.AnyAsync(pp => pp.ParadaId == parada.Id && pp.PedidoId == retenido.Id));
        Assert.Equal("cancelada", (await lectura.RutaParadas.SingleAsync(p => p.Id == parada.Id)).Estado);
    }
}
