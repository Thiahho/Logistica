using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>
/// Máquina de estados del pedido (construccion_v1.md §5). Se declara una sola vez acá; el
/// frontend (lib/dominio/estados.ts) es un espejo de solo lectura para decidir qué botones
/// mostrar — la fuente de verdad es esta, porque el backend es quien tiene que imponerla
/// (no hay lógica de negocio confiable del lado del navegador).
///
///   Borrador     → Confirmado, Cancelado
///   Confirmado   → EnRuta, Cancelado
///   EnRuta       → Entregado, Fallido
///   Fallido      → Reprogramado, Devuelto
///   Reprogramado → Confirmado
/// </summary>
public static class TransicionesPedido
{
    private static readonly Dictionary<EstadoPedido, EstadoPedido[]> Permitidas = new()
    {
        [EstadoPedido.Borrador] = [EstadoPedido.Confirmado, EstadoPedido.Cancelado],
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
