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
  /** B9 (Anexo I §4, "+40 km → Cotización"): true si la zona de destino no tiene ninguna tarifa
   * cargada y todavía no se le fijó un precio manual. Solo puede ser true en Borrador. */
  requiereCotizacion: boolean;
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

/** Localidad sin zona asignada, bloqueada para cotizar (GET /api/localidades/pendientes, acta
 * changelog 3.10). La zona sugerida es orientativa (por distancia al depósito contra los rangos
 * de km ya cargados en /tarifas) — nunca se aplica sola, hace falta confirmar con `PUT .../zona`. */
export interface LocalidadPendiente {
  id: number;
  nombre: string;
  partido: string | null;
  distanciaKmDeposito: number | null;
  zonaSugeridaId: number | null;
  zonaSugeridaCodigo: string | null;
  zonaSugeridaNombre: string | null;
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
  estado: "pendiente" | "completada" | "fallida";
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
}

/** Espejo de MisParadasController.ParadaDelDia (GET /api/mis-paradas/dia). */
export interface ParadaDelDia {
  paradaId: number;
  rutaId: number;
  orden: number;
  tipo: string;
  estado: "pendiente" | "completada" | "fallida";
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
  motivosFallo: string[];
  umbralDesvioMetros: number;
  /** Solo null cuando no hay ruta en curso (`rutaId === null`) — una ruta en_curso siempre tiene
   * origen resuelto, CerrarPlanificacion lo exige (acta changelog 3.8). */
  origen: OrigenRuta | null;
  recorrido: Recorrido | null;
  paradas: ParadaDelDia[];
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
