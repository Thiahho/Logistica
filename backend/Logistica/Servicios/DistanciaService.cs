using Logistica.Dominio;
using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>Fuente: "ruta" | "recta" | "manual" — trazabilidad de con qué número se cobró
/// (pedidos.km_fuente), igual criterio que geo_proveedor en Ubicacion.</summary>
public record DistanciaResuelta(decimal Km, string Fuente);

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
}
