using Logistica.Dominio;

namespace Logistica.Tests;

// RF-08 (acta §7): pasada la hora de corte no entran pedidos nuevos con entrega mañana.
public class CorteDeCargaTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 2);
    private static readonly TimeOnly Corte = new(18, 0);

    [Theory]
    [InlineData(17, 59, false)]
    [InlineData(18, 0, false)]   // a las 18:00 en punto todavía entra
    [InlineData(18, 1, true)]
    [InlineData(23, 59, true)]
    public void La_carga_de_manana_cierra_pasada_la_hora_de_corte(int hora, int minuto, bool cerrada) =>
        Assert.Equal(cerrada, CorteDeCarga.Cerrada(Hoy.AddDays(1), Hoy, new TimeOnly(hora, minuto), Corte));

    [Theory]
    [InlineData(-5)]   // carga retroactiva del back-office
    [InlineData(0)]    // hoy: urgencias y carga del día siguen sus propias reglas
    [InlineData(2)]
    [InlineData(30)]
    public void El_corte_solo_alcanza_a_los_pedidos_para_manana(int dias) =>
        Assert.False(CorteDeCarga.Cerrada(Hoy.AddDays(dias), Hoy, new TimeOnly(20, 0), Corte));

    [Fact]
    public void Antes_del_corte_la_primera_fecha_abierta_es_manana() =>
        Assert.Equal(Hoy.AddDays(1), CorteDeCarga.PrimeraFechaAbierta(Hoy, new TimeOnly(10, 0), Corte));

    [Fact]
    public void Despues_del_corte_la_primera_fecha_abierta_es_pasado_manana() =>
        Assert.Equal(Hoy.AddDays(2), CorteDeCarga.PrimeraFechaAbierta(Hoy, new TimeOnly(19, 0), Corte));

    [Fact]
    public void El_mensaje_dice_la_hora_y_la_fecha_que_sigue_abierta() =>
        Assert.Equal(
            "La carga para mañana cerró a las 18:00; esto se carga a partir del 04/10.",
            CorteDeCarga.Mensaje(Corte, Hoy.AddDays(2)));
}
