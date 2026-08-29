using Microsoft.EntityFrameworkCore;

namespace Logistica.Datos;

/// <summary>
/// Único camino de escritura para operaciones de dominio. Publica el actor (y el motivo,
/// cuando aplica) como GUC de Postgres dentro de la misma transacción antes de guardar, para
/// que fn_log_estado_pedido sepa quién actuó (no hay auth.uid(): no hay Supabase).
///
/// set_config(..., is_local: true) equivale a SET LOCAL: vive solo dentro de la transacción
/// actual y se descarta al confirmar o revertir, aunque Npgsql reutilice la conexión del pool
/// para otro usuario en la siguiente request.
/// </summary>
public static class EscrituraDominio
{
    public static async Task<int> GuardarComoAsync(
        this LogisticaDbContext db,
        Guid? usuarioId,
        string? motivo = null,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select set_config('app.usuario_id', {usuarioId.ToString()}, true)", ct);

        if (motivo is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"select set_config('app.motivo', {motivo}, true)", ct);
        }

        var filas = await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return filas;
    }
}
