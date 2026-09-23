namespace Logistica.Opciones;

/// <summary>
/// De dónde sale el km que cobra DistanciaService.ResolverAsync cuando no hay km_manual cargado.
/// "ruta": distancia real por calles vía RuteoService (OSRM), con fallback automático a "recta"
/// si el proveedor no responde. "recta": Dominio/Geo.cs (haversine) directo, sin depender de un
/// proveedor externo. Una línea de configuración, no una reescritura, si cambia el criterio
/// comercial (mismo espíritu que Ruteo:BaseUrl).
/// </summary>
public class OpcionesDistancia
{
    public string Fuente { get; set; } = "ruta";
}
