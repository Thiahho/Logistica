using Logistica.Datos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Estado: el del pedido (EnRuta, Cancelado...). La PWA lo usa para marcar un pedido que operación
/// canceló con la ruta en curso (acta changelog 4.8). Es un estado, no un importe.</summary>
public record PedidoDeParada(long PedidoId, string DestinatarioNombre, string DestinatarioTelefono, int Bultos, string? Observaciones, string Estado);

public record ParadaDelDia(
    long ParadaId, long RutaId, int Orden, string Tipo, string Estado,
    DateTimeOffset? LlegadaEn, DateTimeOffset? SalidaEn,
    string CalleNumero, string? Localidad, string? Referencia, decimal? Lat, decimal? Lng,
    List<PedidoDeParada> Pedidos);

/// <summary>Bundle de una ruta puntual (no "del día" de un repartidor): mismas piezas que
/// MisParadasController.JornadaDelDia, sin MotivosFallo/UmbralDesvioMetros (config del
/// repartidor, no del back-office) y sin Fecha (ya la tiene RutaDetalle).</summary>
public record JornadaRuta(
    long RutaId, int Total, int Completadas, int Fallidas, int Canceladas,
    OrigenRuta? Origen, Recorrido? Recorrido, List<ParadaDelDia> Paradas);

/// <summary>
/// Núcleo de MisParadasController.Dia (H2, RNF-07) extraído para poder armar el mismo bundle
/// por rutaId en vez de "la ruta en_curso del repartidor autenticado" — es lo único que
/// impedía reusarlo desde el back-office (jornada §9.3, ver RutasController/{id}/jornada y
/// JornadaController). Consulta v_paradas_repartidor, igual que antes: nunca expone importes.
/// </summary>
public class JornadaService(LogisticaDbContext db, OrigenRutaService origenes, RuteoService ruteo)
{
    public async Task<JornadaRuta?> ArmarAsync(long rutaId, CancellationToken ct = default)
    {
        var ruta = await db.Rutas.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rutaId, ct);
        if (ruta is null) return null;

        var origen = await origenes.ResolverAsync(ruta, ct);

        var filas = await (
            from p in db.Set<ParadaRepartidor>()
            where p.RutaId == rutaId
            orderby p.Orden
            select p
        ).ToListAsync(ct);

        var paradas = filas
            .GroupBy(f => f.ParadaId)
            .Select(g =>
            {
                var primero = g.First();
                return new ParadaDelDia(
                    primero.ParadaId, primero.RutaId, primero.Orden, primero.Tipo, primero.Estado,
                    primero.LlegadaEn, primero.SalidaEn,
                    primero.CalleNumero, primero.Localidad, primero.Referencia, primero.Lat, primero.Lng,
                    g.Select(f => new PedidoDeParada(f.PedidoId, f.DestinatarioNombre, f.DestinatarioTelefono, f.Bultos, f.Observaciones, f.PedidoEstado.ToString())).ToList());
            })
            .OrderBy(p => p.Orden)
            .ToList();

        // Mismo criterio que Dia(): traza desde el origen resuelto, en el orden ya planificado,
        // sobre las paradas con coordenada real. Cachea en RuteoService, así que el polling del
        // back-office y el refresh de la PWA pegan en la misma entrada.
        var puntosRuta = new List<PuntoRuta>();
        if (origen is not null && OrigenRutaService.Punto(origen) is { } puntoOrigen) puntosRuta.Add(puntoOrigen);
        puntosRuta.AddRange(paradas.Where(p => p.Lat is not null && p.Lng is not null)
            .Select(p => new PuntoRuta(p.Lat!.Value, p.Lng!.Value)));
        var recorrido = await ruteo.TrazarAsync(puntosRuta, ct);

        return new JornadaRuta(
            rutaId, paradas.Count,
            paradas.Count(p => p.Estado == "completada"), paradas.Count(p => p.Estado == "fallida"),
            paradas.Count(p => p.Estado == "cancelada"),
            origen, recorrido, paradas);
    }
}
