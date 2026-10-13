using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace Logistica.Tests.BaseDeDatos;

// El porcentaje de cancelaciones del tablero es "cancelados sobre los pedidos con fecha de entrega en el
// período, excluidos los que siguen en borrador" (docs/diseño_b2_tablero.md). Un borrador que la baja de
// su empresa dejó retenido sigue sin haberse confirmado nunca: tampoco entra en ese total.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class TableroDeIndicadoresTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly Dia = new(2026, 10, 5);

    private static ClientesController Clientes(LogisticaDbContext db, Guid usuarioId) =>
        new ClientesController(db, null!, null!, null!, null!, null!, new RetencionPedidosService(db)).ComoUsuario(usuarioId);

    private async Task PedidoDeAsync(int clienteId, EstadoPedido estado)
    {
        var origen = await baseDatos.UbicacionAsync();
        var destino = await baseDatos.UbicacionAsync();

        await using var db = baseDatos.CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = clienteId,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destino.Id,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = Dia,
            Estado = estado,
        };
        if (estado != EstadoPedido.Borrador)
        {
            pedido.PrecioBase = 1000m;
            pedido.Total = 1000m;
        }
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
    }

    /// <summary>El tablero del día, acotado a la empresa: lo que carguen las otras pruebas no lo mueve.</summary>
    private async Task<TableroController.Tablero> TableroAsync(int clienteId, Guid actor)
    {
        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(await new TableroController(db).ComoUsuario(actor)
            .Obtener(Dia, Dia, clienteId, zonaId: null, vehiculoId: null, repartidorId: null, CancellationToken.None));
        return Assert.IsType<TableroController.Tablero>(respuesta.Value);
    }

    [Fact]
    public async Task El_borrador_que_quedo_retenido_no_entra_en_el_total_del_porcentaje_de_cancelaciones()
    {
        var actor = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        await PedidoDeAsync(cliente.Id, EstadoPedido.Cancelado);
        await PedidoDeAsync(cliente.Id, EstadoPedido.Entregado);
        await PedidoDeAsync(cliente.Id, EstadoPedido.EnRuta);
        await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador);
        await PedidoDeAsync(cliente.Id, EstadoPedido.Borrador);

        // Un cancelado sobre tres pedidos que salieron de borrador: los dos borradores no cuentan.
        var antes = await TableroAsync(cliente.Id, actor.Id);
        Assert.Equal(1, antes.Cancelados);
        Assert.Equal(33.3m, antes.PctCancelaciones);

        // La baja retiene el que estaba en ruta y los dos borradores.
        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Clientes(db, actor.Id).CambiarActivo(
                cliente.Id, new ClientesController.ActivoRequest(false), CancellationToken.None));

        // El total no cambia: el que ya estaba confirmado sigue contando, y los borradores retenidos
        // siguen sin haberse confirmado nunca.
        var despues = await TableroAsync(cliente.Id, actor.Id);
        Assert.Equal(1, despues.Cancelados);
        Assert.Equal(33.3m, despues.PctCancelaciones);
        Assert.Equal(1, despues.Entregas);
    }
}
