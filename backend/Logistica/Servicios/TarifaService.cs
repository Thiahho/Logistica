using Logistica.Dominio;
using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Fija o borra una tarifa (RF-09) sin pisar el historial: si ya hay una vigente de hoy la
/// actualiza; si es de un día anterior la cierra y abre una nueva. precio=null cierra la
/// vigente. clienteId=null es la lista general.
///
/// Extraído de ClientesController.FijarTarifa (tarifas por cliente) para reusarlo también desde
/// TarifasController (lista general): es la misma regla de vigencia en los dos casos.
/// </summary>
public class TarifaService(LogisticaDbContext db)
{
    private record PrecioZonaTipo(int ZonaId, string TipoVehiculo, decimal? Precio);

    /// <summary>
    /// Precio general (sin cliente) vigente a `fecha`, para TODAS las zonas y ambos tipos de
    /// vehículo, en una sola consulta. Antes esto eran 2 round-trips (uno por tipo de vehículo)
    /// por cada zona recorrida en un foreach — con Postgres local es invisible, pero contra un
    /// Postgres gestionado cada round-trip cuesta 50-200x más (latencia de red en vez de CPU
    /// local), así que ClientesController.Detalle y TarifasController.Listar lo notaban.
    ///
    /// El resultado siempre trae una fila por (zona, tipo), aunque tarifa_vigente() devuelva
    /// null — mismo comportamiento que el SingleAsync() original sobre un solo
    /// "select tarifa_vigente(...)" (siempre 1 fila, valor null o no).
    /// </summary>
    public async Task<Dictionary<(int ZonaId, string TipoVehiculo), decimal?>> PreciosGeneralesAsync(
        DateOnly fecha, CancellationToken ct = default)
    {
        var filas = await db.Database
            .SqlQuery<PrecioZonaTipo>($"""
                select z.id as "ZonaId", tv as "TipoVehiculo", tarifa_vigente(null, z.id, {fecha}, tv) as "Precio"
                from zonas z, unnest(array['camioneta', 'moto']) as tv
                """)
            .ToListAsync(ct);
        return filas.ToDictionary(f => (f.ZonaId, f.TipoVehiculo), f => f.Precio);
    }

    public async Task FijarAsync(int? clienteId, int zonaId, string tipoVehiculo, decimal? precio, CancellationToken ct = default)
    {
        var hoy = Reloj.HoyLocal();
        var vigente = await db.Tarifas
            .Where(t => t.ClienteId == clienteId && t.ZonaId == zonaId && t.TipoVehiculo == tipoVehiculo && t.VigenteHasta == null)
            .SingleOrDefaultAsync(ct);

        if (precio is null)
        {
            if (vigente is not null)
            {
                // ck_tarifas_vigencia exige vigente_hasta >= vigente_desde: una fila abierta hoy
                // no se puede "cerrar ayer", se borra directamente en vez de dejar un rango inválido.
                if (vigente.VigenteDesde == hoy) db.Tarifas.Remove(vigente);
                else vigente.VigenteHasta = hoy.AddDays(-1);
            }
            await db.SaveChangesAsync(ct);
            return;
        }

        if (vigente is not null && vigente.VigenteDesde == hoy)
        {
            vigente.Precio = precio.Value;
        }
        else
        {
            if (vigente is not null) vigente.VigenteHasta = hoy.AddDays(-1);
            db.Tarifas.Add(new Tarifa { ClienteId = clienteId, ZonaId = zonaId, TipoVehiculo = tipoVehiculo, Precio = precio.Value, VigenteDesde = hoy });
        }

        await db.SaveChangesAsync(ct);
    }
}
