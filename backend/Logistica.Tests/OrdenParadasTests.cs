using Logistica.Dominio;

namespace Logistica.Tests;

// Viajes con varias paradas: orden sugerido desde el origen (vecino más cercano + 2-opt), con
// consolidación por dirección (RF-14) y las paradas sin coordenadas al final.
public class OrdenParadasTests
{
    // Una "línea" de puntos hacia el este del origen, ~1 km entre cada uno.
    private static readonly OrdenParadas.Punto Origen = new(-34.6000m, -58.4000m);
    private static OrdenParadas.Parada P(long id, int kmEste) => new(id, -34.6000m, -58.4000m + kmEste * 0.0109m);

    [Fact]
    public void Ordena_por_cercania_aunque_se_hayan_cargado_desordenadas()
    {
        var r = OrdenParadas.Sugerir(Origen, [P(3, 3), P(1, 1), P(5, 5), P(2, 2), P(4, 4)]);
        Assert.Equal([1L, 2, 3, 4, 5], r.Ubicaciones);
        Assert.InRange(r.MetrosLineaRecta, 4900, 5100);
    }

    [Fact]
    public void El_orden_sugerido_nunca_es_mas_largo_que_el_de_carga()
    {
        var cargadas = new[]
        {
            new OrdenParadas.Parada(1, -34.61m, -58.38m), new OrdenParadas.Parada(2, -34.58m, -58.43m),
            new OrdenParadas.Parada(3, -34.63m, -58.36m), new OrdenParadas.Parada(4, -34.59m, -58.41m),
            new OrdenParadas.Parada(5, -34.62m, -58.44m), new OrdenParadas.Parada(6, -34.57m, -58.37m),
        };
        var r = OrdenParadas.Sugerir(Origen, cargadas);

        var deCarga = OrdenParadas.Metros(Origen, cargadas.Select(p => new OrdenParadas.Punto(p.Lat!.Value, p.Lng!.Value)).ToList());
        Assert.True(r.MetrosLineaRecta <= deCarga);
        Assert.Equal(6, r.Ubicaciones.Count);
    }

    [Fact]
    public void Dos_paradas_en_la_misma_direccion_son_un_solo_punto()
    {
        var r = OrdenParadas.Sugerir(Origen, [P(7, 2), P(8, 1), P(7, 2)]);
        Assert.Equal([8L, 7], r.Ubicaciones);
    }

    [Fact]
    public void Las_paradas_sin_coordenadas_quedan_al_final_en_orden_de_carga()
    {
        var r = OrdenParadas.Sugerir(Origen,
        [
            new OrdenParadas.Parada(9, null, null), P(2, 2), new OrdenParadas.Parada(10, null, null), P(1, 1),
        ]);
        Assert.Equal([1L, 2, 9, 10], r.Ubicaciones);
        Assert.Equal([9L, 10], r.SinCoordenadas);
    }

    [Fact]
    public void Una_sola_parada()
    {
        var r = OrdenParadas.Sugerir(Origen, [P(1, 3)]);
        Assert.Equal([1L], r.Ubicaciones);
        Assert.InRange(r.MetrosLineaRecta, 2900, 3100);
    }
}
