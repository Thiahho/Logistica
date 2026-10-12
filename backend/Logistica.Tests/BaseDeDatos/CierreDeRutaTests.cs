using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// Cierre económico (RF-26/27). Una ruta cerrada ya no se puede interrumpir, así que cerrarla con paradas
// pendientes dejaría sus pedidos en EnRuta para siempre, sin circuito de reprogramación que los rescate.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class CierreDeRutaTests(BaseDePrueba baseDatos)
{
    // El cierre calcula el pago sugerido: necesita el servicio de liquidación real, el resto no se toca.
    private static RutasController Rutas(LogisticaDbContext db, Guid usuarioId) =>
        new RutasController(db, null!, null!, null!, null!, new LiquidacionService(db), null!).ComoUsuario(usuarioId);

    private static readonly RutasController.CerrarRutaRequest Cierre = new(
        KmInicial: 1000, KmFinal: 1080, CombustibleMonto: 5000m, PeajesMonto: 0m, OtrosCostos: 0m,
        PagoRepartidor: 0m, NotasCierre: null, SinDeclaracionDelRepartidor: true);

    /// <summary>Una ruta en curso con una parada por cada estado pedido, cada una con su pedido EnRuta.</summary>
    private async Task<(Ruta Ruta, List<Pedido> Pedidos)> RutaEnCursoAsync(params string[] estadosDeParadas)
    {
        var ruta = await baseDatos.RutaAsync();
        var pedidos = new List<Pedido>();
        for (var i = 0; i < estadosDeParadas.Length; i++)
        {
            var pedido = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);
            await using var db = baseDatos.CrearContexto();
            var parada = new RutaParada
            {
                RutaId = ruta.Id, UbicacionId = pedido.DestinoUbicacionId, Tipo = "entrega", Orden = i + 1,
            };
            db.RutaParadas.Add(parada);
            await db.SaveChangesAsync();
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedido.Id });
            await db.SaveChangesAsync();
            await baseDatos.EjecutarAsync(
                $"update ruta_paradas set estado = '{estadosDeParadas[i]}' where id = {parada.Id}");
            pedidos.Add(pedido);
        }
        await baseDatos.EjecutarAsync($"update rutas set estado = 'en_curso' where id = {ruta.Id}");
        return (ruta, pedidos);
    }

    private async Task<IActionResult> CerrarAsync(long rutaId, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        return await Rutas(db, actor).Cerrar(rutaId, Cierre, CancellationToken.None);
    }

    [Fact]
    public async Task Cerrar_con_una_parada_pendiente_responde_409_y_no_cambia_nada()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (ruta, pedidos) = await RutaEnCursoAsync("completada", "pendiente");

        var respuesta = Assert.IsType<ConflictObjectResult>(await CerrarAsync(ruta.Id, actor.Id));

        var mensaje = Assert.IsType<string>(respuesta.Value);
        Assert.Contains("1 parada", mensaje);
        Assert.Contains("nterrump", mensaje);
        await using var db = baseDatos.CrearContexto();
        var guardada = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal("en_curso", guardada.Estado);
        Assert.Null(guardada.CerradaEn);
        Assert.Null(guardada.CerradaPor);
        Assert.Null(guardada.KmFinal);
        Assert.Equal(EstadoPedido.EnRuta, (await db.Pedidos.SingleAsync(p => p.Id == pedidos[1].Id)).Estado);
    }

    [Fact]
    public async Task Cerrar_con_todas_las_paradas_resueltas_cierra_la_ruta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (ruta, _) = await RutaEnCursoAsync("completada", "fallida", "cancelada");

        Assert.IsType<OkObjectResult>(await CerrarAsync(ruta.Id, actor.Id));

        await using var db = baseDatos.CrearContexto();
        var guardada = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal("cerrada", guardada.Estado);
        Assert.Equal(1080, guardada.KmFinal);
        Assert.Equal(actor.Id, guardada.CerradaPor);
    }

    // El camino que indica el 409: interrumpir pasa lo pendiente a fallido y recién ahí la ruta cierra.
    [Fact]
    public async Task Despues_de_interrumpirla_la_ruta_con_pendientes_se_puede_cerrar()
    {
        var actor = await baseDatos.UsuarioAsync();
        var (ruta, pedidos) = await RutaEnCursoAsync("pendiente");
        Assert.IsType<ConflictObjectResult>(await CerrarAsync(ruta.Id, actor.Id));

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<OkObjectResult>(await Rutas(db, actor.Id).Interrumpir(
                ruta.Id, new RutasController.InterrumpirRutaRequest("Avería del vehículo"), CancellationToken.None));

        Assert.IsType<OkObjectResult>(await CerrarAsync(ruta.Id, actor.Id));
        await using var lectura = baseDatos.CrearContexto();
        Assert.Equal("cerrada", (await lectura.Rutas.SingleAsync(r => r.Id == ruta.Id)).Estado);
        Assert.Equal(EstadoPedido.Fallido, (await lectura.Pedidos.SingleAsync(p => p.Id == pedidos[0].Id)).Estado);
    }
}
