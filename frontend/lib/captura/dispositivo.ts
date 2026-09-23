const PREFIJO = "logistica:device-uuid:parada:";
const PREFIJO_RETIRO = "logistica:device-uuid:retiro:";

/**
 * El device_uuid se genera EN EL MOMENTO DE LA CAPTURA, no al enviar — es lo único que hace
 * idempotente el reintento (RNF-02): el backend tiene un unique (pedido_id, device_uuid) y
 * responde `duplicado: true` en vez de crear una fila nueva. Se persiste hasta que el cierre
 * confirma para sobrevivir a que el repartidor cierre la app o pierda señal a mitad de la carga.
 */
export function uuidDeCaptura(paradaId: number): string {
  const clave = PREFIJO + paradaId;
  try {
    const existente = localStorage.getItem(clave);
    if (existente) return existente;
    const nuevo = crypto.randomUUID();
    localStorage.setItem(clave, nuevo);
    return nuevo;
  } catch {
    // localStorage no disponible (navegación privada muy restrictiva): se pierde la
    // idempotencia entre recargas, no dentro de esta sesión de la pestaña.
    return crypto.randomUUID();
  }
}

export function limpiarUuidDeCaptura(paradaId: number): void {
  try {
    localStorage.removeItem(PREFIJO + paradaId);
  } catch {
    // no-op
  }
}

/** Mismo criterio que `uuidDeCaptura`, pero por ruta: el retiro es un solo acto por ruta (RF-35),
 * así que el reintento del mismo botón tiene que reusar el uuid para que el servidor lo reconozca
 * como duplicado y no como un segundo dispositivo (409). */
export function uuidDeRetiro(rutaId: number): string {
  const clave = PREFIJO_RETIRO + rutaId;
  try {
    const existente = localStorage.getItem(clave);
    if (existente) return existente;
    const nuevo = crypto.randomUUID();
    localStorage.setItem(clave, nuevo);
    return nuevo;
  } catch {
    return crypto.randomUUID();
  }
}

export function limpiarUuidDeRetiro(rutaId: number): void {
  try {
    localStorage.removeItem(PREFIJO_RETIRO + rutaId);
  } catch {
    // no-op
  }
}
