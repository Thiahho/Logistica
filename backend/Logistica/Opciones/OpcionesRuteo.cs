namespace Logistica.Opciones;

/// <summary>
/// Proveedor del recorrido por calles (Servicios/RuteoService.cs). "osrm": el demo público en
/// desarrollo — su política de uso no admite producción. "ors": OpenRouteService, producción, con
/// ApiKey. "ninguno": sin recorrido por calles — el mapa dibuja línea recta y DistanciaService cae
/// a haversine, igual que cuando el proveedor no responde.
/// </summary>
public class OpcionesRuteo
{
    public string Proveedor { get; set; } = "osrm";

    public string BaseUrl { get; set; } = "https://router.project-osrm.org/";

    /// <summary>Solo "ors". Nunca en appsettings.json: variable de entorno Ruteo__ApiKey.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Sin tope, HttpClient espera 100 s: un proveedor colgado frenaba el armado de la ruta
    /// y la jornada del repartidor, que embebe el recorrido.</summary>
    public int TimeoutSegundos { get; set; } = 10;
}
