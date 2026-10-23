using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// E1: un pedido que el back-office marca Entregado se factura igual que el que cierra el repartidor
// desde la parada. Sin el ítem, esa entrega no entra en ningún cierre de ciclo.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class EntregaDesdeBackOfficeTests(BaseDePrueba baseDatos)
{
    // CambiarEstado solo usa la base y la cuenta corriente: el resto de las dependencias no se tocan.
    private static PedidosController Controlador(LogisticaDbContext db, Guid usuarioId) =>
        new PedidosController(db, null!, null!, null!, null!, new CuentaCorrienteService(db), null!, null!, null!, new RetencionPedidosService(db))
            .ComoUsuario(usuarioId);

    [Fact]
    public async Task Marcar_entregado_desde_el_back_office_acuna_el_item_facturable()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.EnRuta);

        await using (var db = baseDatos.CrearContexto())
        {
            var respuesta = await Controlador(db, actor.Id).CambiarEstado(
                pedido.Id, new PedidosController.CambiarEstadoRequest("Entregado", null, null), CancellationToken.None);
            Assert.IsType<NoContentResult>(respuesta);
        }

        await using var lectura = baseDatos.CrearContexto();
        Assert.Equal(EstadoPedido.Entregado, (await lectura.Pedidos.SingleAsync(p => p.Id == pedido.Id)).Estado);
        var item = Assert.Single(await lectura.FacturaItems.Where(i => i.PedidoId == pedido.Id).ToListAsync());
        Assert.Equal("pedido", item.Tipo);
        Assert.Equal("aprobado", item.Estado);
        Assert.Equal(1000m, item.Monto);
        Assert.Equal($"Pedido #{pedido.Id} — Destinatario", item.Descripcion);
        Assert.Equal(actor.Id, item.CreadoPor);
        Assert.Null(item.FacturaId);
    }

    [Fact]
    public async Task Un_pedido_sin_precio_congelado_no_se_marca_entregado()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedido = await PedidoEnRutaSinPrecioAsync();

        await using (var db = baseDatos.CrearContexto())
        {
            var respuesta = await Controlador(db, actor.Id).CambiarEstado(
                pedido.Id, new PedidosController.CambiarEstadoRequest("Entregado", null, null), CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(respuesta);
        }

        await using var lectura = baseDatos.CrearContexto();
        Assert.Equal(EstadoPedido.EnRuta, (await lectura.Pedidos.SingleAsync(p => p.Id == pedido.Id)).Estado);
        Assert.False(await lectura.FacturaItems.AnyAsync(i => i.PedidoId == pedido.Id));
    }

    /// <summary>La aplicación nunca deja un pedido en ruta sin precio (CerrarPlanificacion lo cotiza antes),
    /// pero nada en la base lo impide: es el caso que la entrega no puede facturar.</summary>
    private async Task<Pedido> PedidoEnRutaSinPrecioAsync()
    {
        var modelo = await baseDatos.PedidoAsync();
        await using var db = baseDatos.CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = modelo.ClienteId,
            OrigenUbicacionId = modelo.OrigenUbicacionId,
            DestinoUbicacionId = modelo.DestinoUbicacionId,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = modelo.FechaEntrega,
            Estado = EstadoPedido.EnRuta,
        };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }
}
