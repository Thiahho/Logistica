using System.Net;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// B6: el resumen diario de envíos contra la base. Resend se reemplaza por un receptor que guarda lo
// que se le manda (o lo rechaza): las pruebas no salen a internet.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class AvisosEstadoBaseTests(BaseDePrueba baseDatos)
{
    private sealed class Resend(bool acepta = true) : HttpMessageHandler
    {
        public List<string> Recibidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!acepta) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("caído") };
            Recibidos.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"abc"}""") };
        }

        public IEnumerable<string> Para(string email) => Recibidos.Where(r => r.Contains(email));
    }

    private static AvisosEstadoService Servicio(LogisticaDbContext db, Resend resend)
    {
        var http = new HttpClient(resend) { BaseAddress = new Uri("http://resend.prueba/") };
        var email = new EmailService(http, Options.Create(new OpcionesResend { ApiKey = "clave-de-prueba" }), NullLogger<EmailService>.Instance);
        return new AvisosEstadoService(db, email, Options.Create(new OpcionesAvisosEstado()), NullLogger<AvisosEstadoService>.Instance);
    }

    // Un día adelante: el último cierre queda después de los eventos que la prueba acaba de generar,
    // a cualquier hora que corra.
    private static DateTimeOffset Manana => DateTimeOffset.UtcNow.AddDays(1);

    private async Task<int> CorrerAsync(Resend resend)
    {
        await using var db = baseDatos.CrearContexto();
        return await Servicio(db, resend).EnviarPendientesAsync(Manana, CancellationToken.None);
    }

    /// <summary>Un pedido del cliente que ya salió a reparto, con el cliente configurado como pida la prueba.</summary>
    private async Task<(Pedido Pedido, string Email)> PedidoEnRutaAsync(bool avisos = true)
    {
        var pedido = await baseDatos.PedidoAsync();
        var email = $"{Guid.NewGuid():N}@cliente.test";
        await baseDatos.EjecutarAsync($"""
            update clientes set email = '{email}', avisos_estado = {avisos}, avisos_estado_hasta = now() - interval '1 minute'
            where id = {pedido.ClienteId}
            """);
        await baseDatos.EjecutarAsync($"update pedidos set precio_base = 1000, total = 1000, estado = 'en_ruta' where id = {pedido.Id}");
        return (pedido, email);
    }

    private async Task<DateTimeOffset?> InformadoHastaAsync(int clienteId)
    {
        await using var db = baseDatos.CrearContexto();
        return await db.Clientes.Where(c => c.Id == clienteId).Select(c => c.AvisosEstadoHasta).SingleAsync();
    }

    [Fact]
    public async Task El_cliente_activado_recibe_un_resumen_con_el_ultimo_estado_de_cada_envio()
    {
        var (pedido, email) = await PedidoEnRutaAsync();
        var otro = await baseDatos.PedidoAsync();
        await baseDatos.EjecutarAsync($"update pedidos set cliente_id = {pedido.ClienteId}, precio_base = 1, total = 1, estado = 'en_ruta' where id = {otro.Id}");
        await baseDatos.EjecutarAsync($"update pedidos set estado = 'entregado' where id = {pedido.Id}");
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor);
        await using (var db = baseDatos.CrearContexto())
        {
            (await db.Pedidos.SingleAsync(p => p.Id == otro.Id)).Estado = EstadoPedido.Fallido;
            await db.GuardarComoAsync(usuario.Id, "destinatario_ausente");
        }

        var resend = new Resend();
        await CorrerAsync(resend);

        var correo = Assert.Single(resend.Para(email));
        Assert.Contains("1 entregado, 1 sin entregar", correo);
        Assert.Contains($"#{pedido.Id} ", correo);
        Assert.Contains("destinatario ausente", correo);
        Assert.DoesNotContain("Siguen en reparto", correo); // salió y se entregó: cuenta una vez, como entregado
        Assert.DoesNotContain(usuario.Nombre, correo);
    }

    [Fact]
    public async Task El_resumen_no_se_repite_en_la_corrida_siguiente()
    {
        var (pedido, email) = await PedidoEnRutaAsync();
        var resend = new Resend();
        await CorrerAsync(resend);

        await CorrerAsync(resend);

        Assert.Single(resend.Para(email));
        Assert.True(await InformadoHastaAsync(pedido.ClienteId) > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Un_cliente_sin_activar_no_recibe_nada()
    {
        var (_, email) = await PedidoEnRutaAsync(avisos: false);

        var resend = new Resend();
        await CorrerAsync(resend);

        Assert.Empty(resend.Para(email));
    }

    [Fact]
    public async Task Si_el_correo_no_sale_se_reintenta_en_la_corrida_siguiente()
    {
        var (pedido, email) = await PedidoEnRutaAsync();

        await CorrerAsync(new Resend(acepta: false));
        Assert.True(await InformadoHastaAsync(pedido.ClienteId) < DateTimeOffset.UtcNow);

        var resend = new Resend();
        await CorrerAsync(resend);
        Assert.Single(resend.Para(email));
    }

    [Fact]
    public async Task Un_cliente_sin_movimientos_no_recibe_correo_y_queda_al_dia()
    {
        var cliente = await baseDatos.ClienteAsync();
        var email = $"{Guid.NewGuid():N}@cliente.test";
        await baseDatos.EjecutarAsync($"""
            update clientes set email = '{email}', avisos_estado = true, avisos_estado_hasta = now() - interval '1 minute'
            where id = {cliente.Id}
            """);

        var resend = new Resend();
        await CorrerAsync(resend);

        Assert.Empty(resend.Para(email));
        Assert.True(await InformadoHastaAsync(cliente.Id) > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Lo_que_paso_antes_de_activar_los_avisos_no_se_informa()
    {
        var (pedido, email) = await PedidoEnRutaAsync(avisos: false);
        // Se activa ahora: el punto de partida es este instante, posterior a la salida a reparto.
        await baseDatos.EjecutarAsync($"update clientes set avisos_estado = true, avisos_estado_hasta = now() where id = {pedido.ClienteId}");

        var resend = new Resend();
        await CorrerAsync(resend);

        Assert.Empty(resend.Para(email));
    }
}
