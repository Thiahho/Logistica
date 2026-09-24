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
  /** null mientras el pedido sigue en Borrador (acta changelog 3.11): el precio depende del tipo
   * de vehículo, que recién se conoce cuando la ruta que lo lleva cierra su planificación. */
  total: number | null;
  fechaEntrega: string;
  clienteId: number;
  clienteRazonSocial: string;
  direccionDudosa: boolean;
  /** Bultos del pedido (declarados por el cliente o confirmados en recepción). */
  bultos: number;
  /** B9 (Anexo I §4, "+40 km → Cotización"): true si la zona de destino no tiene ninguna tarifa
   * cargada y todavía no se le fijó un precio manual. Solo puede ser true en Borrador. */
  requiereCotizacion: boolean;
  /** B3 (acta changelog 4.21): solo en la respuesta del alta — el saldo supera el límite de crédito
   * del rango del cliente. Solo avisa. */
  avisoCredito?: string | null;
}

/** Envoltorio de página (GET /api/pedidos con `pagina`/`tamanioPagina`). `total` es la cantidad
 * de filas que matchean el filtro, no un importe — no confundir con PedidoResumen.total. */
export interface ListaPaginada<T> {
  items: T[];
  total: number;
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

/** GET /api/mi-cuenta/plan-del-dia: envío de hoy del cliente con sus cambios de estado. */
export interface EventoEstado {
  estado: EstadoPedido;
  motivo: string | null;
  ocurridoEn: string;
}

export interface PedidoDelDia {
  id: number;
  destinatarioNombre: string;
  estado: EstadoPedido;
  destinoCalleNumero: string;
  destinoLocalidad: string | null;
  eventos: EventoEstado[];
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
  /** Coordenadas del destino (null si la dirección no se pudo geocodificar). */
  destinoLat: number | null;
  destinoLng: number | null;
  destinatarioNombre: string;
  destinatarioTelefono: string;
  bultos: number;
  pesoKg: number | null;
  valorDeclarado: number | null;
  fechaEntrega: string;
  urgente: boolean;
  // Todos null mientras el pedido sigue en Borrador (acta changelog 3.11) — peajes es la
  // excepción, se conoce desde el alta porque no depende del tipo de vehículo.
  precioBase: number | null;
  /** Anexo I §10.2-N: recargo % según el km recorrido, encima de precioBase. 0 cuando no hay
   * coordenadas utilizables (Servicios/DistanciaService.cs) o Precio:TramosKm está vacío. */
  recargoKm: number | null;
  /** Km con el que se cotizó — snapshot, no se recalcula tras congelar el precio. */
  kmCobrados: number | null;
  /** ruta | recta | manual — de dónde salió kmCobrados. */
  kmFuente: string | null;
  recargoUrgencia: number | null;
  descuentoRuta: number | null;
  /** B3, definición J (acta changelog 4.21): descuento del rango del cliente al cotizar. */
  descuentoRango: number;
  peajes: number;
  total: number | null;
  precioCongeladoEn: string | null;
  estado: EstadoPedido;
  origenCarga: string;
  observaciones: string | null;
  creadoEn: string;
  direccionDudosa: boolean;
  /** B9 (Anexo I §4): la zona de destino no tiene tarifa cargada y no se fijó precio manual. */
  requiereCotizacion: boolean;
  precioManual: PrecioManualInfo | null;
  /** E1 (§10.2-M/D13): derivado del historial, no una columna — 3 gratis antes de que la 4ta
   * genere un pedido `tipo='reintento'` en vez de reprogramar. */
  vecesReprogramado: number;
  /** E1: true si ya hay un factura_items tipo='pedido' para este pedido (entregado, o
   * cancelado ya confirmado) — no significa que ya esté en una factura EMITIDA, solo que ya
   * generó el cargo. */
  facturado: boolean;
  historial: HistorialEvento[];
}

/** Quién fijó el precio manual y cuándo (B9, Anexo I §4) — criterio subjetivo que afecta precio,
 * requiere rastro escrito (mismo espíritu que el ajuste de rango §10.2-F del Anexo). */
export interface PrecioManualInfo {
  precio: number;
  fijadoPor: string | null;
  fijadoEn: string;
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
  cicloFacturacion: string;
  corteSuspendidoHasta: string | null;
  corteSuspendidoMotivo: string | null;
  corteSuspendidoPor: string | null;
  corteSuspendidoEn: string | null;
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

/** Localidad que quedó sin zona y bloqueada para cotizar (GET /api/localidades/pendientes). La zona
 * se asigna sola midiendo contra el depósito (acta changelog 4.11); acá solo llegan las que no se
 * pudieron medir o que ningún rango de km cubre — se resuelven con `PUT .../zona`. */
export interface LocalidadPendiente {
  id: number;
  nombre: string;
  partido: string | null;
  distanciaKmDeposito: number | null;
  motivo: "sin_coordenadas" | "fuera_de_rango";
}

/** GET /api/localidades/con-zona: todas, con su zona y si fue automática o manual. */
export interface LocalidadDeZona {
  id: number;
  nombre: string;
  partido: string | null;
  zonaId: number | null;
  zonaCodigo: string | null;
  distanciaKmDeposito: number | null;
  zonaManual: boolean;
}

/** POST /api/localidades/recalcular. */
export interface ResultadoRecalculo {
  asignadas: number;
  sinZona: number;
  sinCoordenadas: number;
}

/** Precio orientativo al elegir la localidad: solo tarifa de la zona, por tipo de vehículo. */
export interface PrecioSugeridoLocalidad {
  zonaCodigo: string | null;
  zonaNombre: string | null;
  distanciaKm: number | null;
  camioneta: number | null;
  moto: number | null;
  requiereCotizacion: boolean;
}

/** Precio por cliente, ahora por zona x tipo de vehículo (acta changelog 3.11): camioneta y moto
 * tienen tarifa propia, no un factor sobre la otra. */
export interface TarifaZona {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  precioGeneralCamioneta: number | null;
  precioClienteCamioneta: number | null;
  precioGeneralMoto: number | null;
  precioClienteMoto: number | null;
}

export interface TarifaGeneral {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  kmDesde: number | null;
  kmHasta: number | null;
  precioCamioneta: number | null;
  precioMoto: number | null;
}

/** Tramo de km sin ninguna zona activa que lo cubra (auditoría §7, coherencia de km). hastaKm
 * null = hueco abierto hasta el final. Aviso no bloqueante, mismo criterio que RF-16. */
export interface HuecoKm {
  desdeKm: number;
  hastaKm: number | null;
}

/** GET /api/tarifas — envuelve la lista de zonas junto con los huecos detectados entre ellas. */
export interface TarifasResponse {
  zonas: TarifaGeneral[];
  huecos: HuecoKm[];
}

export type TipoVehiculo = "camioneta" | "moto";

/** Etiqueta comercial de cada tipo de vehículo (Anexo I §4, unificación de nomenclatura): la
 * infografía dice "Auto", el sistema sigue guardando "camioneta" — un solo lugar para el mapeo,
 * si el material comercial vuelve a cambiar de nombre no hay que barrer archivos. */
export function etiquetaTipoVehiculo(tipo: TipoVehiculo): string {
  return tipo === "camioneta" ? "Auto" : "Moto";
}

export interface DesglosePrecio {
  precioBase: number;
  /** Anexo I §10.2-N: recargo % según el km recorrido, encima de precioBase. 0 cuando no hay
   * coordenadas utilizables o Precio:TramosKm está vacío — el estimado no cambia respecto de
   * antes en ese caso. */
  recargoKm: number;
  recargoUrgencia: number;
  descuentoRuta: number;
  peajes: number;
  total: number;
  /** Km con el que se cotizó, o null si no se pudo resolver distancia. */
  kmCobrados: number | null;
  /** ruta | recta | manual — de dónde salió kmCobrados, o null si kmCobrados es null. */
  kmFuente: string | null;
  /** B3, definición J: descuento del rango del cliente, solo sobre la tarifa general. */
  descuentoRango: number;
}

/** Estimado informativo de POST /api/pedidos/cotizar (acta changelog 3.11) — nunca es el precio
 * final: null en un tipo = todavía no hay tarifa cargada para esa zona en ese tipo de vehículo.
 * requiereCotizacion (Anexo I B9) = true cuando ninguno de los dos tipos tiene tarifa. */
export interface CotizacionEstimada {
  camioneta: DesglosePrecio | null;
  moto: DesglosePrecio | null;
  requiereCotizacion: boolean;
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
// la tabla que administra /usuarios (personal interno), aquella es "quién soy yo ahora".
export interface UsuarioCuenta {
  id: string;
  nombre: string;
  email: string;
  rol: string;
  activo: boolean;
}

// Login de consulta de una empresa cliente — tabla separada de UsuarioCuenta a propósito
// (backend: Entidades/ClienteUsuario.cs), gestionada desde la ficha del cliente, no desde /usuarios.
export interface ClienteUsuarioCuenta {
  id: string;
  nombre: string;
  email: string;
  activo: boolean;
}

/** Fila del ABM de flota (GET /api/vehiculos). */
export interface Vehiculo {
  id: number;
  patente: string;
  descripcion: string | null;
  /** camioneta | moto (acta changelog 3.11) — determina qué tarifa aplica a los pedidos de una
   * ruta al cerrar su planificación. */
  tipo: TipoVehiculo;
  marca: string | null;
  modelo: string | null;
  anio: number | null;
  kmActual: number | null;
  venceVtv: string | null;
  venceSeguro: string | null;
  costoKm: number | null;
  capacidadParadas: number;
  activo: boolean;
}

/** Para el selector de vehículo al armar una ruta (GET /api/vehiculos/seleccion). */
export interface VehiculoSeleccion {
  id: number;
  patente: string;
  descripcion: string | null;
  capacidadParadas: number;
}

/** Ruta.Estado solo tiene estos tres valores (ck_rutas_estado). Espejo del check constraint. */
export type EstadoRuta = "planificada" | "en_curso" | "cerrada";
export const ESTADOS_RUTA: EstadoRuta[] = ["planificada", "en_curso", "cerrada"];

export function etiquetaEstadoRuta(estado: string): string {
  const etiquetas: Record<string, string> = {
    planificada: "Planificada",
    en_curso: "En curso",
    cerrada: "Cerrada",
  };
  return etiquetas[estado] ?? estado;
}

export interface RutaResumen {
  id: number;
  fecha: string;
  vehiculoPatente: string | null;
  repartidorNombre: string | null;
  estado: EstadoRuta;
  cantidadParadas: number;
  /** Suma de bultos de los pedidos de todas las paradas. */
  cantidadBultos: number;
}

export interface RutaDetalle {
  id: number;
  fecha: string;
  vehiculoId: number | null;
  vehiculoPatente: string | null;
  repartidorId: string | null;
  repartidorNombre: string | null;
  capacidadParadas: number;
  estado: EstadoRuta;
  cantidadParadas: number;
  kmInicial: number | null;
  kmFinal: number | null;
  combustibleMonto: number | null;
  peajesMonto: number | null;
  otrosCostos: number | null;
  pagoRepartidor: number | null;
  notasCierre: string | null;
  cerradaEn: string | null;
  /** Lo elegido, para saber en qué modo abrir el selector de partida. Null mientras el
   * planificador todavía no eligió nada (acta changelog 3.8: ya no hay depósito por default). */
  origenUbicacionId: number | null;
  /** Resuelto a partir de `origenUbicacionId` — null en la misma situación que ese campo. */
  origen: OrigenRuta | null;
  /** Declaración de la calle (acta changelog 4.7). Conviven con las columnas económicas de arriba,
   * que son el cierre de administración: nunca se pisan una a la otra. */
  retiroConfirmadoEn: string | null;
  retiroBultosEsperados: number | null;
  retiroBultosContados: number | null;
  retiroObservaciones: string | null;
  retiroKmInicial: number | null;
  cierreRepartidorEn: string | null;
  cierreRepartidorKmFinal: number | null;
  cierreRepartidorCombustible: number | null;
  cierreRepartidorPeajes: number | null;
  cierreRepartidorNotas: string | null;
  cerradaPor: string | null;
  // B4 (acta changelog 4.21): desglose del pago calculado al cerrar; null si se pagó a mano.
  liqEntregas: number | null;
  liqFallidasImputables: number | null;
  liqPctExito: number | null;
  liqPagoEntregas: number | null;
  liqBono: number | null;
  pagoAjusteMotivo: string | null;
  liquidacionId: number | null;
}

/** B4: pago al repartidor calculado para una ruta (GET /api/rutas/{id}/liquidacion-sugerida). */
export interface DesgloseLiquidacion {
  tipoVehiculo: string;
  entregas: number;
  fallidasImputables: number;
  fallidasNoImputables: number;
  pctExito: number;
  pagoPorEntrega: number;
  pagoEntregas: number;
  pctMinimoExitosas: number;
  bono: number;
  total: number;
}

/** B4: parámetros de liquidación vigentes por tipo de vehículo (GET /api/parametros-liquidacion). */
export interface ParametroLiquidacion {
  id: number;
  tipoVehiculo: string;
  pagoPorEntrega: number;
  bonoRuta: number;
  pctMinimoExitosas: number;
  motivosImputables: string[];
  vigenteDesde: string;
  vigenteHasta: string | null;
}

export interface ParametrosLiquidacionResponse {
  parametros: ParametroLiquidacion[];
  motivosFallo: string[];
}

/** B4: una ruta dentro de una liquidación (o candidata a entrar). */
export interface RutaLiquidable {
  id: number;
  fecha: string;
  vehiculoPatente: string | null;
  entregas: number | null;
  fallidasImputables: number | null;
  pctExito: number | null;
  pagoEntregas: number | null;
  bono: number | null;
  pagoRepartidor: number;
  pagoAjusteMotivo: string | null;
}

export interface PrevisualizacionLiquidacion {
  repartidorId: string;
  repartidorNombre: string;
  desde: string;
  hasta: string;
  rutas: RutaLiquidable[];
  total: number;
}

export interface LiquidacionResumen {
  id: number;
  repartidorId: string;
  repartidorNombre: string;
  desde: string;
  hasta: string;
  cantidadRutas: number;
  total: number;
  emitidaEn: string;
}

export interface LiquidacionDetalle extends LiquidacionResumen {
  nota: string | null;
  emitidaPorNombre: string;
  rutas: RutaLiquidable[];
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
  /** B9 (Anexo I §4): true si la zona no tiene tarifa cargada y el pedido no tiene precio manual
   * — va a rechazar el cierre de planificación si entra a la ruta así. */
  requiereCotizacion: boolean;
  precioManual: number | null;
  /** B3 (acta 4.21 frente a §7): el rango ordena la lista de pendientes al armar, nunca las paradas. */
  rangoNombre: string;
  prioridad: number;
}

/** Destinatario ya usado por un cliente, con su dirección ya geocodificada (GET
 * /api/pedidos/destinatarios-frecuentes). Espejo exacto de PedidosController.DestinatarioFrecuente. */
export interface DestinatarioFrecuente {
  destinatarioNombre: string;
  destinatarioTelefono: string;
  destinoUbicacionId: number;
  destinoCalleNumero: string;
  localidadId: number;
  localidadNombre: string;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
  veces: number;
  ultimaFechaEntrega: string;
}

/** Un repartidor u otro usuario, para selectores (GET /api/usuarios/seleccion). */
export interface UsuarioSeleccion {
  id: string;
  nombre: string;
}

/** Una parada ya guardada (GET /api/rutas/{id}/paradas), para reabrir un armado en curso o
 * para el detalle de ruta de solo lectura. */
export interface ParadaArmada {
  id: number;
  ubicacionId: number;
  calleNumero: string;
  localidad: string | null;
  lat: number | null;
  lng: number | null;
  anclada: boolean;
  orden: number;
  /** Igual que ParadaDelDia: cancelada = todos sus pedidos los canceló operación con la ruta en curso. */
  estado: "pendiente" | "completada" | "fallida" | "cancelada";
  llegadaEn: string | null;
  salidaEn: string | null;
  pedidoIds: number[];
}

export interface ResultadoRuta {
  ingresos: number;
  costos: number;
  margen: number;
  efectivas: number;
  fallidas: number;
  reprogramadas: number;
  /** Derivado en lectura: "tal_cual" (administración cerró con los números del repartidor),
   * "corregido", "sin_declaracion", o null si la ruta todavía no se cerró. */
  aprobacion: "tal_cual" | "corregido" | "sin_declaracion" | null;
}

/** Espejo de MiJornadaController.CierreJornadaResultado (POST /api/mi-jornada/cierre). */
export interface CierreJornadaResultado {
  rutaId: number;
  cierreConfirmadoEn: string;
  total: number;
  entregadas: number;
  fallidas: number;
  kmInicial: number;
  kmFinal: number;
  kmRecorridos: number;
  combustibleMonto: number;
  peajesMonto: number;
  notas: string | null;
  duplicado: boolean;
}

/** Espejo de Servicios/RuteoService.cs. Recorrido real por calles vía OSRM, trazado siempre en
 * el servidor (POST /api/recorrido o embebido en GET /api/mis-paradas/dia). */
export interface PuntoRuta {
  lat: number;
  lng: number;
}

export interface Recorrido {
  linea: PuntoRuta[];
  distanciaMetros: number;
  duracionSegundos: number;
}

/** Espejo de MisParadasController.PedidoDeParada. */
export interface PedidoDeParada {
  pedidoId: number;
  destinatarioNombre: string;
  destinatarioTelefono: string;
  bultos: number;
  observaciones: string | null;
  /** Estado del pedido (EnRuta, Cancelado...). Un pedido Cancelado lo canceló operación con la ruta
   * en curso: no se entrega, y el cierre de la parada lo saltea. */
  estado: EstadoPedido;
}

/** Espejo de PedidosController.PruebaEntregaResumen (GET /api/pedidos/{id}/prueba-entrega, solo administración). */
export interface PruebaEntregaResumen {
  id: number;
  resultado: "entregado" | "fallido";
  motivoFallo: string | null;
  receptorNombre: string | null;
  identidadVerificada: boolean;
  /** DNI del receptor, solo dígitos. null si no lo dio (ver sinDocumentoMotivo). */
  documentoNumero: string | null;
  sinDocumentoMotivo: string | null;
  tieneFoto: boolean;
  lat: number | null;
  lng: number | null;
  desvioMetros: number | null;
  desvioAlto: boolean;
  capturadaEn: string;
  sincronizadaEn: string;
}

/** Espejo de MisParadasController.ParadaDelDia (GET /api/mis-paradas/dia). */
export interface ParadaDelDia {
  paradaId: number;
  rutaId: number;
  orden: number;
  tipo: string;
  /** cancelada: todos sus pedidos los canceló operación. Terminal, sin prueba de entrega. */
  estado: "pendiente" | "completada" | "fallida" | "cancelada";
  llegadaEn: string | null;
  salidaEn: string | null;
  calleNumero: string;
  localidad: string | null;
  referencia: string | null;
  lat: number | null;
  lng: number | null;
  pedidos: PedidoDeParada[];
}

/** Origen de una ruta: un depósito del catálogo, o cualquier otra dirección (donde quedó la
 * camioneta el día anterior — acta changelog 3.6/3.8). Espejo exacto de
 * Servicios/OrigenRutaService.OrigenRuta. Lat/lng nullable: una dirección de partida sin
 * geocodificar no bloquea el armado, solo degrada el recorrido a la primera parada. */
export interface OrigenRuta {
  ubicacionId: number;
  calleNumero: string;
  localidad: string | null;
  localidadId: number | null;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
  esDeposito: boolean;
  nombreDeposito: string | null;
}

/** Un depósito del catálogo (GET /api/ubicaciones/depositos), seleccionable al armar una ruta.
 * Espejo exacto de Servicios/OrigenRutaService.Deposito. */
export interface Deposito {
  ubicacionId: number;
  nombre: string;
  calleNumero: string;
  localidad: string | null;
  localidadId: number | null;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
}

/** Bundle único de RNF-07: todo lo que la PWA necesita antes de salir, en un solo request —
 * incluido el recorrido ya trazado, para que el mapa funcione sin señal en la calle. */
export interface JornadaDelDia {
  fecha: string | null;
  rutaId: number | null;
  total: number;
  completadas: number;
  fallidas: number;
  canceladas: number;
  motivosFallo: string[];
  umbralDesvioMetros: number;
  /** Solo null cuando no hay ruta en curso (`rutaId === null`) — una ruta en_curso siempre tiene
   * origen resuelto, CerrarPlanificacion lo exige (acta changelog 3.8). */
  origen: OrigenRuta | null;
  recorrido: Recorrido | null;
  paradas: ParadaDelDia[];
  /** RF-35: null hasta que el repartidor firma el conteo — sin eso el servidor rechaza llegada y
   * cierre de parada (409, acta §7). */
  retiroConfirmadoEn: string | null;
  /** Bultos de la ruta: en vivo mientras el retiro no está firmado, congelados una vez firmado. */
  bultosEsperados: number;
  /** RF-26: declaración de cierre del repartidor. No es el cierre de la ruta: queda pendiente de
   * revisión de administración. */
  cierreRepartidorEn: string | null;
  /** Novedades abiertas o sin ver de la ruta (acta changelog 4.8, RF-36/RF-37). */
  novedades: NovedadDelDia[];
  /** Listas cerradas, en configuración del servidor (provisionales). */
  categoriasIncidencia: string[];
  categoriasCarga: string[];
}

export type TipoNovedad =
  | "incidencia_ruta"
  | "problema_carga"
  | "cambio_propuesto"
  | "cambio_operacion"
  | "cancelacion"
  /** Acta RF-45 (changelog 4.26): operación agregó una urgencia a la ruta en curso. */
  | "urgencia";

/** Espejo de MisParadasController.NovedadDelDia. `sinVer`: el repartidor todavía no acusó recibo — un
 * aviso de operación (cambio, cancelación) o la respuesta a algo que él informó. */
export interface NovedadDelDia {
  id: number;
  tipo: TipoNovedad;
  origen: "repartidor" | "operacion";
  categoria: string | null;
  descripcion: string;
  propuestaCampo: string | null;
  propuestaValorNuevo: string | null;
  paradaId: number | null;
  pedidoId: number | null;
  estado: "abierta" | "resuelta" | "rechazada";
  resolucion: string | null;
  creadaEn: string;
  resueltaEn: string | null;
  sinVer: boolean;
}

/** Espejo de MiJornadaController.NovedadResultado (POST /api/mi-jornada/novedades). */
export interface NovedadResultado {
  id: number;
  tipo: TipoNovedad;
  estado: string;
  duplicado: boolean;
}

/** Espejo de NovedadesController.NovedadResumen: la vista de back-office. */
export interface NovedadResumen {
  id: number;
  rutaId: number;
  rutaFecha: string;
  repartidorNombre: string | null;
  paradaId: number | null;
  paradaOrden: number | null;
  pedidoId: number | null;
  pedidoDestinatario: string | null;
  tipo: TipoNovedad;
  origen: "repartidor" | "operacion";
  categoria: string | null;
  descripcion: string;
  propuestaCampo: string | null;
  propuestaValorAnterior: string | null;
  propuestaValorNuevo: string | null;
  tieneFoto: boolean;
  estado: "abierta" | "resuelta" | "rechazada";
  creadaPorNombre: string;
  creadaEn: string;
  resueltaPorNombre: string | null;
  resueltaEn: string | null;
  resolucion: string | null;
  vistoEn: string | null;
}

export const ETIQUETA_TIPO_NOVEDAD: Record<TipoNovedad, string> = {
  incidencia_ruta: "Incidencia de ruta",
  problema_carga: "Problema con la carga",
  cambio_propuesto: "Corrección propuesta",
  cambio_operacion: "Cambio de operación",
  cancelacion: "Pedido cancelado",
  urgencia: "Urgencia agregada",
};

/** RF-45: GET /api/rutas/urgencias/ventana. Horas "HH:mm:ss". */
export interface VentanaUrgencias {
  desde: string;
  hasta: string;
  abierta: boolean;
  maxParadasDesplazadas: number;
}

/** RF-45: respuesta de POST /api/rutas/{id}/urgencias. */
export interface UrgenciaInsertada {
  paradaId: number;
  orden: number;
  desplazadas: number;
  consolidada: boolean;
  total: number | null;
}

export const ETIQUETA_CAMPO_EDITABLE: Record<string, string> = {
  destinatario_telefono: "Teléfono",
  destinatario_nombre: "Nombre del destinatario",
  observaciones: "Observaciones",
};

/** "bulto_danado" → "Bulto danado". Las categorías son valores de configuración, no traducciones. */
export function etiquetaCategoria(categoria: string): string {
  const s = categoria.replaceAll("_", " ");
  return s.charAt(0).toUpperCase() + s.slice(1);
}

/** Espejo de MiJornadaController.RetiroResultado (POST /api/mi-jornada/retiro). */
export interface RetiroResultado {
  rutaId: number;
  retiroConfirmadoEn: string;
  bultosEsperados: number;
  bultosContados: number;
  discrepancia: boolean;
  duplicado: boolean;
}

/** Espejo de Servicios/JornadaService.JornadaRuta (GET /api/rutas/{id}/jornada) — mismo bundle
 * que JornadaDelDia, para una ruta puntual en vez de "la ruta en_curso del repartidor
 * autenticado". Sin MotivosFallo/UmbralDesvioMetros (config del repartidor, no del
 * back-office) ni Fecha (RutaDetalle ya la tiene). */
export interface JornadaRuta {
  rutaId: number;
  total: number;
  completadas: number;
  fallidas: number;
  canceladas: number;
  origen: OrigenRuta | null;
  recorrido: Recorrido | null;
  paradas: ParadaDelDia[];
}

/** Espejo de MisParadasController.CierreResultado (POST /api/mis-paradas/{id}/cierre). */
export interface CierreResultado {
  paradaId: number;
  estadoParada: string;
  pedidosActualizados: number;
  desvioMetros: number | null;
  desvioAlto: boolean;
  duplicado: boolean;
  /** Pendiente de menor orden, resuelta por el servidor tras aplicar el cierre. null = no queda ninguna. */
  siguienteParadaId: number | null;
}

// ==================== E1 — cuenta corriente y facturación ====================

export type CicloFacturacion = "quincenal" | "mensual";

export function etiquetaCiclo(ciclo: CicloFacturacion): string {
  return ciclo === "quincenal" ? "Quincenal" : "Mensual";
}

/** pendiente | parcial | pagada | vencida — derivado (v_facturas_saldo), nunca persistido. */
export type EstadoFactura = "pendiente" | "parcial" | "pagada" | "vencida";

export function etiquetaEstadoFactura(estado: string): string {
  const etiquetas: Record<string, string> = {
    pendiente: "Pendiente",
    parcial: "Parcial",
    pagada: "Pagada",
    vencida: "Vencida",
  };
  return etiquetas[estado] ?? estado;
}

/** Espejo de FacturasController.FacturaResumen (GET /api/facturas). */
export interface FacturaResumen {
  id: number;
  clienteId: number;
  clienteRazonSocial: string;
  ciclo: CicloFacturacion;
  periodoDesde: string;
  periodoHasta: string;
  fechaEmision: string;
  fechaVencimiento: string;
  total: number;
  pagado: number;
  saldo: number;
  estado: EstadoFactura;
}

/** tipo de un ítem de factura: pedido | ajuste | nota_credito. */
export type TipoFacturaItem = "pedido" | "ajuste" | "nota_credito";

export interface FacturaItemResumen {
  id: number;
  pedidoId: number | null;
  tipo: TipoFacturaItem;
  descripcion: string;
  monto: number | null;
  estado: "pendiente" | "aprobado" | "rechazado";
}

/** Espejo de FacturasController.FacturaDetalle (GET /api/facturas/{id}). */
export interface FacturaDetalle extends FacturaResumen {
  items: FacturaItemResumen[];
}

/** Espejo de ClientesController.FacturaClienteResumen (dentro de CuentaCorrienteCliente). */
export interface FacturaClienteResumen {
  id: number;
  ciclo: CicloFacturacion;
  periodoDesde: string;
  periodoHasta: string;
  fechaEmision: string;
  fechaVencimiento: string;
  total: number;
  pagado: number;
  saldo: number;
  estado: EstadoFactura;
}

/** Espejo de ClientesController.PagoResumen. */
export interface PagoResumen {
  id: number;
  monto: number;
  fechaPago: string;
  medio: string;
  nota: string | null;
  registradoPorNombre: string | null;
  registradoEn: string;
}

/** Espejo de ClientesController.CuentaCorrienteCliente (GET /api/clientes/{id}/cuenta-corriente). */
export interface CuentaCorrienteCliente {
  saldo: number;
  deudaVencida: number;
  servicioCortado: boolean;
  corteSuspendidoHasta: string | null;
  corteSuspendidoMotivo: string | null;
  corteSuspendidoPorNombre: string | null;
  corteSuspendidoEn: string | null;
  pendienteDeFacturar: number;
  ajustesPendientes: number;
  facturas: FacturaClienteResumen[];
  pagos: PagoResumen[];
}

/** Espejo de MiCuentaController.FacturaPropia/CuentaPropia (GET /api/mi-cuenta, rol cliente). */
export interface FacturaPropia {
  id: number;
  periodoDesde: string;
  periodoHasta: string;
  fechaVencimiento: string;
  total: number;
  saldo: number;
  estado: EstadoFactura;
}

export interface CuentaPropia {
  saldo: number;
  deudaVencida: number;
  servicioCortado: boolean;
  proximoVencimiento: string | null;
  facturas: FacturaPropia[];
  /** B3 (acta RF-42): el cliente ve su rango y su descuento, nunca los números con que se calculó. */
  rangoNombre: string;
  descuentoPct: number;
}

/** B3 (acta RF-42): configuración de un rango (GET /api/rangos). Umbrales null = no exige nada. */
export interface RangoConfig {
  codigo: string;
  nombre: string;
  orden: number;
  minEnviosTrimestre: number | null;
  minFacturacionTrimestre: number | null;
  minAntiguedadMeses: number | null;
  minSemanasActivas: number | null;
  minPctPagosEnTermino: number | null;
  descuentoPct: number;
  limiteCredito: number | null;
  prioridad: number;
}

export interface CriteriosRango {
  envios: number;
  facturacion: number;
  antiguedadMeses: number;
  semanasActivas: number;
  pctPagosEnTermino: number;
  facturasVencidas: number;
}

/** Lo que el recálculo trimestral haría (o hizo) con un cliente. */
export interface RangoRecalculado {
  clienteId: number;
  razonSocial: string;
  criterios: CriteriosRango;
  calculadoAnterior: string;
  calculadoNuevo: string;
  efectivoAnterior: string;
  efectivoNuevo: string;
  ajusteVencido: boolean;
}

export interface HistorialRango {
  rangoAnterior: string | null;
  rangoNuevo: string;
  origen: "recalculo" | "ajuste";
  trimestre: string | null;
  /** CriteriosRango serializado por el backend (claves en PascalCase). */
  criterios: string | null;
  motivo: string | null;
  registradoPor: string;
  registradoEn: string;
}

/** B7 (acta RF-43): costo fijo de un mes. */
export interface CostoFijo {
  id: number;
  mes: string;
  categoria: string;
  descripcion: string | null;
  monto: number;
}

/** B7: un tramo de la estructura objetivo (PUT/GET /api/objetivos-rentabilidad). */
export interface ObjetivoRentabilidad {
  nombre: string;
  pctMin: number;
  pctMax: number;
  /** pago_repartidor | combustible | peajes | otros_costos | fijos | fijos:<categoría> | margen */
  fuentes: string[];
}

export interface TramoEvaluado extends ObjetivoRentabilidad {
  id: number;
  monto: number;
  /** null si el mes no tuvo ingresos. */
  pct: number | null;
  estado: "debajo" | "dentro" | "encima" | null;
}

/** GET /api/rentabilidad?mes=2026-09 — solo Administración. */
export interface ResultadoMes {
  mes: string;
  ingresos: number;
  pagoRepartidor: number;
  combustible: number;
  peajes: number;
  otrosCostos: number;
  variables: number;
  fijos: number;
  margen: number;
  pctMargen: number | null;
  rutasCerradas: number;
  fijosPorCategoria: Record<string, number>;
  tramos: TramoEvaluado[];
  costosFijos: CostoFijo[];
}

/** B2/E3: tablero de indicadores (GET /api/tablero). null = sin datos para calcularlo. */
export interface Tablero {
  desde: string;
  hasta: string;
  /** false con filtro de cliente o zona: el costo de una ruta no se prorratea, los indicadores de ruta no se muestran. */
  indicadoresDeRuta: boolean;
  entregas: number;
  entregasPorDia: number | null;
  kmPorEntrega: number | null;
  minutosPorEntrega: number | null;
  facturacion: number;
  facturacionPorCliente: { nombre: string; valor: number }[];
  facturacionPorRango: { nombre: string; valor: number }[];
  cancelados: number;
  pctCancelaciones: number | null;
  rutasCerradas: number;
  costoPorEntrega: number | null;
  costoPorRuta: number | null;
  margenTotal: number | null;
  margenPorRuta: number | null;
  pctOcupacion: number | null;
  porDia: { fecha: string; entregas: number; margen: number | null }[];
}

/** GET /api/clientes/{id}/rango — solo Administración. */
export interface RangoDeCliente {
  clienteId: number;
  calculado: string;
  efectivo: string;
  calculadoEn: string | null;
  ajuste: number;
  ajusteMotivo: string | null;
  ajusteVence: string | null;
  ajusteVigente: boolean;
  descuentoPct: number;
  limiteCredito: number | null;
  historial: HistorialRango[];
}

/** Resultado de un cierre de ciclo por cliente (GET .../cierre/previsualizacion, POST .../cierre). */
export interface ResultadoCierreCliente {
  clienteId: number;
  razonSocial: string;
  ciclo: CicloFacturacion;
  facturaId: number | null;
  periodoDesde: string;
  periodoHasta: string;
  fechaVencimiento: string;
  total: number;
  cantidadItems: number;
  ajustesPendientes: number;
  omitido: string | null;
}

// ---- Panel de cobranza (clientes críticos) ----

/** vencido | por_vencer — categoría del cliente en el panel de cobranza (Servicios/
 * CuentaCorrienteService.cs RiesgoAsync: deuda ya vencida, o factura que vence dentro de la
 * ventana de preaviso). */
export type CategoriaRiesgo = "vencido" | "por_vencer";

export function etiquetaCategoriaRiesgo(categoria: string): string {
  return categoria === "vencido" ? "Vencido" : categoria === "por_vencer" ? "Por vencer" : categoria;
}

/** Espejo de CuentaCorrienteService.ClienteEnRiesgo (GET /api/clientes/riesgo). */
export interface ClienteEnRiesgo {
  clienteId: number;
  razonSocial: string;
  email: string | null;
  telefono: string | null;
  activo: boolean;
  categoria: CategoriaRiesgo;
  saldo: number;
  deudaVencida: number;
  servicioCortado: boolean;
  corteSuspendidoHasta: string | null;
  proximoVencimiento: string | null;
  saldoProximoAVencer: number;
  diasHastaVencimiento: number | null;
  colorPago: string;
  colorTrato: string;
  colorOper: string;
}

/** Espejo de ClientesController.AvisoPrevisualizado — el asunto/mensaje ya vienen armados por
 * el servidor (nunca los arma el front). `omitido` no null = no se va a mandar nada para este
 * cliente. */
export interface AvisoPrevisualizado {
  clienteId: number;
  razonSocial: string;
  categoria: string;
  monto: number;
  vencimiento: string | null;
  email: string | null;
  telefonoNormalizado: string | null;
  asunto: string | null;
  mensaje: string | null;
  linkWhatsApp: string | null;
  omitido: string | null;
}

/** Espejo de POST /api/clientes/avisos/previsualizacion. */
export interface PrevisualizacionAvisos {
  resendConfigurado: boolean;
  avisos: AvisoPrevisualizado[];
}

/** Espejo de ClientesController.ResultadoAvisoCliente (POST /api/clientes/avisos). */
export interface ResultadoAvisoCliente {
  clienteId: number;
  razonSocial: string;
  categoria: string;
  emailDestino: string | null;
  emailEnviado: boolean;
  emailSimulado: boolean;
  emailError: string | null;
  telefonoNormalizado: string | null;
  linkWhatsApp: string | null;
  eventoId: number | null;
  omitido: string | null;
}

/** Espejo de PedidosController.AjusteResumen (GET/POST /api/pedidos/{id}/ajustes). */
export interface AjusteResumen {
  id: number;
  tipo: TipoFacturaItem;
  descripcion: string;
  monto: number | null;
  estado: "pendiente" | "aprobado" | "rechazado";
  creadoPorNombre: string | null;
  creadoEn: string;
  resueltoPorNombre: string | null;
  resueltoEn: string | null;
}

/** Espejo de PedidosController.ReintentoCreado — respuesta 200 cuando la 4ta reprogramación
 * redirige a un pedido nuevo tipo='reintento' en vez de reprogramar (D13, §10.2-M). */
export interface ReintentoCreado {
  pedidoOriginalId: number;
  reintentoId: number;
  reprogramaciones: number;
}

// ==================== Jornada (monitor del día, §9.3 ciclo diario — NO es el tablero B2) ====================

/** Espejo de JornadaController.ResumenJornada (GET /api/jornada/resumen). */
export interface ResumenJornada {
  fecha: string;
  rutas: ContadorRutas;
  paradas: ContadorParadas;
  rutasDelDia: RutaDelDia[];
  repartidores: RepartidorDelDia[];
  generadoEn: string;
  /** Rutas en curso con la declaración de cierre del repartidor esperando revisión de administración. */
  declaracionesPendientes: number;
  /** Novedades del repartidor sin responder, sumadas sobre todas las rutas de la fecha. */
  novedadesAbiertas: number;
}

export interface ContadorRutas {
  planificadas: number;
  enCurso: number;
  cerradas: number;
  total: number;
}

export interface ContadorParadas {
  total: number;
  pendientes: number;
  completadas: number;
  fallidas: number;
}

export interface RutaDelDia {
  id: number;
  estado: EstadoRuta;
  vehiculoPatente: string | null;
  repartidorId: string | null;
  repartidorNombre: string | null;
  paradas: number;
  pendientes: number;
  completadas: number;
  fallidas: number;
}

export interface RepartidorDelDia {
  repartidorId: string;
  nombre: string;
  rutaId: number;
  rutaEstado: EstadoRuta;
  vehiculoPatente: string | null;
  paradas: number;
  completadas: number;
  fallidas: number;
  pendientes: number;
  primeraLlegada: string | null;
  ultimaActividad: string | null;
  retiroConfirmadoEn: string | null;
  retiroBultosEsperados: number | null;
  retiroBultosContados: number | null;
  cierreRepartidorEn: string | null;
  /** Lo que informó el repartidor desde la calle y nadie respondió todavía. */
  novedadesAbiertas: number;
}

// ==================== Deliverys / urgencias (Anexo I D14, §10.2-N) ====================

/** Servicio punto a punto ad-hoc: un Pedido con tipo="delivery" (no una tabla nueva — construccion_v1.md
 * §1, techo de 17 tablas ya alcanzado). A diferencia de un Pedido normal, el operador elige el
 * tipo de vehículo en el alta, así que nace directo en Confirmado, con el precio ya congelado.
 * Espejo de DeliverysController.DeliveryResumen. */
export interface DeliveryResumen {
  id: number;
  destinatarioNombre: string;
  estado: EstadoPedido;
  total: number | null;
  fechaEntrega: string;
  clienteId: number;
  clienteRazonSocial: string;
  urgente: boolean;
  kmCobrados: number | null;
  kmFuente: string | null;
}

/** Espejo de DeliverysController.CotizarDeliveryRequest — origen y destino ya resueltos vía
 * POST /api/ubicaciones (mismo <SelectorDireccion> que el resto del sistema). */
export interface CotizarDeliveryRequest {
  origenUbicacionId: number;
  destinoUbicacionId: number;
  clienteId: number;
  fechaEntrega: string;
  urgente: boolean;
  tipoVehiculo: TipoVehiculo;
  peajes: number;
  kmManual?: number | null;
  precioManual?: number | null;
}

/** Espejo de DeliverysController.CrearDeliveryRequest. */
export interface CrearDeliveryRequest {
  clienteId: number;
  referenciaCliente: string | null;
  origenUbicacionId: number;
  destinoUbicacionId: number;
  destinatarioNombre: string;
  destinatarioTelefono: string;
  bultos: number;
  pesoKg: number | null;
  valorDeclarado: number | null;
  fechaEntrega: string;
  urgente: boolean;
  tipoVehiculo: TipoVehiculo;
  peajes: number;
  kmManual: number | null;
  precioManual: number | null;
  observaciones: string | null;
}

// ---------------------------------------------------------------------------
// Repartidores (RF-34, acta changelog 4.6) — espejo de RepartidoresController.
// No hay entidad `Repartidor`: es `usuarios.rol='repartidor'` con disponibilidad
// y carga derivadas de rutas/ruta_paradas en lectura (acta §4).
// ---------------------------------------------------------------------------

/** Derivada, nunca declarada. Precedencia: inactivo > en_ruta > asignado > libre.
 * Siempre es el estado de HOY, aunque el rango de fechas elegido sea otro — el rango
 * gobierna solo el acumulado. No existen ausencias (franco/vacaciones/licencia): un
 * repartidor de vacaciones figura "libre", costo asumido en el acta. */
export type Disponibilidad = "inactivo" | "en_ruta" | "asignado" | "libre";

/** Espejo de RepartidoresController.RepartidorListado. */
export interface RepartidorListado {
  id: string;
  nombre: string;
  activo: boolean;
  disponibilidad: Disponibilidad;
  rutaHoyId: number | null;
  rutaHoyEstado: EstadoRuta | null;
  vehiculoHoyPatente: string | null;
  paradasHoy: number;
  completadasHoy: number;
  fallidasHoy: number;
  pendientesHoy: number;
  rutasRango: number;
  paradasRango: number;
  completadasRango: number;
  fallidasRango: number;
}

/** Espejo de RepartidoresController.RutaDelRango. */
export interface RutaDelRango {
  rutaId: number;
  fecha: string;
  estado: EstadoRuta;
  vehiculoPatente: string | null;
  paradas: number;
  completadas: number;
  fallidas: number;
  pendientes: number;
}

/** Espejo de RepartidoresController.RepartidorDetalle. */
export interface RepartidorDetalle {
  id: string;
  nombre: string;
  email: string;
  activo: boolean;
  disponibilidad: Disponibilidad;
  desde: string;
  hasta: string;
  rutasRango: number;
  paradasRango: number;
  completadasRango: number;
  fallidasRango: number;
  rutas: RutaDelRango[];
}

// ---- Portal del cliente: alta (B5) y recepción (B13) ----

/** Espejo de MiCuentaController.CrearPedidoPortalRequest. */
export interface CrearPedidoPortalRequest {
  destinatarioNombre: string;
  destinatarioTelefono: string;
  destinoUbicacionId: number;
  bultos: number;
  pesoKg: number | null;
  fechaEntrega: string;
  urgente: boolean;
  tipoVehiculo: TipoVehiculo;
  observaciones: string | null;
}

/** Espejo de MiCuentaController.PedidoPortalCreado. precio null + requiereCotizacion true = B9
 * (zona sin tarifa): el pedido se creó igual, pero todavía no tiene precio vinculante. */
export interface PedidoPortalCreado {
  id: number;
  fechaEntrega: string;
  precio: number | null;
  requiereCotizacion: boolean;
}

/** Cuerpo del 409 de corte horario (MiCuentaController.CrearPedido §6). */
export interface CorteHorarioPortal {
  mensaje: string;
  fechaEntregaSugerida: string;
}

/** Espejo de PedidosController.PedidoRecepcionPendiente (GET /api/pedidos/recepcion-pendiente). */
export interface PedidoRecepcionPendiente {
  id: number;
  clienteRazonSocial: string;
  destinatarioNombre: string;
  bultos: number;
  bultosDeclaradoCliente: number;
  creadoEn: string;
}

/** Espejo de PedidosController.ConfirmarRecepcionRequest. */
export interface ConfirmarRecepcionRequest {
  bultosConfirmados: number;
  nota?: string | null;
}

/** "Mis clientes" (registro explícito, reversión de la decisión de acta 3.5, changelog 4.10) —
 * espejo de MiCuentaController.ClienteDestinatarioResumen. Misma forma que DestinatarioFrecuente
 * menos veces/ultimaFechaEntrega, más id/observaciones. */
export interface ClienteDestinatarioResumen {
  id: string;
  nombre: string;
  telefono: string;
  destinoUbicacionId: number;
  destinoCalleNumero: string;
  localidadId: number;
  localidadNombre: string | null;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
  observaciones: string | null;
}

/** Espejo de MiCuentaController.GuardarClienteDestinatarioRequest. */
export interface GuardarClienteDestinatarioRequest {
  nombre: string;
  telefono: string;
  destinoUbicacionId: number;
  observaciones?: string | null;
}
