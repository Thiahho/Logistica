using Logistica.Datos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Calienta el servidor apenas arranca, en segundo plano (no demora el inicio): ejecuta una consulta
/// mínima por cada consulta "pesada" de la app para que EF Core compile sus árboles de expresión, el
/// JIT compile los caminos calientes y el pool de conexiones abra sus primeras conexiones. Sin esto el
/// PRIMER usuario en abrir cada pantalla pagaba entre 0,2 y 0,9 s extra (medido con 100.000 pedidos:
/// jornada de ruta 901 ms en frío contra 3 ms en caliente; mis-paradas/dia 300 ms contra 6 ms). Nunca
/// falla el arranque: cualquier error se anota y se sigue.
/// </summary>
public class CalentamientoService(IServiceScopeFactory scopes, ILogger<CalentamientoService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LogisticaDbContext>();

            await db.Pedidos.AsNoTracking().OrderByDescending(p => p.Id)
                .Select(p => new { p.Id, p.Bultos, Cliente = p.Cliente.RazonSocial, Destino = p.DestinoUbicacion.CalleNumero, LocalidadNombre = p.DestinoUbicacion.Localidad!.Nombre })
                .Take(1).ToListAsync(ct);
            await db.Pedidos.AsNoTracking().Where(p => EF.Functions.ILike(p.DestinatarioNombre, "%x%")).Take(1).Select(p => p.Id).ToListAsync(ct);
            await db.Rutas.AsNoTracking().OrderByDescending(r => r.Id)
                .Select(r => new { r.Id, Paradas = db.RutaParadas.Count(p => p.RutaId == r.Id), Bultos = db.ParadaPedidos.Where(pp => pp.Parada.RutaId == r.Id).Sum(pp => (int?)pp.Pedido.Bultos) })
                .Take(1).ToListAsync(ct);
            await db.RutaParadas.AsNoTracking().Include(p => p.Ubicacion).Take(1).ToListAsync(ct);
            await db.PedidoEventos.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Facturas.AsNoTracking().Take(1).ToListAsync(ct);
            await db.FacturaItems.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Clientes.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Localidades.AsNoTracking().Include(l => l.Zona).Take(1).ToListAsync(ct);
            await db.Tarifas.AsNoTracking().Take(1).ToListAsync(ct);
            await db.PruebasEntrega.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Novedades.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Usuarios.AsNoTracking().Take(1).ToListAsync(ct);

            log.LogInformation("Calentamiento listo en {Ms} ms", reloj.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Calentamiento omitido: no bloquea el arranque");
        }
    }
}
