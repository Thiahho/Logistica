using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// Cierre de ciclo sin actor, como lo llama Servicios/CierreCiclosAutomatico.cs: emite, no repite y no
// se frena por ajustes sin aprobar.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class CierreDeCiclosTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly FinDeSeptiembre = new(2026, 9, 30);
    private static readonly DateOnly PrimeroDeOctubre = new(2026, 10, 1);

    private async Task<Pedido> PedidoConItemAsync(decimal? monto, string estado, string tipo = "pedido")
    {
        var pedido = await baseDatos.PedidoAsync();
        await ItemAsync(pedido.Id, monto, estado, tipo);
        return pedido;
    }

    private async Task ItemAsync(long pedidoId, decimal? monto, string estado, string tipo)
    {
        await using var db = baseDatos.CrearContexto();
        db.FacturaItems.Add(new FacturaItem
        {
            PedidoId = pedidoId, Tipo = tipo, Descripcion = "Envío de prueba", Monto = monto, Estado = estado,
            CreadoEn = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<ResultadoCierreCliente>> CerrarAsync(int clienteId)
    {
        await using var db = baseDatos.CrearContexto();
        return await new CuentaCorrienteService(db).CerrarCiclosAsync(
            PrimeroDeOctubre, clienteId, previsualizar: false, actor: null, CancellationToken.None);
    }

    [Fact]
    public async Task El_cierre_sin_actor_emite_la_factura_del_periodo_vencido()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");

        var resultado = Assert.Single(await CerrarAsync(pedido.ClienteId));

        Assert.NotNull(resultado.FacturaId);
        Assert.Equal(FinDeSeptiembre, resultado.PeriodoHasta);
        Assert.Equal(1000m, resultado.Total);
        await using var db = baseDatos.CrearContexto();
        var factura = await db.Facturas.SingleAsync(f => f.ClienteId == pedido.ClienteId);
        Assert.Null(factura.EmitidaPor);
        Assert.Equal(new DateOnly(2026, 10, 10), factura.FechaVencimiento); // mensual: cierre + 10 días
        Assert.Equal(factura.Id, (await db.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id)).FacturaId);
    }

    [Fact]
    public async Task Repetir_el_cierre_no_emite_una_segunda_factura()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await CerrarAsync(pedido.ClienteId);

        var segunda = await CerrarAsync(pedido.ClienteId);

        Assert.DoesNotContain(segunda, r => r.FacturaId is not null);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(1, await db.Facturas.CountAsync(f => f.ClienteId == pedido.ClienteId));
    }

    [Fact]
    public async Task Un_ajuste_sin_aprobar_no_frena_la_factura_y_queda_para_la_siguiente()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await ItemAsync(pedido.Id, null, "pendiente", "ajuste");

        var resultado = Assert.Single(await CerrarAsync(pedido.ClienteId));

        Assert.NotNull(resultado.FacturaId);
        Assert.Equal(1000m, resultado.Total);
        Assert.Equal(1, resultado.AjustesPendientes);
        await using var db = baseDatos.CrearContexto();
        var ajuste = await db.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id && i.Tipo == "ajuste");
        Assert.Null(ajuste.FacturaId);
    }

    [Fact]
    public async Task Un_cliente_sin_nada_para_facturar_no_recibe_factura()
    {
        var cliente = await baseDatos.ClienteAsync();

        var resultados = await CerrarAsync(cliente.Id);

        Assert.DoesNotContain(resultados, r => r.FacturaId is not null);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(0, await db.Facturas.CountAsync(f => f.ClienteId == cliente.Id));
    }
}
