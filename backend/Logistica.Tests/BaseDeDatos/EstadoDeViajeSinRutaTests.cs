using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// Un viaje al que la baja de la empresa le quitó la ruta propuesta no la recupera: sus envíos, al
// reanudarse, se rutean como cualquier otro pedido. El estado que se ve del viaje — en el detalle y en
// los listados, del back-office y del portal — sale entonces de sus envíos. Un viaje que conserva su
// ruta se sigue leyendo de ella, como siempre.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class EstadoDeViajeSinRutaTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly Dia = new(2026, 10, 5); // la fecha de BaseDePrueba.RutaAsync

    // De cada uno solo se usa la base: las paradas de prueba no tienen coordenadas, así que el detalle
    // no llega a pedirle un recorrido al proveedor de ruteo.
    private static ViajeService Viajes(LogisticaDbContext db) => new(db, null!, new RuteoService(null!, null!, null!));

    private static ClientesController Clientes(LogisticaDbContext db, Guid usuarioId) =>
        new ClientesController(db, null!, null!, null!, null!, null!, new RetencionPedidosService(db)).ComoUsuario(usuarioId);

    private static PedidosController Pedidos(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(
                db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!,
                Options.Create(new OpcionesPortal()), null!, new RetencionPedidosService(db))
            .ComoUsuario(usuarioId);

    private async Task<Viaje> ViajeAsync(int clienteId, Guid creadoPor, long? rutaId = null, string estado = EstadosViaje.Activo)
    {
        var origen = await baseDatos.UbicacionAsync();
        await using var db = baseDatos.CrearContexto();
        var viaje = new Viaje
        {
            ClienteId = clienteId, FechaEntrega = Dia, OrigenUbicacionId = origen.Id, RutaId = rutaId, Estado = estado,
            CreadoPorUsuarioId = creadoPor, CreadoEn = DateTimeOffset.UtcNow,
        };
        db.Viajes.Add(viaje);
        await db.SaveChangesAsync();
        return viaje;
    }

    private async Task<Pedido> EnvioAsync(Viaje viaje, EstadoPedido estado, long? destinoId = null)
    {
        var origen = await baseDatos.UbicacionAsync();
        destinoId ??= (await baseDatos.UbicacionAsync()).Id;

        await using var db = baseDatos.CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = viaje.ClienteId,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destinoId.Value,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = Dia,
            Estado = estado,
            ViajeId = viaje.Id,
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

    private async Task<RutaParada> ParadaConAsync(long rutaId, int orden, Pedido pedido, string estado = "pendiente")
    {
        await using var db = baseDatos.CrearContexto();
        var parada = new RutaParada { RutaId = rutaId, UbicacionId = pedido.DestinoUbicacionId, Tipo = "entrega", Orden = orden };
        db.RutaParadas.Add(parada);
        await db.SaveChangesAsync();
        db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedido.Id });
        await db.SaveChangesAsync();
        if (estado != "pendiente")
            await baseDatos.EjecutarAsync($"update ruta_paradas set estado = '{estado}' where id = {parada.Id}");
        return parada;
    }

    /// <summary>Una ruta común — no es la propuesta de ningún viaje — que el repartidor ya tiene en la calle.</summary>
    private async Task<Ruta> RutaEnCursoAsync()
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        await baseDatos.EjecutarAsync(
            "update rutas set estado = 'en_curso', retiro_confirmado_en = now(), retiro_bultos_esperados = 2, " +
            $"retiro_bultos_contados = 2 where id = {ruta.Id}");
        return ruta;
    }

    private async Task CambiarActivoAsync(int clienteId, Guid actor, bool activo)
    {
        await using var db = baseDatos.CrearContexto();
        Assert.IsType<NoContentResult>(await Clientes(db, actor).CambiarActivo(
            clienteId, new ClientesController.ActivoRequest(activo), CancellationToken.None));
    }

    private async Task ReanudarAsync(long pedidoId, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        Assert.IsType<NoContentResult>(await Pedidos(db, actor).CambiarEstado(
            pedidoId, new PedidosController.CambiarEstadoRequest("Borrador", "La empresa pagó.", null), CancellationToken.None));
    }

    private async Task<ViajeDetalle> DetalleAsync(Viaje viaje)
    {
        await using var db = baseDatos.CrearContexto();
        return (await Viajes(db).DetalleAsync(viaje.Id, clienteId: null, conPrecios: true, CancellationToken.None))!;
    }

    /// <summary>El estado del viaje, que tiene que ser el mismo en las cuatro lecturas: detalle y listado,
    /// del back-office (sin acotar a un cliente) y del portal (acotado al suyo).</summary>
    private async Task<string> EstadoAsync(Viaje viaje)
    {
        await using var db = baseDatos.CrearContexto();
        var servicio = Viajes(db);
        var backOffice = (await servicio.DetalleAsync(viaje.Id, clienteId: null, conPrecios: true, CancellationToken.None))!.Estado;
        var portal = (await servicio.DetalleAsync(viaje.Id, viaje.ClienteId, conPrecios: false, CancellationToken.None))!.Estado;
        var listaPortal = (await servicio.ListarAsync(viaje.ClienteId, null, null, CancellationToken.None))
            .Items.Single(v => v.Id == viaje.Id).Estado;
        // El listado del back-office trae los viajes de todos los clientes: página grande para que entre.
        var listaBackOffice = (await servicio.ListarAsync(null, 1, 200, CancellationToken.None))
            .Items.SingleOrDefault(v => v.Id == viaje.Id)?.Estado ?? listaPortal;

        Assert.Equal(backOffice, portal);
        Assert.Equal(backOffice, listaPortal);
        Assert.Equal(backOffice, listaBackOffice);
        return backOffice;
    }

    // ---- De la baja a la calle ----

    [Fact]
    public async Task El_viaje_retenido_por_la_baja_se_lee_retenido_y_al_reanudar_sus_envios_sigue_el_camino_de_ellos()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var propuesta = await baseDatos.RutaAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id, propuesta.Id);
        var primero = await EnvioAsync(viaje, EstadoPedido.Borrador);
        var segundo = await EnvioAsync(viaje, EstadoPedido.Borrador);
        await ParadaConAsync(propuesta.Id, 1, primero);
        await ParadaConAsync(propuesta.Id, 2, segundo);
        Assert.Equal("planificado", await EstadoAsync(viaje));

        // La baja le quita la ruta propuesta y retiene sus envíos.
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: false);
        Assert.Equal("retenido", await EstadoAsync(viaje));

        // Reactivar la empresa no reanuda nada.
        await CambiarActivoAsync(cliente.Id, actor.Id, activo: true);
        Assert.Equal("retenido", await EstadoAsync(viaje));

        // Con un envío reanudado ya hay algo por planificar, aunque el otro siga retenido.
        await ReanudarAsync(primero.Id, actor.Id);
        Assert.Equal("sin_ruta", await EstadoAsync(viaje));
        await ReanudarAsync(segundo.Id, actor.Id);
        Assert.Equal("sin_ruta", await EstadoAsync(viaje));

        // Operación lo suma a una ruta común: el viaje no vuelve a tener una propia.
        var comun = await baseDatos.RutaAsync();
        await ParadaConAsync(comun.Id, 1, primero);
        Assert.Equal("planificado", await EstadoAsync(viaje));
        var detalle = await DetalleAsync(viaje);
        Assert.Null(detalle.RutaId);
        Assert.Null(detalle.RutaEstado);
    }

    [Fact]
    public async Task Con_un_envio_en_la_calle_en_una_ruta_comun_el_viaje_sin_ruta_propia_esta_en_curso()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id);
        var enLaCalle = await EnvioAsync(viaje, EstadoPedido.EnRuta);
        await EnvioAsync(viaje, EstadoPedido.Borrador);
        await EnvioAsync(viaje, EstadoPedido.Retenido);
        var ruta = await RutaEnCursoAsync();
        await ParadaConAsync(ruta.Id, 1, enLaCalle);

        Assert.Equal("en_curso", await EstadoAsync(viaje));
    }

    [Fact]
    public async Task Con_todos_sus_envios_resueltos_el_viaje_sin_ruta_propia_esta_cerrado()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id);
        var entregado = await EnvioAsync(viaje, EstadoPedido.Entregado);
        var fallido = await EnvioAsync(viaje, EstadoPedido.Fallido);
        await EnvioAsync(viaje, EstadoPedido.Devuelto);
        await EnvioAsync(viaje, EstadoPedido.Cancelado);
        // La ruta que los llevó sigue en la calle con pedidos de otros: para este viaje ya no hay nada pendiente.
        var ruta = await RutaEnCursoAsync();
        await ParadaConAsync(ruta.Id, 1, entregado, "completada");
        await ParadaConAsync(ruta.Id, 2, fallido, "fallida");

        Assert.Equal("cerrado", await EstadoAsync(viaje));
    }

    [Fact]
    public async Task La_fila_del_reprogramado_en_la_parada_donde_fallo_no_pone_al_viaje_en_curso()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id);
        var reprogramado = await EnvioAsync(viaje, EstadoPedido.Reprogramado);
        var dondeFallo = await RutaEnCursoAsync();
        await ParadaConAsync(dondeFallo.Id, 1, reprogramado, "fallida");

        Assert.Equal("sin_ruta", await EstadoAsync(viaje));

        // Vuelve a salir en otra ruta: cuenta esa, no la historia.
        var nueva = await baseDatos.RutaAsync();
        await ParadaConAsync(nueva.Id, 1, reprogramado);
        Assert.Equal("planificado", await EstadoAsync(viaje));
    }

    // ---- Lo que no cambia ----

    [Fact]
    public async Task El_viaje_que_conserva_su_ruta_se_sigue_leyendo_de_ella()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();

        // En planificación, aunque uno de sus envíos ya esté en la calle por otra ruta.
        var propuesta = await baseDatos.RutaAsync();
        var planificado = await ViajeAsync(cliente.Id, actor.Id, propuesta.Id);
        await ParadaConAsync(propuesta.Id, 1, await EnvioAsync(planificado, EstadoPedido.Borrador));
        var otraRuta = await RutaEnCursoAsync();
        await ParadaConAsync(otraRuta.Id, 1, await EnvioAsync(planificado, EstadoPedido.EnRuta));
        Assert.Equal("planificado", await EstadoAsync(planificado));

        // En curso, aunque la baja le haya retenido todo lo pendiente.
        var enCalle = await RutaEnCursoAsync();
        var enCurso = await ViajeAsync(cliente.Id, actor.Id, enCalle.Id);
        await ParadaConAsync(enCalle.Id, 1, await EnvioAsync(enCurso, EstadoPedido.Retenido), "cancelada");
        Assert.Equal("en_curso", await EstadoAsync(enCurso));

        // Cerrada, aunque le quede un envío reprogramado.
        var cerrada = await baseDatos.RutaAsync();
        var cerrado = await ViajeAsync(cliente.Id, actor.Id, cerrada.Id);
        await ParadaConAsync(cerrada.Id, 1, await EnvioAsync(cerrado, EstadoPedido.Reprogramado), "fallida");
        await baseDatos.EjecutarAsync($"update rutas set estado = 'cerrada' where id = {cerrada.Id}");
        Assert.Equal("cerrado", await EstadoAsync(cerrado));
    }

    [Fact]
    public async Task El_viaje_cancelado_sigue_cancelado_y_el_que_nunca_tuvo_ruta_sigue_sin_ruta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();

        var cancelado = await ViajeAsync(cliente.Id, actor.Id, estado: EstadosViaje.Cancelado);
        await EnvioAsync(cancelado, EstadoPedido.Cancelado);
        Assert.Equal("cancelado", await EstadoAsync(cancelado));

        // Todas sus direcciones eran dudosas: se creó sin ruta propuesta y sus envíos siguen en borrador.
        var sinRuta = await ViajeAsync(cliente.Id, actor.Id);
        await EnvioAsync(sinRuta, EstadoPedido.Borrador);
        Assert.Equal("sin_ruta", await EstadoAsync(sinRuta));
    }

    // ---- Las paradas del detalle ----

    [Fact]
    public async Task Las_paradas_de_un_viaje_sin_ruta_propia_no_quedan_como_fuera_de_ruta_salvo_la_de_direccion_dudosa()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id);
        var retenido = await EnvioAsync(viaje, EstadoPedido.Retenido);
        var reanudado = await EnvioAsync(viaje, EstadoPedido.Borrador);
        var dudosa = await baseDatos.UbicacionAsync(verificada: false, confianza: "baja");
        var aVerificar = await EnvioAsync(viaje, EstadoPedido.Borrador, dudosa.Id);

        var paradas = (await DetalleAsync(viaje)).Paradas.ToDictionary(p => p.PedidoId);

        Assert.True(paradas[retenido.Id].EnRuta);
        Assert.True(paradas[reanudado.Id].EnRuta);
        Assert.False(paradas[aVerificar.Id].EnRuta);
    }

    [Fact]
    public async Task En_un_viaje_con_ruta_propia_la_parada_que_no_entro_en_ella_sigue_fuera_de_ruta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var propuesta = await baseDatos.RutaAsync();
        var viaje = await ViajeAsync(cliente.Id, actor.Id, propuesta.Id);
        var enLaRuta = await EnvioAsync(viaje, EstadoPedido.Borrador);
        var afuera = await EnvioAsync(viaje, EstadoPedido.Borrador);
        await ParadaConAsync(propuesta.Id, 1, enLaRuta);

        var paradas = (await DetalleAsync(viaje)).Paradas.ToDictionary(p => p.PedidoId);

        Assert.True(paradas[enLaRuta.Id].EnRuta);
        Assert.False(paradas[afuera.Id].EnRuta);
    }
}
