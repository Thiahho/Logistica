using Logistica.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Logistica.Web;

/// <summary>
/// /health/listo (changelog 1.42): el backend no migra al arrancar (auditoria_seguridad.md hallazgo 17),
/// así que un deploy con migraciones sin aplicar arranca "sano" pero rompe en cuanto toca una columna
/// nueva. Este chequeo lo deja "no listo" hasta que se migre. No expone qué migraciones faltan: solo cuántas.
/// </summary>
public class ChequeoMigraciones(LogisticaDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var pendientes = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
        return pendientes == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{pendientes} migración(es) sin aplicar");
    }
}
