using Logistica.Entidades;
using Logistica.Servicios;

namespace Logistica.Tests;

// Estado visible de un viaje que no tiene ruta propia (la baja de la empresa le quitó la propuesta y
// nada se la devuelve): sale de sus envíos, que al reanudarse se rutean como cualquier otro pedido.
public class EstadoDeViajeTests
{
    private static EnvioDeViaje Envio(EstadoPedido estado, bool enCurso = false, bool planificada = false) =>
        new(estado, enCurso, planificada);

    [Fact]
    public void Con_un_envio_en_la_calle_el_viaje_esta_en_curso_aunque_el_resto_no_haya_salido()
    {
        Assert.Equal("en_curso", ViajeService.EstadoSinRutaPropia(
        [
            Envio(EstadoPedido.EnRuta, enCurso: true),
            Envio(EstadoPedido.Borrador, planificada: true),
            Envio(EstadoPedido.Reprogramado),
            Envio(EstadoPedido.Retenido),
            Envio(EstadoPedido.Entregado, enCurso: true),
        ]));
    }

    [Theory]
    [InlineData(EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Confirmado)]
    [InlineData(EstadoPedido.Reprogramado)]
    public void Con_un_envio_en_una_ruta_en_planificacion_el_viaje_esta_planificado(EstadoPedido estado)
    {
        Assert.Equal("planificado", ViajeService.EstadoSinRutaPropia(
        [
            Envio(estado, planificada: true),
            Envio(EstadoPedido.Borrador),
            Envio(EstadoPedido.Retenido),
            Envio(EstadoPedido.Entregado, enCurso: true),
        ]));
    }

    [Theory]
    [InlineData(EstadoPedido.Borrador)]
    [InlineData(EstadoPedido.Confirmado)]
    [InlineData(EstadoPedido.Reprogramado)]
    public void Con_un_envio_pendiente_que_no_esta_en_ninguna_ruta_el_viaje_sigue_sin_ruta(EstadoPedido estado)
    {
        Assert.Equal("sin_ruta", ViajeService.EstadoSinRutaPropia(
        [
            Envio(estado),
            Envio(EstadoPedido.Retenido),
            Envio(EstadoPedido.Entregado),
        ]));
    }

    [Fact]
    public void La_fila_que_un_reprogramado_conserva_donde_fallo_no_es_una_ruta_vigente()
    {
        // Falló en una ruta que sigue en curso (o ya cerró): esa fila es historia de ese intento.
        Assert.Equal("sin_ruta", ViajeService.EstadoSinRutaPropia([Envio(EstadoPedido.Reprogramado, enCurso: true)]));
        // Y cuando vuelve a entrar en un armado, lo que cuenta es la ruta nueva.
        Assert.Equal("planificado", ViajeService.EstadoSinRutaPropia(
            [Envio(EstadoPedido.Reprogramado, enCurso: true, planificada: true)]));
    }

    [Fact]
    public void Sin_ningun_envio_pendiente_y_con_alguno_retenido_el_viaje_esta_retenido()
    {
        Assert.Equal("retenido", ViajeService.EstadoSinRutaPropia(
        [
            Envio(EstadoPedido.Retenido),
            Envio(EstadoPedido.Entregado),
            Envio(EstadoPedido.Fallido, enCurso: true),
            Envio(EstadoPedido.Cancelado),
        ]));
    }

    [Fact]
    public void Con_todos_los_envios_resueltos_el_viaje_esta_cerrado()
    {
        Assert.Equal("cerrado", ViajeService.EstadoSinRutaPropia(
        [
            Envio(EstadoPedido.Entregado),
            Envio(EstadoPedido.Fallido),
            Envio(EstadoPedido.Devuelto),
            Envio(EstadoPedido.Cancelado),
        ]));
    }

    [Fact]
    public void Los_cancelados_no_deciden_y_si_no_queda_otro_el_viaje_esta_cerrado()
    {
        Assert.Equal("cerrado", ViajeService.EstadoSinRutaPropia(
            [Envio(EstadoPedido.Cancelado), Envio(EstadoPedido.Cancelado, planificada: true)]));
    }

    [Fact]
    public void Un_viaje_sin_envios_sigue_sin_ruta()
    {
        Assert.Equal("sin_ruta", ViajeService.EstadoSinRutaPropia([]));
    }
}
