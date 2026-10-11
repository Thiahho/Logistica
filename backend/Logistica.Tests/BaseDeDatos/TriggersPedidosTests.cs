using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// Acta RF-28 (historia de estados), P1/RF-02 (precio congelado, criterio de aceptación 7) y RF-05
// (dirección dudosa, criterio 6): trg_log_estado_pedido, trg_log_inmutable, trg_congelar_pedido y
// trg_bloquear_direccion_dudosa.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class TriggersPedidosTests(BaseDePrueba baseDatos)
{
    // ---- trg_log_estado_pedido ----

    [Fact]
    public async Task Alta_de_pedido_registra_el_primer_evento_como_sistema()
    {
        var pedido = await baseDatos.PedidoAsync();

        await using var db = baseDatos.CrearContexto();
        var evento = Assert.Single(await db.PedidoEventos.Where(e => e.PedidoId == pedido.Id).ToListAsync());
        Assert.Null(evento.EstadoAnterior);
        Assert.Equal(EstadoPedido.Borrador, evento.EstadoNuevo);
        Assert.Equal("sistema", evento.ActorTipo);
        Assert.Null(evento.ActorUsuarioId);
        Assert.Equal("automatico", evento.ActorTexto);
    }

    [Fact]
    public async Task Cambio_de_estado_por_GuardarComoAsync_registra_actor_y_motivo()
    {
        var usuario = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync();

        await using var db = baseDatos.CrearContexto();
        var guardado = await db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        guardado.Estado = EstadoPedido.Cancelado;
        await db.GuardarComoAsync(usuario.Id, "El cliente lo pidió");

        var eventos = await db.PedidoEventos.Where(e => e.PedidoId == pedido.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, eventos.Count);
        Assert.Equal(EstadoPedido.Borrador, eventos[1].EstadoAnterior);
        Assert.Equal(EstadoPedido.Cancelado, eventos[1].EstadoNuevo);
        Assert.Equal("usuario", eventos[1].ActorTipo);
        Assert.Equal(usuario.Id, eventos[1].ActorUsuarioId);
        Assert.Equal("El cliente lo pidió", eventos[1].Motivo);
    }

    [Fact]
    public async Task El_actor_no_queda_pegado_a_la_conexion_para_la_escritura_siguiente()
    {
        var usuario = await baseDatos.UsuarioAsync();
        var primero = await baseDatos.PedidoAsync();

        await using (var db = baseDatos.CrearContexto())
        {
            (await db.Pedidos.SingleAsync(p => p.Id == primero.Id)).Estado = EstadoPedido.Cancelado;
            await db.GuardarComoAsync(usuario.Id, "motivo de otro pedido");
        }

        // Alta sin actor, que puede caer en la misma conexión del pool.
        var segundo = await baseDatos.PedidoAsync();

        await using var lectura = baseDatos.CrearContexto();
        var evento = await lectura.PedidoEventos.SingleAsync(e => e.PedidoId == segundo.Id);
        Assert.Equal("sistema", evento.ActorTipo);
        Assert.Null(evento.Motivo);
    }

    [Fact]
    public async Task Editar_un_pedido_sin_cambiar_el_estado_no_agrega_eventos()
    {
        var pedido = await baseDatos.PedidoAsync();

        await baseDatos.EjecutarAsync($"update pedidos set observaciones = 'Tocar timbre' where id = {pedido.Id}");

        var eventos = await baseDatos.EscalarAsync<long>($"select count(*) from pedido_eventos where pedido_id = {pedido.Id}");
        Assert.Equal(1, eventos);
    }

    // ---- trg_log_inmutable ----

    [Theory]
    [InlineData("update pedido_eventos set motivo = 'reescrito' where pedido_id = {0}")]
    [InlineData("delete from pedido_eventos where pedido_id = {0}")]
    public async Task La_historia_de_estados_no_se_edita_ni_se_borra(string sql)
    {
        var pedido = await baseDatos.PedidoAsync();

        await baseDatos.RechazaPorTriggerAsync(string.Format(sql, pedido.Id), "pedido_eventos es de solo inserción");
    }

    // ---- trg_congelar_pedido ----

    [Theory]
    [InlineData("precio_base = 1")]
    [InlineData("recargo_urgencia = 1")]
    [InlineData("descuento_ruta = 1")]
    [InlineData("descuento_rango = 1")]
    [InlineData("peajes = 1")]
    [InlineData("total = 1")]
    [InlineData("precio_manual = 1")]
    [InlineData("km_cobrados = 1")]
    [InlineData("recargo_km = 1")]
    [InlineData("km_fuente = 'manual'")]
    [InlineData("km_manual = 1")]
    public async Task Pedido_confirmado_no_admite_cambios_de_precio(string asignacion)
    {
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Confirmado);

        await baseDatos.RechazaPorTriggerAsync(
            $"update pedidos set {asignacion} where id = {pedido.Id}", "precio y destino no se modifican");
    }

    [Fact]
    public async Task Pedido_confirmado_no_admite_cambio_de_destino()
    {
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Confirmado);
        var otroDestino = await baseDatos.UbicacionAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update pedidos set destino_ubicacion_id = {otroDestino.Id} where id = {pedido.Id}",
            "precio y destino no se modifican");
    }

    [Theory]
    [InlineData(EstadoPedido.EnRuta)]
    [InlineData(EstadoPedido.Entregado)]
    [InlineData(EstadoPedido.Fallido)]
    [InlineData(EstadoPedido.Cancelado)]
    public async Task El_precio_sigue_congelado_en_los_estados_posteriores(EstadoPedido estado)
    {
        var pedido = await baseDatos.PedidoAsync(estado);

        await baseDatos.RechazaPorTriggerAsync(
            $"update pedidos set total = 1 where id = {pedido.Id}", "precio y destino no se modifican");
    }

    [Fact]
    public async Task En_borrador_el_precio_se_puede_fijar_y_confirmar_en_la_misma_escritura()
    {
        var pedido = await baseDatos.PedidoAsync();

        var filas = await baseDatos.EjecutarAsync(
            $"update pedidos set precio_base = 1500, total = 1500, estado = 'confirmado' where id = {pedido.Id}");

        Assert.Equal(1, filas);
    }

    [Fact]
    public async Task Pedido_confirmado_admite_cambios_que_no_son_precio_ni_destino()
    {
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Confirmado);

        var filas = await baseDatos.EjecutarAsync(
            $"update pedidos set observaciones = 'Tocar timbre', destinatario_telefono = '1160000000', estado = 'en_ruta' where id = {pedido.Id}");

        Assert.Equal(1, filas);
    }

    // ---- trg_bloquear_direccion_dudosa ----

    [Theory]
    [InlineData(true, null, true)]      // verificada a mano: alcanza
    [InlineData(true, "fallida", false)]
    [InlineData(false, "alta", true)]
    [InlineData(false, "media", true)]
    public async Task Direccion_apta_entra_a_una_ruta(bool verificada, string? confianza, bool conZona)
    {
        var destino = await baseDatos.UbicacionAsync(verificada, confianza, conZona: conZona);
        var pedido = await baseDatos.PedidoAsync(destinoId: destino.Id);
        var parada = await baseDatos.ParadaAsync((await baseDatos.RutaAsync()).Id, destino.Id);

        var filas = await baseDatos.EjecutarAsync(
            $"insert into parada_pedidos (parada_id, pedido_id) values ({parada.Id}, {pedido.Id})");

        Assert.Equal(1, filas);
    }

    [Theory]
    [InlineData("baja", true)]
    [InlineData("fallida", true)]
    [InlineData(null, true)]       // nunca se geolocalizó
    [InlineData("alta", false)]    // geolocalizada pero la localidad no tiene zona: no se puede cotizar
    [InlineData("media", false)]
    public async Task Direccion_dudosa_no_entra_a_una_ruta(string? confianza, bool conZona)
    {
        var destino = await baseDatos.UbicacionAsync(verificada: false, confianza, conZona: conZona);
        var pedido = await baseDatos.PedidoAsync(destinoId: destino.Id);
        var parada = await baseDatos.ParadaAsync((await baseDatos.RutaAsync()).Id, destino.Id);

        await baseDatos.RechazaPorTriggerAsync(
            $"insert into parada_pedidos (parada_id, pedido_id) values ({parada.Id}, {pedido.Id})",
            "dirección sin geolocalizar, de baja confianza o sin zona");
    }

    [Fact]
    public async Task Direccion_sin_localidad_pero_verificada_a_mano_entra_a_una_ruta()
    {
        var destino = await baseDatos.UbicacionAsync(verificada: true, confianza: null, conLocalidad: false);
        var pedido = await baseDatos.PedidoAsync(destinoId: destino.Id);
        var parada = await baseDatos.ParadaAsync((await baseDatos.RutaAsync()).Id, destino.Id);

        var filas = await baseDatos.EjecutarAsync(
            $"insert into parada_pedidos (parada_id, pedido_id) values ({parada.Id}, {pedido.Id})");

        Assert.Equal(1, filas);
    }

    [Fact]
    public async Task Ubicacion_apta_responde_false_y_no_null_para_un_id_inexistente()
    {
        var apta = await baseDatos.EscalarAsync<bool>("select ubicacion_apta(-1)");

        Assert.False(apta);
    }

    // Migración CorregirUbicacionAptaSinLocalidad: antes ubicacion_apta devolvía null sin localidad y
    // el trigger la dejaba pasar.
    [Theory]
    [InlineData("baja")]
    [InlineData("alta")]   // sin localidad no hay zona
    [InlineData(null)]
    public async Task Direccion_sin_localidad_y_sin_verificar_no_entra_a_una_ruta(string? confianza)
    {
        var destino = await baseDatos.UbicacionAsync(verificada: false, confianza, conLocalidad: false);
        var pedido = await baseDatos.PedidoAsync(destinoId: destino.Id);
        var parada = await baseDatos.ParadaAsync((await baseDatos.RutaAsync()).Id, destino.Id);

        await baseDatos.RechazaPorTriggerAsync(
            $"insert into parada_pedidos (parada_id, pedido_id) values ({parada.Id}, {pedido.Id})",
            "dirección sin geolocalizar, de baja confianza o sin zona");
    }
}
