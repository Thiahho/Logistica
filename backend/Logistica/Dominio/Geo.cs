namespace Logistica.Dominio;

/// <summary>
/// Espejo en C# de frontend/lib/dominio/geo.ts. El dispositivo calcula su propio desvío para
/// mostrarlo en el momento de la captura, pero el servidor descarta ese valor y recalcula acá
/// contra la coordenada de la parada — mismo criterio que el precio (construccion_v1.md §3 regla
/// 1): si el número tiene consecuencias (RF-29, marcado sobre umbral), no viene del navegador.
/// Sin API de matriz de distancias, igual que el ruteo: con distancias cortas de reparto urbano,
/// haversine es más que suficiente.
/// </summary>
public static class Geo
{
    private const double RadioTierraM = 6371000;

    public static int DistanciaMetros(decimal latA, decimal lngA, decimal latB, decimal lngB)
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad((double)(latB - latA));
        var dLng = Rad((double)(lngB - lngA));
        var senoLat = Math.Sin(dLat / 2);
        var senoLng = Math.Sin(dLng / 2);
        var h = senoLat * senoLat + Math.Cos(Rad((double)latA)) * Math.Cos(Rad((double)latB)) * senoLng * senoLng;
        return (int)Math.Round(2 * RadioTierraM * Math.Asin(Math.Sqrt(h)));
    }
}
