// Hogar único de los tipos que reflejan las entidades del backend. Antes estaban duplicados
// (con formas distintas) en cada página que los usaba — PedidoResumen en pedidos/page.tsx y
// mis-envios/page.tsx, Cliente en clientes/page.tsx y pedidos/nuevo/page.tsx.

export type EstadoPedido =
  | "Borrador"
  | "Confirmado"
  | "EnRuta"
  | "Entregado"
  | "Fallido"
  | "Reprogramado"
  | "Devuelto"
  | "Cancelado";

export const ESTADOS_PEDIDO: EstadoPedido[] = [
  "Borrador",
  "Confirmado",
  "EnRuta",
  "Entregado",
  "Fallido",
  "Reprogramado",
  "Devuelto",
  "Cancelado",
];

export interface PedidoResumen {
  id: number;
  destinatarioNombre: string;
  estado: EstadoPedido;
  total: number;
  fechaEntrega: string;
  clienteId: number;
  direccionDudosa: boolean;
}

export interface HistorialEvento {
  id: number;
  estadoAnterior: EstadoPedido | null;
  estadoNuevo: EstadoPedido;
  motivo: string | null;
  actorTipo: "sistema" | "usuario";
  actorNombre: string | null;
  ocurridoEn: string;
}

export interface PedidoDetalle {
  id: number;
  clienteId: number;
  clienteRazonSocial: string;
  referenciaCliente: string | null;
  tipo: string;
  pedidoOrigenId: number | null;
  destinoCalleNumero: string;
  destinoLocalidad: string | null;
  destinatarioNombre: string;
  destinatarioTelefono: string;
  bultos: number;
  pesoKg: number | null;
  valorDeclarado: number | null;
  fechaEntrega: string;
  urgente: boolean;
  precioBase: number;
  recargoUrgencia: number;
  descuentoRuta: number;
  peajes: number;
  total: number;
  precioCongeladoEn: string;
  estado: EstadoPedido;
  origenCarga: string;
  observaciones: string | null;
  creadoEn: string;
  direccionDudosa: boolean;
  historial: HistorialEvento[];
}

/** Entidad completa, solo para administración (semáforos y datos de contacto). */
export interface Cliente {
  id: number;
  razonSocial: string;
  cuit: string | null;
  contacto: string | null;
  telefono: string | null;
  email: string | null;
  activo: boolean;
  colorPago: string;
  colorTrato: string;
  colorOper: string;
}

/** Para el selector de cliente en el alta de pedido (GET /api/clientes/seleccion): sin
 * semáforos ni tarifas, que son privativos de administración. */
export interface ClienteSeleccion {
  id: number;
  razonSocial: string;
}

export interface Localidad {
  id: number;
  nombre: string;
  partido: string | null;
  zonaId: number | null;
}

export interface Zona {
  id: number;
  codigo: string;
  nombre: string;
  activa: boolean;
}

export interface TarifaZona {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  precioGeneral: number | null;
  precioCliente: number | null;
}

export interface TarifaGeneral {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  precio: number | null;
}

export interface EventoResumen {
  id: number;
  tipoCodigo: string;
  tipoDescripcion: string;
  dimension: string;
  valorNum: number | null;
  nota: string | null;
  ocurridoEn: string;
  registradoPorNombre: string | null;
}

// Nombrado "UsuarioCuenta" (no "Usuario") para no chocar con lib/auth/types.ts, que ya usa
// "Usuario" para la identidad de la sesión activa — son conceptos distintos: esta es la fila de
// la tabla que administra /usuarios, aquella es "quién soy yo ahora".
export interface UsuarioCuenta {
  id: string;
  nombre: string;
  email: string;
  rol: string;
  clienteId: number | null;
  activo: boolean;
}

export interface RutaResumen {
  id: number;
  fecha: string;
  vehiculo: string | null;
  repartidorNombre: string | null;
  estado: string;
  cantidadParadas: number;
}

export interface RutaDetalle {
  id: number;
  fecha: string;
  vehiculo: string | null;
  repartidorId: string | null;
  repartidorNombre: string | null;
  capacidadParadas: number;
  estado: string;
  cantidadParadas: number;
  kmInicial: number | null;
  kmFinal: number | null;
  combustibleMonto: number | null;
  peajesMonto: number | null;
  otrosCostos: number | null;
  pagoRepartidor: number | null;
  notasCierre: string | null;
  cerradaEn: string | null;
}

/** Candidato a entrar en una ruta (GET /api/pedidos/candidatos-ruta). */
export interface CandidatoRuta {
  pedidoId: number;
  clienteRazonSocial: string;
  destinatarioNombre: string;
  bultos: number;
  urgente: boolean;
  zonaCodigo: string | null;
  destinoUbicacionId: number;
  destinoCalleNumero: string;
  destinoLocalidad: string | null;
  lat: number | null;
  lng: number | null;
  direccionApta: boolean;
  yaEnEstaRuta: boolean;
}

/** Un repartidor u otro usuario, para selectores (GET /api/usuarios/seleccion). */
export interface UsuarioSeleccion {
  id: string;
  nombre: string;
}

/** Una parada ya guardada (GET /api/rutas/{id}/paradas), para reabrir un armado en curso. */
export interface ParadaArmada {
  ubicacionId: number;
  calleNumero: string;
  localidad: string | null;
  lat: number | null;
  lng: number | null;
  anclada: boolean;
  pedidoIds: number[];
}

export interface ResultadoRuta {
  ingresos: number;
  costos: number;
  margen: number;
  efectivas: number;
  fallidas: number;
  reprogramadas: number;
}
