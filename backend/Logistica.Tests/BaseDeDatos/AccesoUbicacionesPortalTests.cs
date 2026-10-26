using System.Net;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// M-N1: `ubicaciones` es un catálogo compartido. Desde el portal, un cliente solo usa las ubicaciones a
// las que llegó por su cuenta (las resolvió, son destino de un envío suyo o de un contacto suyo), y solo
// mueve el punto de una que nadie más usa.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class AccesoUbicacionesPortalTests(BaseDePrueba baseDatos)
{
    private const string NoExiste = "La ubicación de destino no existe.";
    private const string AlgunaNoExiste = "Alguna de las direcciones de destino no existe.";
    private const long IdInexistente = long.MaxValue;
    private const string Punto = "-34.70, -58.50";

    private sealed class GeocoderSinResultados : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private static UbicacionService Ubicaciones(LogisticaDbContext db)
    {
        var http = new HttpClient(new GeocoderSinResultados()) { BaseAddress = new Uri("http://geocoder.prueba/") };
        return new UbicacionService(db, new GeocodificacionService(http), new EnlaceMapaService(http));
    }

    // Lo que usan las altas hasta validar el destino; el resto del controller no se toca.
    private static MiCuentaController MiCuenta(LogisticaDbContext db, ClienteUsuario login) =>
        new MiCuentaController(
                db, new CuentaCorrienteService(db), null!, null!, null!,
                Options.Create(new OpcionesPortal()), Options.Create(new OpcionesCarga()), Ubicaciones(db),
                null!, null!, null!, null!, null!, null!, null!)
            .ComoPortal(login, db);

    // Tres días adelante: ni los cortes de carga ni la hora a la que corran las pruebas la afectan.
    private static DateOnly Fecha => Reloj.HoyLocal().AddDays(3);

    private static MiCuentaController.CrearPedidoPortalRequest Pedido(long destinoId) =>
        new("Juan", "1155551234", destinoId, 1, null, Fecha, false, "camioneta", null);

    private static MiCuentaController.ViajePortalRequest Viaje(long destinoId) =>
        new(Fecha, false, "camioneta", null,
        [
            new ParadaViajeEntrada(destinoId, "Juan", "1155551234", 1, null),
            new ParadaViajeEntrada(destinoId, "Ana", "1155551235", 1, null),
        ]);

    private static MiCuentaController.GuardarClienteDestinatarioRequest Contacto(long destinoId) =>
        new("Juan", "1155551234", destinoId, null);

    private async Task<ClienteUsuario> LoginAsync() =>
        await baseDatos.ClienteUsuarioAsync((await baseDatos.ClienteAsync()).Id);

    /// <summary>Una ubicación sin verificar que usa otro cliente (destino de un envío suyo).</summary>
    private async Task<Ubicacion> UbicacionDeOtroAsync()
    {
        var ubicacion = await baseDatos.UbicacionAsync(verificada: false);
        await baseDatos.PedidoAsync(destinoId: ubicacion.Id);
        return ubicacion;
    }

    private static object? Rechazo(IActionResult respuesta) => Assert.IsType<BadRequestObjectResult>(respuesta).Value;

    private async Task<MiCuentaController.UbicacionResuelta> ResolverAsync(
        ClienteUsuario login, Ubicacion ubicacion, string? urlMapa = null)
    {
        await using var db = baseDatos.CrearContexto();
        var respuesta = await MiCuenta(db, login).ResolverUbicacion(
            new(ubicacion.CalleNumero, ubicacion.LocalidadId!.Value, null, urlMapa), CancellationToken.None);
        return Assert.IsType<MiCuentaController.UbicacionResuelta>(Assert.IsType<OkObjectResult>(respuesta).Value);
    }

    [Fact]
    public async Task Un_envio_con_la_ubicacion_de_otro_cliente_se_rechaza_igual_que_una_inexistente()
    {
        var login = await LoginAsync();
        var ajena = await UbicacionDeOtroAsync();

        await using var db = baseDatos.CrearContexto();
        var portal = MiCuenta(db, login);

        Assert.Equal(NoExiste, Rechazo(await portal.CrearPedido(Pedido(IdInexistente), CancellationToken.None)));
        Assert.Equal(NoExiste, Rechazo(await portal.CrearPedido(Pedido(ajena.Id), CancellationToken.None)));
    }

    [Fact]
    public async Task Un_viaje_con_la_ubicacion_de_otro_cliente_se_rechaza_igual_que_una_inexistente()
    {
        var login = await LoginAsync();
        var ajena = await UbicacionDeOtroAsync();

        await using var db = baseDatos.CrearContexto();
        var portal = MiCuenta(db, login);

        Assert.Equal(AlgunaNoExiste, Rechazo(await portal.PrevisualizarViaje(Viaje(IdInexistente), CancellationToken.None)));
        Assert.Equal(AlgunaNoExiste, Rechazo(await portal.PrevisualizarViaje(Viaje(ajena.Id), CancellationToken.None)));
        Assert.Equal(AlgunaNoExiste, Rechazo(await portal.CrearViaje(Viaje(ajena.Id), CancellationToken.None)));
    }

    [Fact]
    public async Task Un_contacto_con_la_ubicacion_de_otro_cliente_se_rechaza_al_crearlo_y_al_editarlo()
    {
        var login = await LoginAsync();
        var ajena = await UbicacionDeOtroAsync();
        var propia = await baseDatos.UbicacionAsync(verificada: false);
        var resuelta = await ResolverAsync(login, propia);

        await using var db = baseDatos.CrearContexto();
        var portal = MiCuenta(db, login);

        Assert.Equal(NoExiste, Rechazo(await portal.CrearDestinatario(Contacto(IdInexistente), CancellationToken.None)));
        Assert.Equal(NoExiste, Rechazo(await portal.CrearDestinatario(Contacto(ajena.Id), CancellationToken.None)));

        var contacto = Assert.IsType<MiCuentaController.ClienteDestinatarioResumen>(
            Assert.IsType<OkObjectResult>(await portal.CrearDestinatario(Contacto(resuelta.Id), CancellationToken.None)).Value);
        Assert.Equal(NoExiste, Rechazo(await portal.EditarDestinatario(contacto.Id, Contacto(ajena.Id), CancellationToken.None)));
        Assert.Equal(propia.Id, (await db.ClientesDestinatarios.AsNoTracking().SingleAsync(d => d.Id == contacto.Id)).DestinoUbicacionId);
    }

    [Fact]
    public async Task Despues_de_resolver_la_misma_direccion_el_cliente_puede_usarla()
    {
        var login = await LoginAsync();
        var compartida = await UbicacionDeOtroAsync();

        var resuelta = await ResolverAsync(login, compartida);
        Assert.Equal(compartida.Id, resuelta.Id);

        await using var db = baseDatos.CrearContexto();
        var contacto = Assert.IsType<MiCuentaController.ClienteDestinatarioResumen>(
            Assert.IsType<OkObjectResult>(await MiCuenta(db, login).CrearDestinatario(Contacto(compartida.Id), CancellationToken.None)).Value);
        Assert.Equal(compartida.Id, contacto.DestinoUbicacionId);
    }

    [Fact]
    public async Task Resolver_dos_veces_la_misma_direccion_no_duplica_el_acceso()
    {
        var login = await LoginAsync();
        var ubicacion = await baseDatos.UbicacionAsync(verificada: false);

        await ResolverAsync(login, ubicacion);
        await ResolverAsync(login, ubicacion);

        Assert.Equal(1L, await baseDatos.EscalarAsync<long>(
            $"select count(*) from clientes_ubicaciones where cliente_id = {login.ClienteId} and ubicacion_id = {ubicacion.Id}"));
    }

    [Fact]
    public async Task Desde_el_portal_no_se_mueve_el_punto_de_una_ubicacion_que_usa_otro_cliente()
    {
        var login = await LoginAsync();
        var compartida = await UbicacionDeOtroAsync();

        var resuelta = await ResolverAsync(login, compartida, Punto);

        Assert.False(resuelta.UrlMapaAplicada);
        Assert.False(string.IsNullOrWhiteSpace(resuelta.UrlMapaError));
        await using var db = baseDatos.CrearContexto();
        var guardada = await db.Ubicaciones.AsNoTracking().SingleAsync(u => u.Id == compartida.Id);
        Assert.Null(guardada.Lat);
        Assert.Null(guardada.Lng);
    }

    [Fact]
    public async Task Desde_el_portal_no_se_mueve_el_punto_de_una_parada_de_ruta_sin_envios_del_cliente()
    {
        var login = await LoginAsync();
        var parada = await baseDatos.UbicacionAsync(verificada: false);
        await ResolverAsync(login, parada);
        // Parada sin envíos vinculados (p. ej. un retiro): no es del cliente, el punto no se toca.
        await baseDatos.ParadaAsync((await baseDatos.RutaAsync()).Id, parada.Id);

        var resuelta = await ResolverAsync(login, parada, Punto);

        Assert.False(resuelta.UrlMapaAplicada);
        Assert.False(string.IsNullOrWhiteSpace(resuelta.UrlMapaError));
    }

    [Fact]
    public async Task Desde_el_portal_se_mueve_el_punto_de_una_ubicacion_que_solo_usa_ese_cliente()
    {
        var login = await LoginAsync();
        var propia = await baseDatos.UbicacionAsync(verificada: false);
        await ResolverAsync(login, propia);

        var resuelta = await ResolverAsync(login, propia, Punto);

        Assert.True(resuelta.UrlMapaAplicada);
        Assert.Null(resuelta.UrlMapaError);
        Assert.Equal(-34.70m, resuelta.Lat);
        Assert.Equal(-58.50m, resuelta.Lng);
    }

    [Fact]
    public async Task El_back_office_sigue_moviendo_el_punto_de_una_ubicacion_compartida()
    {
        var compartida = await UbicacionDeOtroAsync();

        await using var db = baseDatos.CrearContexto();
        var respuesta = await new UbicacionesController(Ubicaciones(db), null!, null!).Resolver(
            new(compartida.CalleNumero, compartida.LocalidadId!.Value, null, Punto), CancellationToken.None);

        var resuelta = Assert.IsType<UbicacionesController.UbicacionResuelta>(Assert.IsType<OkObjectResult>(respuesta).Value);
        Assert.True(resuelta.UrlMapaAplicada);
        Assert.Equal(-34.70m, resuelta.Lat);
    }
}
