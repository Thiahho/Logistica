using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>
/// Máquina de estados del pedido (construccion_v1.md §5). Se declara una sola vez acá; el
/// frontend (lib/dominio/estados.ts) es un espejo de solo lectura para decidir qué botones
/// mostrar — la fuente de verdad es esta, porque el backend es quien tiene que imponerla
/// (no hay lógica de negocio confiable del lado del navegador).
///
///   Borrador     → Cancelado
///   Confirmado   → EnRuta, Cancelado
///   EnRuta       → Entregado, Fallido
///   Fallido      → Reprogramado, Devuelto
///   Reprogramado → Confirmado
///
/// Borrador → Confirmado NO está en esta lista a propósito (acta changelog 3.11): confirmar es
/// cotizar, y eso exige conocer el tipo de vehículo real, que solo se sabe cuando
/// RutasController.CerrarPlanificacion cierra la planificación de la ruta que lleva el pedido —
/// esa es la ÚNICA vía a Confirmado desde Borrador, nunca esta transición genérica. Permitirla acá
/// dejaría confirmar (y por lo tanto rutear) un pedido con precio_base/total todavía en null.
/// </summary>
public static class TransicionesPedido
{
    private static readonly Dictionary<EstadoPedido, EstadoPedido[]> Permitidas = new()
    {
        [EstadoPedido.Borrador] = [EstadoPedido.Cancelado],
        [EstadoPedido.Confirmado] = [EstadoPedido.EnRuta, EstadoPedido.Cancelado],
        [EstadoPedido.EnRuta] = [EstadoPedido.Entregado, EstadoPedido.Fallido],
        [EstadoPedido.Fallido] = [EstadoPedido.Reprogramado, EstadoPedido.Devuelto],
        [EstadoPedido.Reprogramado] = [EstadoPedido.Confirmado],
    };

    // Fallido y Cancelado: sin motivo, la razón de la interrupción se pierde para siempre
    // (P4 — registrar es gratis, reconstruir es imposible).
    private static readonly HashSet<EstadoPedido> ConMotivoObligatorio =
        [EstadoPedido.Fallido, EstadoPedido.Cancelado];

    public static bool Permitida(EstadoPedido actual, EstadoPedido nuevo) =>
        Permitidas.TryGetValue(actual, out var siguientes) && siguientes.Contains(nuevo);

    public static bool MotivoObligatorio(EstadoPedido nuevo) => ConMotivoObligatorio.Contains(nuevo);
}
