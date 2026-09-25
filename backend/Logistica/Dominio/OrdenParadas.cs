namespace Logistica.Dominio;

/// <summary>
/// Orden sugerido de las paradas de un viaje: vecino más cercano desde el origen + mejora 2-opt,
/// sobre distancia en línea recta (haversine). Es el mismo algoritmo que corre en el navegador al
/// armar una ruta (frontend/lib/dominio/ruteo.ts, sugerirOrden), del lado del servidor porque acá
/// el orden se guarda como la ruta propuesta del viaje. Sin matriz de distancias por calle, igual
/// que el armado (construccion_v1.md §1): el orden es un borrador que Operación revisa (acta §2,
/// "ruteo automático sin revisión humana — nunca").
///
/// Consolida por ubicación (RF-14): dos paradas con la misma dirección son un solo punto del
/// recorrido. Las paradas sin coordenadas no se pueden ordenar y quedan al final, en el orden en
/// que se cargaron.
/// </summary>
public static class OrdenParadas
{
    public record Punto(decimal Lat, decimal Lng);

    /// <summary>Una parada a ordenar: la clave agrupa las que van a la misma dirección.</summary>
    public record Parada(long UbicacionId, decimal? Lat, decimal? Lng);

    /// <summary>Resultado: las ubicaciones en el orden sugerido (cada una una sola vez) y los metros
    /// en línea recta del recorrido origen → primera → … → última (sin volver).</summary>
    public record Resultado(IReadOnlyList<long> Ubicaciones, IReadOnlyList<long> SinCoordenadas, int MetrosLineaRecta);

    public static Resultado Sugerir(Punto origen, IReadOnlyList<Parada> paradas)
    {
        // Primera aparición de cada ubicación manda (RF-14): el orden de carga desempata.
        var unicas = paradas.GroupBy(p => p.UbicacionId).Select(g => g.First()).ToList();
        var conCoordenadas = unicas.Where(p => p.Lat is not null && p.Lng is not null)
            .Select(p => (p.UbicacionId, Punto: new Punto(p.Lat!.Value, p.Lng!.Value))).ToList();
        var sinCoordenadas = unicas.Where(p => p.Lat is null || p.Lng is null).Select(p => p.UbicacionId).ToList();

        var orden = VecinoMasCercano(origen, conCoordenadas);
        Mejorar2Opt(origen, orden);

        return new Resultado(
            orden.Select(o => o.UbicacionId).Concat(sinCoordenadas).ToList(),
            sinCoordenadas,
            Metros(origen, orden.Select(o => o.Punto).ToList()));
    }

    /// <summary>Metros en línea recta de un recorrido que sale de `origen` y visita `puntos` en orden.</summary>
    public static int Metros(Punto origen, IReadOnlyList<Punto> puntos)
    {
        var total = 0;
        var actual = origen;
        foreach (var p in puntos)
        {
            total += Distancia(actual, p);
            actual = p;
        }
        return total;
    }

    private static int Distancia(Punto a, Punto b) => Geo.DistanciaMetros(a.Lat, a.Lng, b.Lat, b.Lng);

    private static List<(long UbicacionId, Punto Punto)> VecinoMasCercano(Punto origen, List<(long UbicacionId, Punto Punto)> paradas)
    {
        var restantes = new List<(long UbicacionId, Punto Punto)>(paradas);
        var ordenadas = new List<(long UbicacionId, Punto Punto)>();
        var actual = origen;
        while (restantes.Count > 0)
        {
            var mejor = 0;
            for (var i = 1; i < restantes.Count; i++)
                if (Distancia(actual, restantes[i].Punto) < Distancia(actual, restantes[mejor].Punto)) mejor = i;
            var siguiente = restantes[mejor];
            restantes.RemoveAt(mejor);
            ordenadas.Add(siguiente);
            actual = siguiente.Punto;
        }
        return ordenadas;
    }

    /// <summary>Invierte tramos mientras acorten el recorrido (más de 1 m, para no ciclar por
    /// redondeo). Muta `orden`.</summary>
    private static void Mejorar2Opt(Punto origen, List<(long UbicacionId, Punto Punto)> orden)
    {
        if (orden.Count < 3) return;
        int Total(List<(long UbicacionId, Punto Punto)> r) => Metros(origen, r.Select(x => x.Punto).ToList());

        var mejoro = true;
        while (mejoro)
        {
            mejoro = false;
            for (var i = 0; i < orden.Count - 1; i++)
            {
                for (var j = i + 1; j < orden.Count; j++)
                {
                    var candidato = new List<(long UbicacionId, Punto Punto)>(orden);
                    candidato.Reverse(i, j - i + 1);
                    if (Total(candidato) < Total(orden) - 1)
                    {
                        orden.Clear();
                        orden.AddRange(candidato);
                        mejoro = true;
                    }
                }
            }
        }
    }
}
