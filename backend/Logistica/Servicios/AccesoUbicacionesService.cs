using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// M-N1: qué ubicaciones del catálogo compartido puede usar y mover un cliente del portal. Estático sobre
/// el contexto (como ObservacionesFrecuentes): MiCuentaController no suma dependencias.
/// </summary>
public static class AccesoUbicacionesService
{
    /// <summary>Las que el cliente resolvió desde el portal, o son destino de un envío o de un contacto
    /// suyo. Un id fuera de acá se responde igual que uno inexistente.</summary>
    public static IQueryable<Ubicacion> Alcanzables(LogisticaDbContext db, int clienteId) =>
        db.Ubicaciones.Where(u =>
            db.ClientesUbicaciones.Any(a => a.ClienteId == clienteId && a.UbicacionId == u.Id)
            || db.Pedidos.Any(p => p.ClienteId == clienteId && p.DestinoUbicacionId == u.Id)
            || db.ClientesDestinatarios.Any(d => d.ClienteId == clienteId && d.DestinoUbicacionId == u.Id));

    /// <summary>Registra que el cliente llegó a esta ubicación. Idempotente.</summary>
    public static Task RegistrarAsync(LogisticaDbContext db, int clienteId, long ubicacionId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"insert into clientes_ubicaciones (cliente_id, ubicacion_id) values ({clienteId}, {ubicacionId}) on conflict do nothing", ct);

    /// <summary>Si el portal de este cliente puede mover el punto: sin verificar, no es depósito ni origen
    /// de una ruta o viaje, y todo envío, contacto, acceso o parada de ruta que la usa es del mismo cliente.</summary>
    public static async Task<bool> PuedeMoverAsync(LogisticaDbContext db, Ubicacion u, int clienteId, CancellationToken ct)
    {
        if (u.Verificada || u.NombreDeposito is not null) return false;
        var id = u.Id;
        return !await db.Rutas.AnyAsync(r => r.OrigenUbicacionId == id, ct)
            && !await db.Viajes.AnyAsync(v => v.OrigenUbicacionId == id, ct)
            && !await db.Pedidos.AnyAsync(
                p => (p.DestinoUbicacionId == id || p.OrigenUbicacionId == id) && p.ClienteId != clienteId, ct)
            && !await db.ClientesDestinatarios.AnyAsync(d => d.DestinoUbicacionId == id && d.ClienteId != clienteId, ct)
            && !await db.ClientesUbicaciones.AnyAsync(a => a.UbicacionId == id && a.ClienteId != clienteId, ct)
            // Una parada sin envíos (p. ej. un retiro) o con envíos de otro cliente tampoco es del cliente.
            && !await db.RutaParadas.AnyAsync(rp => rp.UbicacionId == id
                && (!db.ParadaPedidos.Any(pp => pp.ParadaId == rp.Id)
                    || db.ParadaPedidos.Any(pp => pp.ParadaId == rp.Id && pp.Pedido.ClienteId != clienteId)), ct);
    }
}
