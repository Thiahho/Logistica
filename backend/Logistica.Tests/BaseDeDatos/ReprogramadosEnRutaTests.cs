using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// D13 (§10.2-M): un pedido reprogramado vuelve a salir en otra ruta. La fila de parada_pedidos de su
// intento fallido queda para siempre en la ruta vieja (es historia), así que nada de lo que arma o
// cierra la ruta nueva puede tratarla como una asignación vigente.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class ReprogramadosEnRutaTests(BaseDePrueba baseDatos)
{
    // Solo la base: ninguno de estos caminos cotiza (un reprogramado ya tiene precio) ni arma jornadas.
    private static RutasController Rutas(LogisticaDbContext db, Guid usuarioId) =>
        new RutasController(db, null!, null!, null!, null!, null!, null!).ComoUsuario(usuarioId);

    private static PedidosController Pedidos(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!, null!, null!, new RetencionPedidosService(db))
            .ComoUsuario(usuarioId);

    /// <summary>Un pedido en `estado` que ya salió en una ruta: conserva su fila en una parada fallida de
    /// esa ruta, hoy `estadoRutaVieja`.</summary>
    private async Task<(Pedido Pedido, Ruta RutaVieja)> PedidoConIntentoPrevioAsync(
        EstadoPedido estado = EstadoPedido.Reprogramado, string estadoRutaVieja = "cerrada")
    {
        var pedido = await baseDatos.PedidoAsync(estado);
        var rutaVieja = await baseDatos.RutaAsync();
        await AsignarAsync(rutaVieja.Id, pedido);
        await baseDatos.EjecutarAsync($"update ruta_paradas set estado = 'fallida' where ruta_id = {rutaVieja.Id}");
        await baseDatos.EjecutarAsync($"update rutas set estado = '{estadoRutaVieja}' where id = {rutaVieja.Id}");
        return (pedido, rutaVieja);
    }

    private async Task AsignarAsync(long rutaId, Pedido pedido)
    {
        var parada = await baseDatos.ParadaAsync(rutaId, pedido.DestinoUbicacionId);
        await using var db = baseDatos.CrearContexto();
        db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedido.Id });
        await db.SaveChangesAsync();
    }

    private async Task<IActionResult> GuardarParadasAsync(long rutaId, Pedido pedido, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        return await Rutas(db, actor).GuardarParadas(
            rutaId,
            new RutasController.GuardarParadasRequest(
                [new RutasController.ParadaArmadoRequest(pedido.DestinoUbicacionId, false, [pedido.Id])]),
            CancellationToken.None);
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

    [Fact]
    public async Task Un_pedido_reprogramado_entra_en_el_armado_de_una_ruta_nueva()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync();
        var rutaNueva = await baseDatos.RutaAsync();

        Assert.IsType<NoContentResult>(await GuardarParadasAsync(rutaNueva.Id, pedido, actor.Id));

        await using var db = baseDatos.CrearContexto();
        Assert.True(await db.ParadaPedidos.AnyAsync(pp => pp.PedidoId == pedido.Id && pp.Parada.RutaId == rutaNueva.Id));
    }

    [Fact]
    public async Task El_intento_fallido_en_una_ruta_todavia_en_curso_tampoco_bloquea_el_armado()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync(estadoRutaVieja: "en_curso");
        var rutaNueva = await baseDatos.RutaAsync();

        Assert.IsType<NoContentResult>(await GuardarParadasAsync(rutaNueva.Id, pedido, actor.Id));
    }

    [Fact]
    public async Task Un_reprogramado_ya_puesto_en_otra_ruta_en_planificacion_no_se_asigna_dos_veces()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync();
        var primera = await baseDatos.RutaAsync();
        var segunda = await baseDatos.RutaAsync();
        Assert.IsType<NoContentResult>(await GuardarParadasAsync(primera.Id, pedido, actor.Id));

        Assert.IsType<ConflictObjectResult>(await GuardarParadasAsync(segunda.Id, pedido, actor.Id));
    }

    [Fact]
    public async Task Un_pedido_entregado_no_entra_en_el_armado()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Entregado);
        var ruta = await baseDatos.RutaAsync();

        Assert.IsType<BadRequestObjectResult>(await GuardarParadasAsync(ruta.Id, pedido, actor.Id));
    }

    [Fact]
    public async Task Cerrar_la_planificacion_lleva_el_reprogramado_a_en_ruta_con_el_precio_que_ya_tenia()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync();
        var rutaNueva = await baseDatos.RutaAsync();
        Assert.IsType<NoContentResult>(await GuardarParadasAsync(rutaNueva.Id, pedido, actor.Id));
        await CompletarRutaAsync(rutaNueva.Id);
        DateTimeOffset? congeladoAntes;
        await using (var antes = baseDatos.CrearContexto())
            congeladoAntes = (await antes.Pedidos.SingleAsync(p => p.Id == pedido.Id)).PrecioCongeladoEn;

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Rutas(db, actor.Id).CerrarPlanificacion(rutaNueva.Id, CancellationToken.None));

        await using var lectura = baseDatos.CrearContexto();
        var guardado = await lectura.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(EstadoPedido.EnRuta, guardado.Estado);
        Assert.Equal(1000m, guardado.PrecioBase);
        Assert.Equal(1000m, guardado.Total);
        Assert.Equal(congeladoAntes, guardado.PrecioCongeladoEn);
        Assert.Equal("en_curso", (await lectura.Rutas.SingleAsync(r => r.Id == rutaNueva.Id)).Estado);

        // RF-28, sin huecos: pasa por Confirmado (la única salida de Reprogramado) antes de EnRuta.
        var eventos = await lectura.PedidoEventos.Where(e => e.PedidoId == pedido.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(
            [EstadoPedido.Reprogramado, EstadoPedido.Confirmado, EstadoPedido.EnRuta],
            eventos.Select(e => e.EstadoNuevo).ToArray());
        Assert.All(eventos.Skip(1), e => Assert.Equal(actor.Id, e.ActorUsuarioId));
    }

    [Fact]
    public async Task Los_candidatos_de_ruta_incluyen_los_reprogramados_de_la_fecha()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync();

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(
            await Pedidos(db, actor.Id).CandidatosRuta(pedido.FechaEntrega, null, soloBorrador: false, CancellationToken.None));

        var candidatos = Assert.IsAssignableFrom<IEnumerable<PedidosController.CandidatoRuta>>(respuesta.Value);
        Assert.Contains(candidatos, c => c.PedidoId == pedido.Id);
    }

    [Fact]
    public async Task Un_reprogramado_ya_puesto_en_una_ruta_en_planificacion_no_es_candidato_de_otra()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, _) = await PedidoConIntentoPrevioAsync();
        var rutaNueva = await baseDatos.RutaAsync();
        Assert.IsType<NoContentResult>(await GuardarParadasAsync(rutaNueva.Id, pedido, actor.Id));

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(
            await Pedidos(db, actor.Id).CandidatosRuta(pedido.FechaEntrega, null, soloBorrador: false, CancellationToken.None));

        var candidatos = Assert.IsAssignableFrom<IEnumerable<PedidosController.CandidatoRuta>>(respuesta.Value);
        Assert.DoesNotContain(candidatos, c => c.PedidoId == pedido.Id);
    }

    [Fact]
    public async Task La_entrega_del_segundo_intento_es_ingreso_de_la_ruta_que_lo_entrego_no_de_la_del_intento_fallido()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (pedido, rutaVieja) = await PedidoConIntentoPrevioAsync(EstadoPedido.Entregado);
        var rutaNueva = await baseDatos.RutaAsync();
        await AsignarAsync(rutaNueva.Id, pedido);

        await using var db = baseDatos.CrearContexto();
        var deLaVieja = await ResultadoAsync(db, rutaVieja.Id, actor.Id);
        var deLaNueva = await ResultadoAsync(db, rutaNueva.Id, actor.Id);

        Assert.Equal(0m, deLaVieja.Ingresos);
        Assert.Equal(0, deLaVieja.Efectivas);
        Assert.Equal(1000m, deLaNueva.Ingresos);
        Assert.Equal(1, deLaNueva.Efectivas);
    }

    private static async Task<RutasController.ResultadoRuta> ResultadoAsync(LogisticaDbContext db, long rutaId, Guid actor)
    {
        var respuesta = Assert.IsType<OkObjectResult>(await Rutas(db, actor).Resultado(rutaId, CancellationToken.None));
        return Assert.IsType<RutasController.ResultadoRuta>(respuesta.Value);
    }
}
