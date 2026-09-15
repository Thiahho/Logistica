using System.Globalization;

namespace Logistica.Servicios;

public record MensajeAviso(string Asunto, string Cuerpo);

/// <summary>
/// Texto plano — el mismo cuerpo sirve para el email (Servicios/EmailService.cs) y para el link
/// de WhatsApp (Dominio/EnlaceWhatsApp.cs); evita mantener una plantilla HTML aparte para un
/// mensaje de una sola idea.
///
/// Cultura es-AR explícita: Program.cs fija InvariantCulture para TODO el proceso (evita el bug
/// de "," vs "." en el parseo de formularios), pero un mail en castellano con un monto en formato
/// US ("1,234.56") se lee mal. Acá se pisa a propósito, solo para lo que el cliente lee — mismo
/// criterio de formato que ya usa el frontend (`$${n.toLocaleString("es-AR")}`).
///
/// El texto no amenaza más de lo que el sistema hace de verdad: el corte automático (§10.2-L1)
/// solo bloquea ALTAS de pedido nuevas, nunca pedidos ya confirmados o en ruta.
/// </summary>
public static class PlantillasAviso
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    private static string Moneda(decimal monto) => "$" + monto.ToString("N2", Es);

    public static MensajeAviso DeudaVencida(string razonSocial, decimal monto, bool servicioCortado)
    {
        var corte = servicioCortado
            ? " Mientras la deuda vencida siga impaga, el sistema no admite pedidos nuevos; los pedidos ya confirmados o en ruta no se ven afectados."
            : "";
        return new MensajeAviso(
            $"Comprobante vencido — {razonSocial}",
            $"Hola {razonSocial}: figuran comprobantes vencidos por un total de {Moneda(monto)}.{corte} " +
            "Si ya lo abonaste, avisanos para registrarlo. — Logística");
    }

    public static MensajeAviso ProximoAVencer(string razonSocial, decimal monto, DateOnly vencimiento, int dias)
    {
        return new MensajeAviso(
            $"Tu factura vence el {vencimiento.ToString("dd/MM", Es)}",
            $"Hola {razonSocial}: te recordamos que tu factura por {Moneda(monto)} vence el " +
            $"{vencimiento.ToString("dd/MM/yyyy", Es)} (en {dias} día{(dias == 1 ? "" : "s")}). " +
            "Al vencer sin pago, el sistema deja de admitir pedidos nuevos hasta regularizar. — Logística");
    }
}
