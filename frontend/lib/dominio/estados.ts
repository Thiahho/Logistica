import type { EstadoPedido } from "./tipos";

/**
 * Espejo de solo lectura de Dominio/TransicionesPedido.cs (backend/Logistica). La fuente de
 * verdad es el servidor — esto solo decide qué botones mostrar en /pedidos/[id]; el backend
 * valida la transición igual, un intento inválido devuelve 400 sin importar qué muestre acá.
 */
// Borrador -> Confirmado no está acá a propósito (acta changelog 3.11): confirmar es cotizar,
// y eso exige conocer el tipo de vehículo real — solo pasa cuando la ruta que lleva el pedido
// cierra su planificación (RutasController.CerrarPlanificacion), nunca a mano.
// EnRuta -> Cancelado (E1, §10.2-I): CerrarPlanificacion pasa cada pedido de Confirmado a EnRuta
// en la misma escritura, así que "Confirmado" nunca queda observable para cancelarlo ahí —
// EnRuta es el primer estado externamente alcanzable con precio ya congelado, y cancelarlo desde
// ahí cuesta el 100% del servicio (ver CuentaCorrienteService.AgregarItemDePedido).
const TRANSICIONES: Record<EstadoPedido, EstadoPedido[]> = {
  Borrador: ["Cancelado"],
  Confirmado: ["EnRuta", "Cancelado"],
  EnRuta: ["Entregado", "Fallido", "Cancelado"],
  Entregado: [],
  Fallido: ["Reprogramado", "Devuelto"],
  Reprogramado: ["Confirmado"],
  Devuelto: [],
  Cancelado: [],
};

// Fallido y Cancelado: sin motivo la razón de la interrupción se pierde para siempre.
const CON_MOTIVO_OBLIGATORIO = new Set<EstadoPedido>(["Fallido", "Cancelado"]);

export function transicionesDisponibles(estado: EstadoPedido): EstadoPedido[] {
  return TRANSICIONES[estado];
}

export function motivoObligatorio(estadoNuevo: EstadoPedido): boolean {
  return CON_MOTIVO_OBLIGATORIO.has(estadoNuevo);
}

export const ETIQUETA_TRANSICION: Record<EstadoPedido, string> = {
  Borrador: "Volver a borrador",
  Confirmado: "Confirmar",
  EnRuta: "Marcar en ruta",
  Entregado: "Marcar entregado",
  Fallido: "Marcar fallido",
  Reprogramado: "Reprogramar",
  Devuelto: "Marcar devuelto",
  Cancelado: "Cancelar",
};
