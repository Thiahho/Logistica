using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Logistica.Web;

/// <summary>
/// Topes de paginación. Antes `tamanioPagina` no tenía techo (y sin él se devolvía la tabla entera): un
/// `GET /api/pedidos?tamanioPagina=1000000` traía ~27 MB y tardaba más de un segundo con 100.000 pedidos
/// — con varios en paralelo, un DoS al alcance de cualquier usuario autenticado. Con 100 usuarios
/// pidiéndolo a la vez el servidor bajaba a 3 respuestas por segundo.
/// </summary>
public static class Paginacion
{
    /// <summary>Lo más que se pide por página (las pantallas usan 10 a 20).</summary>
    public const int TamanioMaximo = 100;

    /// <summary>Lo más que se devuelve cuando el llamador no pagina (compatibilidad con consumidores
    /// que no mandan `tamanioPagina`): un techo de seguridad, no un tamaño de página.</summary>
    public const int TopeSinPaginar = 500;

    public static int TamanioEfectivo(int? tamanioPagina) =>
        tamanioPagina is > 0 ? Math.Min(tamanioPagina.Value, TamanioMaximo) : TopeSinPaginar;
}

/// <summary>
/// Filtro global: ningún campo de texto de un body puede ser gigante. Sin esto la carga de un pedido
/// aceptaba un nombre de 100.000 caracteres, un teléfono de 5.000 o unas observaciones de 200.000 (y los
/// guardaba). Es la red de seguridad general; los formularios principales además llevan sus propios
/// límites y mensajes (StringLength / Range en el DTO).
/// </summary>
public class FiltroLimitesDeTexto : IActionFilter
{
    private const int LimiteGeneral = 500;
    private const int LimiteTextoLibre = 2000;
    private const int LimiteUrl = 2048;
    private const int LimiteClave = 200;

    /// <summary>Campos de texto libre: pueden ser más largos que un nombre o una dirección.</summary>
    private static readonly HashSet<string> TextoLibre = new(StringComparer.OrdinalIgnoreCase)
    {
        "Observaciones", "Descripcion", "Nota", "Notas", "NotasCierre", "Motivo", "Resolucion",
        "SinDocumentoMotivo", "MotivoFallo", "Referencia", "ValorNuevo", "Mensaje",
    };

    public void OnActionExecuting(ActionExecutingContext contexto)
    {
        foreach (var argumento in contexto.ActionArguments.Values)
        {
            var error = Revisar(argumento, 0);
            if (error is null) continue;

            contexto.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Solicitud inválida",
                Detail = error,
            });
            return;
        }
    }

    public void OnActionExecuted(ActionExecutedContext contexto) { }

    private static string? Revisar(object? valor, int profundidad)
    {
        if (valor is null || profundidad > 3 || valor is string || valor.GetType().IsPrimitive || valor is IFormFile)
            return null;

        if (valor is IEnumerable coleccion)
        {
            var n = 0;
            foreach (var item in coleccion)
            {
                if (++n > 5000) return "La lista es demasiado larga.";
                var e = Revisar(item, profundidad + 1);
                if (e is not null) return e;
            }
            return null;
        }

        var tipo = valor.GetType();
        if (tipo.Namespace?.StartsWith("System") == true) return null;

        foreach (var propiedad in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (propiedad.GetIndexParameters().Length > 0) continue;
            object? contenido;
            try { contenido = propiedad.GetValue(valor); } catch { continue; }

            if (propiedad.PropertyType == typeof(string) && contenido is string texto)
            {
                var limite = LimiteDe(propiedad.Name);
                if (texto.Length > limite)
                    return $"El campo «{propiedad.Name}» es demasiado largo (máximo {limite} caracteres).";
            }
            else if (contenido is not null && propiedad.PropertyType != typeof(string))
            {
                var e = Revisar(contenido, profundidad + 1);
                if (e is not null) return e;
            }
        }
        return null;
    }

    private static int LimiteDe(string nombre) =>
        TextoLibre.Contains(nombre) ? LimiteTextoLibre
        : nombre.Contains("Url", StringComparison.OrdinalIgnoreCase) ? LimiteUrl
        : nombre.Contains("Password", StringComparison.OrdinalIgnoreCase) ? LimiteClave
        : LimiteGeneral;
}

/// <summary>Validaciones de fecha de entrega compartidas por las altas de pedido.</summary>
public static class ValidacionFechas
{
    /// <summary>null si es válida; si no, el mensaje. `diasAtras`/`diasAdelante` acotan la ventana
    /// respecto de hoy: una fecha en el año 9999 (o en el 2000) era aceptada y quedaba en la base.</summary>
    public static string? FechaEntrega(DateOnly fecha, DateOnly hoy, int diasAtras, int diasAdelante)
    {
        if (fecha < hoy.AddDays(-diasAtras))
            return diasAtras == 0
                ? "La fecha de entrega no puede ser anterior a hoy."
                : $"La fecha de entrega no puede ser anterior a {diasAtras} días.";
        if (fecha > hoy.AddDays(diasAdelante))
            return $"La fecha de entrega no puede superar los {diasAdelante} días desde hoy.";
        return null;
    }
}
