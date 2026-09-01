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
    public async Task FijarAsync(int? clienteId, int zonaId, string tipoVehiculo, decimal? precio, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
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
