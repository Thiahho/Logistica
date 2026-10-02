using System.Globalization;
using System.Text;
using Logistica.Entidades;

namespace Logistica.Servicios;

/// <summary>El último estado al que llegó un envío dentro del período del resumen.</summary>
public record MovimientoEnvio(long PedidoId, string Destinatario, string Direccion, EstadoPedido Estado, string? Motivo);

/// <summary>
/// Texto del resumen diario de envíos que recibe el cliente (B6). Texto plano, como el resto de
/// PlantillasAviso. Solo datos del propio cliente: sus envíos, sus destinatarios y el motivo de una
/// entrega fallida — ni precios (los clientes del portal son suscriptores, acta changelog 4.30) ni
/// nombres del personal (acta changelog 4.10). Lógica pura: se prueba sola en Logistica.Tests.
/// </summary>
public static class ResumenEnvios
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Un cliente con cientos de envíos no recibe un correo de cientos de renglones.</summary>
    public const int MaxRenglonesPorSeccion = 40;

    private static readonly (EstadoPedido Estado, string Titulo, bool ConMotivo)[] Secciones =
    [
        (EstadoPedido.Entregado, "Entregados", false),
        (EstadoPedido.Fallido, "No se pudieron entregar", true),
        (EstadoPedido.EnRuta, "Siguen en reparto", false),
        (EstadoPedido.Reprogramado, "Reprogramados", false),
        (EstadoPedido.Devuelto, "Devueltos", false),
        (EstadoPedido.Cancelado, "Cancelados", true),
    ];

    /// <summary>Los estados que se informan. Borrador y Confirmado son pasos internos de la carga y la
    /// planificación: no le dicen nada al cliente.</summary>
    public static readonly EstadoPedido[] Informables = Secciones.Select(s => s.Estado).ToArray();

    /// <summary>null si no hubo movimientos: no se manda un correo para decir que no pasó nada.</summary>
    public static MensajeAviso? Armar(string razonSocial, DateOnly dia, IReadOnlyCollection<MovimientoEnvio> movimientos)
    {
        if (movimientos.Count == 0) return null;

        var fecha = dia.ToString("dd/MM", Es);
        var cuerpo = new StringBuilder($"Hola {razonSocial}: este es el resumen de tus envíos del {fecha}.\n");
        foreach (var (estado, titulo, conMotivo) in Secciones)
        {
            var delEstado = movimientos.Where(m => m.Estado == estado).OrderBy(m => m.PedidoId).ToList();
            if (delEstado.Count == 0) continue;

            cuerpo.Append($"\n{titulo} ({delEstado.Count}):\n");
            foreach (var m in delEstado.Take(MaxRenglonesPorSeccion))
            {
                cuerpo.Append($"- #{m.PedidoId} · {m.Destinatario} · {m.Direccion}");
                if (conMotivo && !string.IsNullOrWhiteSpace(m.Motivo)) cuerpo.Append($" — {Legible(m.Motivo)}");
                cuerpo.Append('\n');
            }
            if (delEstado.Count > MaxRenglonesPorSeccion)
                cuerpo.Append($"… y {delEstado.Count - MaxRenglonesPorSeccion} más.\n");
        }
        cuerpo.Append("\n— Logística");

        var entregados = movimientos.Count(m => m.Estado == EstadoPedido.Entregado);
        var fallidos = movimientos.Count(m => m.Estado == EstadoPedido.Fallido);
        var partes = new List<string>();
        if (entregados > 0) partes.Add($"{entregados} entregado{(entregados == 1 ? "" : "s")}");
        if (fallidos > 0) partes.Add($"{fallidos} sin entregar");
        var asunto = partes.Count > 0
            ? $"Tus envíos del {fecha}: {string.Join(", ", partes)}"
            : $"Resumen de tus envíos del {fecha}";

        return new MensajeAviso(asunto, cuerpo.ToString());
    }

    /// <summary>Los motivos de fallo son códigos de una lista cerrada ("destinatario_ausente").</summary>
    private static string Legible(string motivo) => motivo.Trim().Replace('_', ' ');
}
