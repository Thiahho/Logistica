using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// E1 / B16: un ajuste aprobado entra en la siguiente factura sin que el cierre mire el estado del pedido.
// Un borrador todavía no tiene precio congelado y un cancelado "deja de tener valor": ninguno de los dos
// admite pedir ni aprobar un ajuste. Rechazar sí, para poder limpiar el que quedó pendiente.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class AjustesSegunEstadoDelPedidoTests(BaseDePrueba baseDatos)
{
    // Los ajustes solo usan la base: el resto de las dependencias no se tocan.
    private static PedidosController Controlador(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!, null!, null!)
            .ComoUsuario(usuarioId);

    private static readonly PedidosController.SolicitarAjusteRequest Solicitud = new(BultosReales: 3, "Vino un bulto de más.");
    private static readonly PedidosController.AprobarAjusteRequest Aprobacion = new(Monto: 500m, CargoGestion: null);

    /// <summary>Un ajuste pendiente sobre el pedido, como el que deja SolicitarAjuste. Va directo a la
    /// base para poder armar el caso del pedido que se canceló con el ajuste ya pedido.</summary>
    private async Task<FacturaItem> AjustePendienteAsync(long pedidoId, Guid creadoPor)
    {
        await using var db = baseDatos.CrearContexto();
        var ajuste = new FacturaItem
        {
            PedidoId = pedidoId, Tipo = "ajuste", Descripcion = "Bultos declarados: 1. Bultos reales: 3.",
            Monto = null, Estado = "pendiente", CreadoPor = creadoPor, CreadoEn = DateTimeOffset.UtcNow,
        };
        db.FacturaItems.Add(ajuste);
        await db.SaveChangesAsync();
        return ajuste;
    }

    private async Task<List<FacturaItem>> AjustesAsync(long pedidoId)
    {
        await using var db = baseDatos.CrearContexto();
        return await db.FacturaItems.AsNoTracking().Where(i => i.PedidoId == pedidoId && i.Tipo == "ajuste").ToListAsync();
    }

    [Theory]
    [InlineData(EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Cancelado)]
    public async Task Pedir_un_ajuste_sobre_un_pedido_en_borrador_o_cancelado_responde_409_y_no_crea_nada(EstadoPedido estado)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(estado);

        await using (var db = baseDatos.CrearContexto())
        {
            var respuesta = await Controlador(db, actor.Id).SolicitarAjuste(pedido.Id, Solicitud, CancellationToken.None);
            Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(respuesta).Value);
        }

        Assert.Empty(await AjustesAsync(pedido.Id));
    }

    [Theory]
    [InlineData(EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Cancelado)]
    public async Task Aprobar_un_ajuste_pendiente_de_un_pedido_en_borrador_o_cancelado_responde_409_y_lo_deja_pendiente(
        EstadoPedido estado)
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(estado);
        var ajuste = await AjustePendienteAsync(pedido.Id, actor.Id);

        await using (var db = baseDatos.CrearContexto())
        {
            var respuesta = await Controlador(db, actor.Id).AprobarAjuste(
                pedido.Id, ajuste.Id, Aprobacion, CancellationToken.None);
            Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(respuesta).Value);
        }

        var guardado = Assert.Single(await AjustesAsync(pedido.Id));
        Assert.Equal("pendiente", guardado.Estado);
        Assert.Null(guardado.Monto);
        Assert.Null(guardado.ResueltoEn);
    }

    // El ajuste que quedó pendiente en un pedido cancelado no se puede aprobar, pero tampoco queda
    // colgado para siempre: rechazarlo sigue permitido.
    [Fact]
    public async Task Rechazar_un_ajuste_pendiente_de_un_pedido_cancelado_sigue_permitido()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Cancelado);
        var ajuste = await AjustePendienteAsync(pedido.Id, actor.Id);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Controlador(db, actor.Id).RechazarAjuste(
                pedido.Id, ajuste.Id, new PedidosController.RechazarAjusteRequest("El pedido se canceló."),
                CancellationToken.None));

        Assert.Equal("rechazado", Assert.Single(await AjustesAsync(pedido.Id)).Estado);
    }

    // El circuito normal no cambia: retiro físico con diferencia de bultos sobre un pedido confirmado.
    [Fact]
    public async Task Sobre_un_pedido_confirmado_el_ajuste_se_pide_y_se_aprueba()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Confirmado);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(
                await Controlador(db, actor.Id).SolicitarAjuste(pedido.Id, Solicitud, CancellationToken.None));
        var pendiente = Assert.Single(await AjustesAsync(pedido.Id));

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Controlador(db, actor.Id).AprobarAjuste(
                pedido.Id, pendiente.Id, Aprobacion, CancellationToken.None));

        var aprobado = Assert.Single(await AjustesAsync(pedido.Id));
        Assert.Equal("aprobado", aprobado.Estado);
        Assert.Equal(500m, aprobado.Monto);
    }
}
