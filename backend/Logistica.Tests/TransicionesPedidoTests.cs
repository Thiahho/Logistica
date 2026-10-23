using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Tests;

// Retenido (empresa dada de baja por falta de pago) dentro de la máquina de estados del pedido.
public class TransicionesPedidoTests
{
    [Theory]
    [InlineData(EstadoPedido.Reprogramado)]
    [InlineData(EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Devuelto)]
    public void De_retenido_se_sale_reanudando_o_devolviendo(EstadoPedido nuevo) =>
        Assert.True(TransicionesPedido.Permitida(EstadoPedido.Retenido, nuevo));

    // Cancelado queda afuera a propósito: cancelar un pedido ya confirmado cobra el 100%.
    [Theory]
    [InlineData(EstadoPedido.Cancelado)]
    [InlineData(EstadoPedido.Confirmado)]
    [InlineData(EstadoPedido.EnRuta)]
    [InlineData(EstadoPedido.Entregado)]
    [InlineData(EstadoPedido.Fallido)]
    [InlineData(EstadoPedido.Retenido)]
    public void De_retenido_no_se_pasa_a_ningun_otro_estado(EstadoPedido nuevo) =>
        Assert.False(TransicionesPedido.Permitida(EstadoPedido.Retenido, nuevo));

    // A Retenido solo lleva la baja de la empresa, nunca la transición genérica.
    [Fact]
    public void Ninguna_transicion_generica_lleva_a_retenido() =>
        Assert.All(Enum.GetValues<EstadoPedido>(), actual =>
            Assert.False(TransicionesPedido.Permitida(actual, EstadoPedido.Retenido)));

    [Theory]
    [InlineData(EstadoPedido.Borrador, EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Confirmado, EstadoPedido.Reprogramado)]
    [InlineData(EstadoPedido.EnRuta, EstadoPedido.Reprogramado)]
    [InlineData(EstadoPedido.Reprogramado, EstadoPedido.Reprogramado)]
    public void Se_reanuda_en_borrador_solo_lo_que_nunca_se_confirmo(EstadoPedido alRetener, EstadoPedido esperado) =>
        Assert.Equal(esperado, TransicionesPedido.ReanudaEn(alRetener));
}
