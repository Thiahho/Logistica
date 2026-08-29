using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Web;

/// <summary>
/// construccion_v1.md §3 regla 3: "Si el trigger lo impide, la app muestra el error, no lo
/// previene por su cuenta." Sin esto, una excepción de trg_congelar_pedido o
/// trg_bloquear_direccion_dudosa sale como 500 con stack trace en vez de un mensaje accionable.
/// Traduce las excepciones que vienen de reglas de negocio en la base (triggers con RAISE
/// EXCEPTION, checks, unique) a ProblemDetails.
/// </summary>
public class ManejadorExcepciones(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, detalle) = Clasificar(exception);
        if (status is null) return false;

        httpContext.Response.StatusCode = status.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status.Value,
                Title = status == StatusCodes.Status409Conflict
                    ? "Conflicto con una regla del sistema"
                    : "Solicitud inválida",
                Detail = detalle,
            },
        });
    }

    private static (int? Status, string? Detalle) Clasificar(Exception exception)
    {
        // trg_congelar_pedido, trg_bloquear_direccion_dudosa, trg_log_inmutable: RAISE EXCEPTION
        // sin código propio queda en SqlState P0001 (raise_exception), con el mensaje del RAISE
        // como MessageText.
        if (BuscarPostgresException(exception) is { } pg)
        {
            return pg.SqlState switch
            {
                "P0001" or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation
                    or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.NotNullViolation
                    => (StatusCodes.Status409Conflict, pg.MessageText),
                _ => (null, null),
            };
        }

        // PrecioService.CotizarAsync: "No hay tarifa vigente para la zona X."
        if (exception is InvalidOperationException invalidOp)
            return (StatusCodes.Status400BadRequest, invalidOp.Message);

        return (null, null);
    }

    private static PostgresException? BuscarPostgresException(Exception exception) => exception switch
    {
        PostgresException pg => pg,
        DbUpdateException { InnerException: PostgresException pg } => pg,
        { InnerException: not null } => BuscarPostgresException(exception.InnerException),
        _ => null,
    };
}
