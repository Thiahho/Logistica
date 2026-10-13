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

// RNF-02 y acta changelog 4.8: el cierre de parada no puede perder la prueba de entrega cuando algo
// cambia entre que leyó los pedidos y que guarda. Solo es un duplicado si las pruebas de este
// dispositivo YA están guardadas; cualquier otro choque se reintenta sobre el estado nuevo.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class CierreDeParadaConcurrenteTests(BaseDePrueba baseDatos)
{
    private static readonly IOptions<OpcionesPruebaEntrega> OpcionesDePrueba =
        Options.Create(new OpcionesPruebaEntrega { RaizFotos = "no-se-usa", MotivosFallo = ["ausente"] });

    /// <summary>No guarda nada: cuenta las escrituras y, en la primera, deja correr `mientrasTanto`. La foto
    /// se escribe después de leer los pedidos y antes de guardar — justo la ventana de la carrera.</summary>
    private sealed class AlmacenamientoDePrueba(Func<Task>? mientrasTanto = null) : AlmacenamientoFotos(OpcionesDePrueba)
    {
        public int Escrituras { get; private set; }

        public override Task<Stream?> AbrirAsync(string rutaRelativa, CancellationToken ct) => Task.FromResult<Stream?>(null);

        protected override async Task EscribirAsync(string rutaRelativa, Stream contenido, CancellationToken ct)
        {
            Escrituras++;
            if (Escrituras == 1 && mientrasTanto is not null) await mientrasTanto();
        }
    }

    private sealed record Escenario(Guid RepartidorId, Ruta Ruta, RutaParada Parada, Pedido Primero, Pedido Segundo);

    /// <summary>Una ruta en curso con el retiro firmado y una parada consolidada (RF-14) de dos pedidos.</summary>
    private async Task<Escenario> ParadaConDosPedidosAsync()
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var primero = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);
        var segundo = await baseDatos.PedidoAsync(EstadoPedido.EnRuta, primero.DestinoUbicacionId);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        var parada = await baseDatos.ParadaAsync(ruta.Id, primero.DestinoUbicacionId);
        await using (var db = baseDatos.CrearContexto())
        {
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = primero.Id });
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = segundo.Id });
            await db.SaveChangesAsync();
        }
        await baseDatos.EjecutarAsync(
            "update rutas set estado = 'en_curso', retiro_confirmado_en = now(), retiro_bultos_esperados = 2, " +
            $"retiro_bultos_contados = 2 where id = {ruta.Id}");
        return new Escenario(repartidor.Id, ruta, parada, primero, segundo);
    }

    private static MisParadasController.CerrarParadaRequest Entrega(string deviceUuid)
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        return new MisParadasController.CerrarParadaRequest
        {
            DeviceUuid = deviceUuid,
            Resultado = "entregado",
            CapturadaEn = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero),
            ReceptorNombre = "Quien recibe",
            DocumentoNumero = "12345678",
            Foto = new FormFile(new MemoryStream(jpeg), 0, jpeg.Length, "foto", "foto.jpg"),
        };
    }

    private async Task<IActionResult> CerrarAsync(
        Escenario e, MisParadasController.CerrarParadaRequest req, AlmacenamientoFotos almacenamiento)
    {
        await using var db = baseDatos.CrearContexto();
        return await new MisParadasController(db, OpcionesDePrueba, almacenamiento, null!, new CuentaCorrienteService(db))
            .ComoUsuario(e.RepartidorId)
            .Cerrar(e.Parada.Id, req, CancellationToken.None);
    }

    private async Task CancelarDesdeOperacionAsync(long pedidoId)
    {
        var operador = await baseDatos.UsuarioAsync();
        await using var db = baseDatos.CrearContexto();
        var respuesta = await new PedidosController(db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!, null!, null!, new RetencionPedidosService(db))
            .ComoUsuario(operador.Id)
            .CambiarEstado(pedidoId, new PedidosController.CambiarEstadoRequest("Cancelado", "El cliente lo pidió", null), CancellationToken.None);
        Assert.IsType<NoContentResult>(respuesta);
    }

    [Fact]
    public async Task Una_cancelacion_en_medio_del_cierre_no_pierde_la_prueba_del_resto_de_la_parada()
    {
        var e = await ParadaConDosPedidosAsync();
        var deviceUuid = Guid.NewGuid().ToString();
        // Operación cancela el segundo pedido cuando el cierre ya lo leyó como EnRuta: su ítem de
        // cancelación choca con el de entrega en ux_factura_items_pedido.
        var almacenamiento = new AlmacenamientoDePrueba(() => CancelarDesdeOperacionAsync(e.Segundo.Id));

        var respuesta = Assert.IsType<OkObjectResult>(await CerrarAsync(e, Entrega(deviceUuid), almacenamiento));

        var cierre = Assert.IsType<MisParadasController.CierreResultado>(respuesta.Value);
        Assert.False(cierre.Duplicado);
        Assert.Equal(1, cierre.PedidosActualizados);
        Assert.Equal("completada", cierre.EstadoParada);
        Assert.Equal(1, almacenamiento.Escrituras); // el reintento no vuelve a subir la foto

        await using var db = baseDatos.CrearContexto();
        Assert.Equal(EstadoPedido.Entregado, (await db.Pedidos.SingleAsync(p => p.Id == e.Primero.Id)).Estado);
        Assert.Equal(EstadoPedido.Cancelado, (await db.Pedidos.SingleAsync(p => p.Id == e.Segundo.Id)).Estado);
        var prueba = Assert.Single(await db.PruebasEntrega.Where(pe => pe.ParadaId == e.Parada.Id).ToListAsync());
        Assert.Equal(e.Primero.Id, prueba.PedidoId);
        Assert.Equal(deviceUuid, prueba.DeviceUuid);
        Assert.Equal($"Pedido #{e.Primero.Id} — Destinatario",
            (await db.FacturaItems.SingleAsync(i => i.PedidoId == e.Primero.Id)).Descripcion);
        Assert.StartsWith("Cancelación", (await db.FacturaItems.SingleAsync(i => i.PedidoId == e.Segundo.Id)).Descripcion);
        Assert.Equal("completada", (await db.RutaParadas.SingleAsync(p => p.Id == e.Parada.Id)).Estado);
    }

    [Fact]
    public async Task Dos_taps_simultaneos_responden_duplicado_con_las_pruebas_guardadas_una_sola_vez()
    {
        var e = await ParadaConDosPedidosAsync();
        var deviceUuid = Guid.NewGuid().ToString();
        // El otro tap cierra entero mientras este todavía no guardó: este choca con el unique
        // (pedido_id, device_uuid) de pruebas_entrega.
        var almacenamiento = new AlmacenamientoDePrueba(async () =>
            Assert.IsType<OkObjectResult>(await CerrarAsync(e, Entrega(deviceUuid), new AlmacenamientoDePrueba())));

        var respuesta = Assert.IsType<OkObjectResult>(await CerrarAsync(e, Entrega(deviceUuid), almacenamiento));

        var cierre = Assert.IsType<MisParadasController.CierreResultado>(respuesta.Value);
        Assert.True(cierre.Duplicado);
        Assert.Equal(2, cierre.PedidosActualizados);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(2, await db.PruebasEntrega.CountAsync(pe => pe.ParadaId == e.Parada.Id && pe.DeviceUuid == deviceUuid));
        Assert.Equal(2, await db.FacturaItems.CountAsync(i => i.PedidoId == e.Primero.Id || i.PedidoId == e.Segundo.Id));
    }

    [Fact]
    public async Task Si_el_reintento_tampoco_puede_guardar_responde_409_y_nunca_duplicado()
    {
        var e = await ParadaConDosPedidosAsync();
        var deviceUuid = Guid.NewGuid().ToString();
        // Una prueba suelta de este dispositivo para UNO solo de los dos pedidos: no alcanza para ser
        // un duplicado (faltaría la del otro) y hace chocar cada intento de guardar.
        await using (var previa = baseDatos.CrearContexto())
        {
            previa.PruebasEntrega.Add(new PruebaEntrega
            {
                PedidoId = e.Primero.Id, Resultado = "fallido", MotivoFallo = "ausente",
                CapturadaEn = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero), DeviceUuid = deviceUuid,
            });
            await previa.SaveChangesAsync();
        }

        var respuesta = await CerrarAsync(e, Entrega(deviceUuid), new AlmacenamientoDePrueba());

        Assert.IsType<ConflictObjectResult>(respuesta);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(EstadoPedido.EnRuta, (await db.Pedidos.SingleAsync(p => p.Id == e.Segundo.Id)).Estado);
        Assert.False(await db.PruebasEntrega.AnyAsync(pe => pe.PedidoId == e.Segundo.Id));
        Assert.Equal("pendiente", (await db.RutaParadas.SingleAsync(p => p.Id == e.Parada.Id)).Estado);
    }
}
