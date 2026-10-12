using Logistica.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logistica.Tests;

public class ManejadorExcepcionesTests
{
    /// <summary>Se queda con el ProblemDetails en vez de escribirlo en la respuesta.</summary>
    private sealed class Captura : IProblemDetailsService
    {
        public ProblemDetailsContext? Contexto { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Contexto = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Contexto = context;
            return ValueTask.FromResult(true);
        }
    }

    private static async Task<(int Status, string? Detalle)> ManejarAsync(Exception excepcion)
    {
        var captura = new Captura();
        var http = new DefaultHttpContext();
        var manejador = new ManejadorExcepciones(captura, NullLogger<ManejadorExcepciones>.Instance);

        Assert.True(await manejador.TryHandleAsync(http, excepcion, CancellationToken.None));

        return (http.Response.StatusCode, captura.Contexto!.ProblemDetails.Detail);
    }

    // Pedido.Version: dos escrituras sobre el mismo pedido. Es un conflicto que el usuario resuelve
    // recargando, no un error del servidor.
    [Fact]
    public async Task Un_choque_de_concurrencia_optimista_es_un_409_que_pide_recargar()
    {
        var (status, detalle) = await ManejarAsync(new DbUpdateConcurrencyException("0 filas afectadas"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("volvé a cargarlo", detalle);
    }

    [Fact]
    public async Task Una_excepcion_desconocida_sigue_siendo_un_500_sin_detalle_interno()
    {
        var (status, detalle) = await ManejarAsync(new NotSupportedException("detalle interno"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("detalle interno", detalle);
    }
}
