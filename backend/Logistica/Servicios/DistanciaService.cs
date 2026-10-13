using Logistica.Dominio;
using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>Fuente: "ruta" | "recta" | "manual" — trazabilidad de con qué número se cobró
/// (pedidos.km_fuente), igual criterio que geo_proveedor en Ubicacion.</summary>
public record DistanciaResuelta(decimal Km, string Fuente);

/// <summary>Los argumentos de DistanciaService.ResolverAsync para un pedido. Dos solicitudes con los
/// mismos valores son la misma consulta.</summary>
public record SolicitudDistancia(
    decimal? LatOrigen, decimal? LngOrigen, decimal? LatDestino, decimal? LngDestino, decimal? KmManual);

/// <summary>
/// Resuelve el km que entra a PrecioService.CotizarAsync para el recargo por kilómetro (Anexo I
/// §10.2-N). Cascada, sin tirar nunca — un proveedor externo caído no puede bloquear una
/// cotización, mismo criterio que RuteoService.TrazarAsync degradando a línea recta:
///
/// 1. km_manual cargado → gana siempre, es la salida de escape cuando ningún proveedor sirve.
/// 2. Faltan coordenadas de origen o destino → null → PrecioService aplica recargo_km = 0
///    (fallback al precio de zona puro, ningún pedido existente cambia).
/// 3. Distancia:Fuente = "ruta" (default) → RuteoService (OSRM), cacheado 30 min.
/// 4. OSRM no responde, o Distancia:Fuente = "recta" → Dominio/Geo.cs (haversine).
/// </summary>
public class DistanciaService(RuteoService ruteo, IOptions<OpcionesDistancia> opciones)
{
    public async Task<DistanciaResuelta?> ResolverAsync(
        decimal? latOrigen, decimal? lngOrigen, decimal? latDestino, decimal? lngDestino,
        decimal? kmManual, CancellationToken ct = default)
    {
        if (kmManual is not null)
            return new DistanciaResuelta(kmManual.Value, "manual");

        if (latOrigen is null || lngOrigen is null || latDestino is null || lngDestino is null)
            return null;

        if (opciones.Value.Fuente == "ruta")
        {
            var puntos = new[]
            {
                new PuntoRuta(latOrigen.Value, lngOrigen.Value),
                new PuntoRuta(latDestino.Value, lngDestino.Value),
            };
            var recorrido = await ruteo.TrazarAsync(puntos, ct);
            if (recorrido is not null)
                return new DistanciaResuelta(recorrido.DistanciaMetros / 1000m, "ruta");
        }

        var metros = Geo.DistanciaMetros(latOrigen.Value, lngOrigen.Value, latDestino.Value, lngDestino.Value);
        return new DistanciaResuelta(metros / 1000m, "recta");
    }

    /// <summary>Cuántas distancias de un mismo lote se le piden al proveedor a la vez. Un número chico y
    /// fijo a propósito: alcanza para que el cierre de una ruta de 8-25 paradas no espere una respuesta
    /// por pedido en fila, sin mandarle al proveedor (un demo público o un plan con cuota por minuto, ver
    /// OpcionesRuteo) una ráfaga del tamaño de la ruta.</summary>
    public const int MaxConsultasALaVez = 4;

    /// <summary>
    /// ResolverAsync para varios pedidos de una vez, de a MaxConsultasALaVez en paralelo. Devuelve una
    /// tarea YA TERMINADA por solicitud, en el mismo orden en que se pidieron — nunca en el orden en que
    /// respondió el proveedor. Esperar cada una da el mismo resultado que ResolverAsync para esa
    /// solicitud, o relanza su misma falla (una cancelación incluida): el que llama decide qué hacer con
    /// la falla de cada pedido en su turno, igual que cuando las pedía de a una.
    ///
    /// Dos solicitudes iguales comparten una sola consulta. Acá adentro no se toca la base: lo único que
    /// corre en paralelo es la llamada al proveedor (HttpClient e IMemoryCache admiten concurrencia; un
    /// DbContext no).
    /// </summary>
    public async Task<IReadOnlyList<Task<DistanciaResuelta?>>> ResolverLoteAsync(
        IReadOnlyList<SolicitudDistancia> solicitudes, CancellationToken ct = default)
    {
        using var cupo = new SemaphoreSlim(MaxConsultasALaVez);
        var porSolicitud = new Dictionary<SolicitudDistancia, Task<DistanciaResuelta?>>();
        foreach (var solicitud in solicitudes)
        {
            if (!porSolicitud.ContainsKey(solicitud))
                porSolicitud[solicitud] = ConCupoAsync(solicitud);
        }

        try
        {
            await Task.WhenAll(porSolicitud.Values);
        }
        catch
        {
            // La falla queda guardada en la tarea de su solicitud; se relanza cuando el que llama la espera.
        }
        return solicitudes.Select(s => porSolicitud[s]).ToList();

        async Task<DistanciaResuelta?> ConCupoAsync(SolicitudDistancia s)
        {
            await cupo.WaitAsync(ct);
            try
            {
                return await ResolverAsync(s.LatOrigen, s.LngOrigen, s.LatDestino, s.LngDestino, s.KmManual, ct);
            }
            finally
            {
                cupo.Release();
            }
        }
    }
}
