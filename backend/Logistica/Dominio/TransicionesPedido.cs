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
///   EnRuta       → Entregado, Fallido, Cancelado
///   Fallido      → Reprogramado, Devuelto
///   Reprogramado → Confirmado
///   Retenido     → Reprogramado, Borrador, Devuelto
///
/// A Retenido NO se llega por esta lista: es la baja de la empresa por falta de pago la que retiene
/// sus pedidos pendientes (Servicios/RetencionPedidosService.cs), nunca un cambio de estado pedido a
/// mano. De Retenido se sale reanudando — a Reprogramado o a Borrador según ReanudaEn, y solo con la
/// empresa otra vez activa — o marcándolo Devuelto, que ahí es terminal y no genera el pedido de
/// retorno. Retenido → Cancelado tampoco está, a propósito: cancelar un pedido ya confirmado factura
/// el 100% (§10.2-I) y retener no cobra nada; quien quiera cancelarlo lo reanuda primero y lo
/// cancela con la regla de siempre.
///
/// Borrador → Confirmado NO está en esta lista a propósito (acta changelog 3.11): confirmar es
/// cotizar, y eso exige conocer el tipo de vehículo real, que solo se sabe cuando
/// RutasController.CerrarPlanificacion cierra la planificación de la ruta que lleva el pedido —
/// esa es la ÚNICA vía a Confirmado desde Borrador, nunca esta transición genérica. Permitirla acá
/// dejaría confirmar (y por lo tanto rutear) un pedido con precio_base/total todavía en null.
///
/// EnRuta → Cancelado (E1, §10.2-I): CerrarPlanificacion pasa cada pedido de Confirmado a EnRuta
/// en la misma escritura (RutasController.cs) — un pedido nunca queda observable "solo
/// Confirmado" para que alguien lo cancele ahí. El estado real y externamente alcanzable con
/// precio ya congelado es EnRuta, así que la regla de negocio ("cancelar un pedido ya confirmado
/// cuesta el 100%") tiene que poder aplicarse desde ahí — si no, cancelar un pedido en camino
/// simplemente no tendría ningún botón que lo permita.
/// </summary>
public static class TransicionesPedido
{
    private static readonly Dictionary<EstadoPedido, EstadoPedido[]> Permitidas = new()
    {
        [EstadoPedido.Borrador] = [EstadoPedido.Cancelado],
        [EstadoPedido.Confirmado] = [EstadoPedido.EnRuta, EstadoPedido.Cancelado],
        [EstadoPedido.EnRuta] = [EstadoPedido.Entregado, EstadoPedido.Fallido, EstadoPedido.Cancelado],
        [EstadoPedido.Fallido] = [EstadoPedido.Reprogramado, EstadoPedido.Devuelto],
        [EstadoPedido.Reprogramado] = [EstadoPedido.Confirmado],
        [EstadoPedido.Retenido] = [EstadoPedido.Reprogramado, EstadoPedido.Borrador, EstadoPedido.Devuelto],
    };

    // Fallido y Cancelado: sin motivo, la razón de la interrupción se pierde para siempre
    // (P4 — registrar es gratis, reconstruir es imposible).
    private static readonly HashSet<EstadoPedido> ConMotivoObligatorio =
        [EstadoPedido.Fallido, EstadoPedido.Cancelado];

    /// <summary>Los estados de un pedido todavía pendiente: los que pasan a Retenido cuando la empresa
    /// se da de baja por falta de pago (Servicios/RetencionPedidosService.cs), y a los que ningún pedido
    /// de una empresa inactiva puede volver.</summary>
    public static readonly EstadoPedido[] Retenibles =
        [EstadoPedido.Borrador, EstadoPedido.Confirmado, EstadoPedido.EnRuta, EstadoPedido.Reprogramado];

    public static bool Permitida(EstadoPedido actual, EstadoPedido nuevo) =>
        Permitidas.TryGetValue(actual, out var siguientes) && siguientes.Contains(nuevo);

    public static bool MotivoObligatorio(EstadoPedido nuevo) => ConMotivoObligatorio.Contains(nuevo);

    /// <summary>A qué estado se reanuda un Retenido, según el estado en que estaba cuando se retuvo (el
    /// estado_anterior de ese evento en pedido_eventos). Solo lo que estaba en Borrador nunca se
    /// confirmó y vuelve a Borrador, a cotizarse cuando se rutee. Todo lo demás ya tenía precio
    /// congelado (P1) y vuelve como Reprogramado: conserva ese precio y sale en una ruta nueva.</summary>
    public static EstadoPedido ReanudaEn(EstadoPedido estadoAlRetener) =>
        estadoAlRetener == EstadoPedido.Borrador ? EstadoPedido.Borrador : EstadoPedido.Reprogramado;
}
