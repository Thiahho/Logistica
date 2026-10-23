namespace Logistica.Entidades;

public enum EstadoPedido
{
    Borrador,
    Confirmado,
    EnRuta,
    Entregado,
    Fallido,
    Reprogramado,
    Devuelto,
    Cancelado,
    // La empresa fue dada de baja por falta de pago: el pedido salió de su ruta y espera a que la
    // operación lo reanude o lo devuelva (Servicios/RetencionPedidosService.cs). Va al final: es el
    // orden en que quedó en el tipo estado_pedido de Postgres (alter type ... add value).
    Retenido,
}
