using System.Net;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// B11: el alta de la importación masiva contra la base. El geocoder se reemplaza por uno que no
// encuentra nada: las pruebas no salen a internet, y de paso cubren la dirección dudosa.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class ImportacionPedidosBaseTests(BaseDePrueba baseDatos)
{
    private sealed class GeocoderSinResultados : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private static ImportacionPedidosService Servicio(Logistica.Datos.LogisticaDbContext db)
    {
        var http = new HttpClient(new GeocoderSinResultados()) { BaseAddress = new Uri("http://geocoder.prueba/") };
        var ubicaciones = new UbicacionService(db, new GeocodificacionService(http), new EnlaceMapaService(http));
        return new ImportacionPedidosService(
            db, ubicaciones, new OrigenRutaService(db, ubicaciones), new CuentaCorrienteService(db),
            Options.Create(new OpcionesCarga()), NullLogger<ImportacionPedidosService>.Instance);
    }

    // Tres días adelante: ni el corte de carga de mañana ni la hora a la que corran las pruebas la afectan.
    private static string Fecha => Reloj.HoyLocal().AddDays(3).ToString("dd/MM/yyyy");

    private static FilaPlanilla Fila(int numero, string localidad, string destinatario = "Juan Pérez", string? calle = null, string? telefono = "1155551234") =>
        new(numero, destinatario, telefono, calle ?? $"Calle {Guid.NewGuid():N} 100", localidad, null, null, "2", Fecha, "Tocar timbre", "REF-1", null, null, null);

    [Fact]
    public async Task Importar_da_de_alta_los_pedidos_en_borrador_con_origen_importado()
    {
        var administrador = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var localidad = await baseDatos.LocalidadAsync();

        await using var db = baseDatos.CrearContexto();
        var resultado = Assert.Single(await Servicio(db).ImportarAsync(
            cliente.Id, [Fila(2, localidad.Nombre)], administrador.Id, CancellationToken.None));

        Assert.Null(resultado.Error);
        Assert.True(resultado.DireccionDudosa); // el geocoder no la encontró: queda marcada, no frena el alta
        var pedido = await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == resultado.PedidoId);
        Assert.Equal(cliente.Id, pedido.ClienteId);
        Assert.Equal("importado", pedido.OrigenCarga);
        Assert.Equal(EstadoPedido.Borrador, pedido.Estado);
        Assert.Equal(2, pedido.Bultos);
        Assert.Equal("REF-1", pedido.ReferenciaCliente);
        Assert.Null(pedido.Total);
        var evento = await db.PedidoEventos.AsNoTracking().SingleAsync(e => e.PedidoId == pedido.Id);
        Assert.Equal(administrador.Id, evento.ActorUsuarioId);
    }

    [Fact]
    public async Task Una_fila_invalida_no_frena_a_las_demas()
    {
        var administrador = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var localidad = await baseDatos.LocalidadAsync();
        var conocida = await baseDatos.UbicacionAsync();
        FilaPlanilla[] filas =
        [
            Fila(2, localidad.Nombre),
            Fila(3, localidad.Nombre, telefono: null),
            Fila(4, "Localidad que no existe"),
            Fila(5, (await DbLocalidad(conocida.LocalidadId!.Value)).Nombre, calle: conocida.CalleNumero),
        ];

        await using var db = baseDatos.CrearContexto();
        var resultados = await Servicio(db).ImportarAsync(cliente.Id, filas, administrador.Id, CancellationToken.None);

        Assert.Equal([2, 3, 4, 5], resultados.Select(r => r.Numero));
        Assert.NotNull(resultados[0].PedidoId);
        Assert.Equal("Falta el teléfono.", resultados[1].Error);
        Assert.Contains("no está cargada en el sistema", resultados[2].Error);
        Assert.NotNull(resultados[3].PedidoId);
        Assert.False(resultados[3].DireccionDudosa); // dirección ya conocida y verificada: no se vuelve a geolocalizar
        Assert.Equal(2, await db.Pedidos.CountAsync(p => p.ClienteId == cliente.Id));
    }

    [Fact]
    public async Task La_vista_previa_no_escribe_y_avisa_lo_que_ya_esta_cargado_o_repetido()
    {
        var administrador = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var localidad = await baseDatos.LocalidadAsync();
        var yaCargada = Fila(2, localidad.Nombre, calle: "Av. Mitre 1234");
        await using (var alta = baseDatos.CrearContexto())
            await Servicio(alta).ImportarAsync(cliente.Id, [yaCargada], administrador.Id, CancellationToken.None);

        FilaPlanilla[] filas =
        [
            yaCargada,
            Fila(3, localidad.Nombre, destinatario: "Ana Gómez", calle: "Belgrano 50"),
            Fila(4, localidad.Nombre, destinatario: "ana gomez", calle: "belgrano  50"),
            Fila(5, localidad.Nombre, telefono: "1"),
        ];
        await using var db = baseDatos.CrearContexto();
        var previstas = await Servicio(db).PrevisualizarAsync(cliente.Id, filas, CancellationToken.None);

        Assert.Equal(["aviso", "ok", "aviso", "error"], previstas.Select(p => p.Estado));
        Assert.Contains("Ya hay un pedido igual cargado", Assert.Single(previstas[0].Mensajes));
        Assert.Equal("Repite la fila 3 de la planilla.", Assert.Single(previstas[2].Mensajes));
        Assert.Equal(1, await db.Pedidos.CountAsync(p => p.ClienteId == cliente.Id));
    }

    [Fact]
    public async Task Un_cliente_inactivo_o_inexistente_no_admite_importacion()
    {
        var cliente = await baseDatos.ClienteAsync();
        await baseDatos.EjecutarAsync($"update clientes set activo = false where id = {cliente.Id}");

        await using var db = baseDatos.CrearContexto();
        Assert.Contains("está inactivo", await Servicio(db).ErrorDeClienteAsync(cliente.Id, CancellationToken.None));
        Assert.Equal("El cliente no existe.", await Servicio(db).ErrorDeClienteAsync(-1, CancellationToken.None));
    }

    [Fact]
    public async Task Un_cliente_con_deuda_vencida_no_admite_importacion()
    {
        var cliente = await baseDatos.ClienteAsync();
        await baseDatos.EjecutarAsync($"""
            insert into facturas (cliente_id, ciclo, periodo_desde, periodo_hasta, fecha_emision, fecha_vencimiento, total, creada_en)
            values ({cliente.Id}, 'mensual', '2026-01-01', '2026-01-31', '2026-02-01', '2026-02-10', 5000, now())
            """);

        await using var db = baseDatos.CrearContexto();
        Assert.Contains("Servicio cortado por deuda vencida", await Servicio(db).ErrorDeClienteAsync(cliente.Id, CancellationToken.None));
    }

    private async Task<Localidad> DbLocalidad(int id)
    {
        await using var db = baseDatos.CrearContexto();
        return await db.Localidades.AsNoTracking().SingleAsync(l => l.Id == id);
    }
}
