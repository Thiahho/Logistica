using Logistica.Dominio;

namespace Logistica.Tests;

// Acta RF-45 / §7: la urgencia entra en la ventana y desplaza como mucho dos paradas pendientes.
public class UrgenciasTests
{
    private static readonly TimeOnly Trece = new(13, 0);

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(13, 59, true)]
    [InlineData(14, 0, false)]
    [InlineData(12, 59, false)]
    public void Ventana_de_una_hora(int h, int m, bool abierta) =>
        Assert.Equal(abierta, Urgencias.VentanaAbierta(new TimeOnly(h, m), Trece, 60));

    // Pendientes: paradas 10, 11, 12 y 13 en los órdenes 3 a 6 (1 y 2 ya se hicieron).
    private static readonly List<ParadaPendiente> Pendientes =
    [
        new(10, 100, 3), new(11, 101, 4), new(12, 102, 5), new(13, 103, 6),
    ];

    [Fact]
    public void Al_final_no_desplaza_a_nadie()
    {
        var i = Urgencias.Resolver(Pendientes, destinoUbicacionId: 999, antesDeParadaId: null, ultimoOrden: 6)!;
        Assert.Equal(7, i.Orden);
        Assert.Equal(0, i.Desplazadas);
        Assert.Null(i.ParadaConsolidadaId);
    }

    [Fact]
    public void Antes_de_la_penultima_desplaza_dos()
    {
        var i = Urgencias.Resolver(Pendientes, 999, antesDeParadaId: 12, ultimoOrden: 6)!;
        Assert.Equal(5, i.Orden);
        Assert.Equal(2, i.Desplazadas);
    }

    [Fact]
    public void Antes_de_la_primera_pendiente_desplaza_todas()
    {
        Assert.Equal(4, Urgencias.Resolver(Pendientes, 999, antesDeParadaId: 10, ultimoOrden: 6)!.Desplazadas);
    }

    [Fact]
    public void Mismo_destino_se_consolida_sin_desplazar()
    {
        var i = Urgencias.Resolver(Pendientes, destinoUbicacionId: 101, antesDeParadaId: 10, ultimoOrden: 6)!;
        Assert.Equal(11, i.ParadaConsolidadaId);
        Assert.Equal(0, i.Desplazadas);
    }

    [Fact]
    public void Parada_que_no_es_pendiente_da_null()
    {
        Assert.Null(Urgencias.Resolver(Pendientes, 999, antesDeParadaId: 1, ultimoOrden: 6));
    }
}
