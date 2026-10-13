/**
 * Borrador local de la captura de una entrega: lo que el repartidor ya cargó (receptor, DNI, foto)
 * sobrevive a una recarga o a que el teléfono cierre la pestaña mientras no hay señal. No es una cola
 * offline: nada se envía solo, el repartidor vuelve a tocar "Confirmar entrega".
 *
 * Vive en IndexedDB y no en localStorage porque la foto es un Blob. Los campos y la foto van en
 * almacenes separados para no reescribir la foto (varios MB) en cada tecla.
 *
 * Nada de acá lanza: sin IndexedDB (navegación privada, cuota llena, navegador viejo) la pantalla
 * sigue andando igual que antes, solo que sin borrador.
 */

const BASE = "logistica-borradores";
const VERSION = 1;
const ALMACEN_CAMPOS = "captura-parada-campos";
const ALMACEN_FOTOS = "captura-parada-fotos";
const ALMACENES = [ALMACEN_CAMPOS, ALMACEN_FOTOS];
const INDICE_FECHA = "guardadoEn";

/** Un borrador es de la jornada: las paradas del día siguiente son otras. Pasado un día se borra,
 * para no dejar nombres, DNI y fotos de terceros guardados en el teléfono. */
export const EDAD_MAXIMA_BORRADOR_MS = 24 * 60 * 60 * 1000;

export interface CamposDeCaptura {
  receptorNombre: string;
  documento: string;
  sinDocumento: boolean;
  sinDocumentoMotivo: string;
}

export interface BorradorDeCaptura {
  campos: CamposDeCaptura | null;
  foto: File | null;
}

interface RegistroDeCampos extends CamposDeCaptura {
  clave: string;
  guardadoEn: number;
}

interface RegistroDeFoto {
  clave: string;
  guardadoEn: number;
  foto: Blob;
  nombre: string;
}

/** Por usuario además de por parada: en un teléfono compartido, el borrador de un repartidor no le
 * aparece a otro. */
export function claveDeBorrador(usuarioId: string, paradaId: number): string {
  return `${usuarioId}:${paradaId}`;
}

export function camposVacios(campos: CamposDeCaptura): boolean {
  return (
    !campos.receptorNombre.trim() && !campos.documento.trim() && !campos.sinDocumento && !campos.sinDocumentoMotivo.trim()
  );
}

export function borradorVencido(guardadoEn: number, ahora: number): boolean {
  return ahora - guardadoEn > EDAD_MAXIMA_BORRADOR_MS;
}

function abrir(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const pedido = indexedDB.open(BASE, VERSION);
    pedido.onupgradeneeded = () => {
      for (const nombre of ALMACENES) {
        pedido.result.createObjectStore(nombre, { keyPath: "clave" }).createIndex(INDICE_FECHA, INDICE_FECHA);
      }
    };
    pedido.onsuccess = () => resolve(pedido.result);
    pedido.onerror = () => reject(pedido.error);
    pedido.onblocked = () => reject(new Error("IndexedDB bloqueada"));
  });
}

/** Corre `trabajo` en una transacción sobre los dos almacenes y resuelve cuando la transacción
 * confirma (no cuando termina cada pedido): recién ahí lo escrito está en disco. `trabajo` devuelve
 * cómo leer el resultado una vez confirmada. */
async function enTransaccion<T>(modo: IDBTransactionMode, trabajo: (tx: IDBTransaction) => () => T): Promise<T> {
  const base = await abrir();
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = base.transaction(ALMACENES, modo);
      const resultado = trabajo(tx);
      tx.oncomplete = () => resolve(resultado());
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  } finally {
    base.close();
  }
}

const sinResultado = () => undefined;

/** Lo guardado para esa clave, o null si no hay nada, está vencido o el almacenamiento no anda. */
export async function leerBorrador(clave: string, ahora = Date.now()): Promise<BorradorDeCaptura | null> {
  try {
    const { campos, foto } = await enTransaccion("readonly", (tx) => {
      const pedidoCampos = tx.objectStore(ALMACEN_CAMPOS).get(clave);
      const pedidoFoto = tx.objectStore(ALMACEN_FOTOS).get(clave);
      return () => ({
        campos: pedidoCampos.result as RegistroDeCampos | undefined,
        foto: pedidoFoto.result as RegistroDeFoto | undefined,
      });
    });
    const camposVigentes = campos && !borradorVencido(campos.guardadoEn, ahora) ? campos : null;
    const fotoVigente = foto && !borradorVencido(foto.guardadoEn, ahora) ? foto : null;
    if (!camposVigentes && !fotoVigente) return null;
    return {
      campos: camposVigentes && {
        receptorNombre: camposVigentes.receptorNombre,
        documento: camposVigentes.documento,
        sinDocumento: camposVigentes.sinDocumento,
        sinDocumentoMotivo: camposVigentes.sinDocumentoMotivo,
      },
      // Algunos navegadores devuelven un Blob pelado en vez del File que se guardó.
      foto: fotoVigente && new File([fotoVigente.foto], fotoVigente.nombre, { type: fotoVigente.foto.type }),
    };
  } catch {
    return null;
  }
}

/** Guarda los campos del formulario; si quedaron todos vacíos, borra el registro en vez de dejar uno en blanco. */
export async function guardarCampos(clave: string, campos: CamposDeCaptura, ahora = Date.now()): Promise<void> {
  try {
    await enTransaccion("readwrite", (tx) => {
      const almacen = tx.objectStore(ALMACEN_CAMPOS);
      if (camposVacios(campos)) almacen.delete(clave);
      else almacen.put({ clave, guardadoEn: ahora, ...campos } satisfies RegistroDeCampos);
      return sinResultado;
    });
  } catch {
    // Sin almacenamiento: la captura sigue solo en memoria, como antes.
  }
}

/** Guarda la foto elegida (la original, sin comprimir: la compresión sigue siendo al enviar), o la borra con null. */
export async function guardarFoto(clave: string, foto: File | null, ahora = Date.now()): Promise<void> {
  try {
    await enTransaccion("readwrite", (tx) => {
      const almacen = tx.objectStore(ALMACEN_FOTOS);
      if (!foto) almacen.delete(clave);
      else almacen.put({ clave, guardadoEn: ahora, foto, nombre: foto.name } satisfies RegistroDeFoto);
      return sinResultado;
    });
  } catch {
    // Cuota llena o sin almacenamiento: la foto sigue solo en memoria, como antes.
  }
}

export async function descartarBorrador(clave: string): Promise<void> {
  try {
    await enTransaccion("readwrite", (tx) => {
      for (const nombre of ALMACENES) tx.objectStore(nombre).delete(clave);
      return sinResultado;
    });
  } catch {
    // no-op
  }
}

/** Borra los borradores vencidos de cualquier parada y de cualquier usuario de este teléfono. */
export async function purgarBorradoresVencidos(ahora = Date.now()): Promise<void> {
  try {
    await enTransaccion("readwrite", (tx) => {
      // upperBound abierto: mismo criterio estricto que borradorVencido.
      const vencidos = IDBKeyRange.upperBound(ahora - EDAD_MAXIMA_BORRADOR_MS, true);
      for (const nombre of ALMACENES) {
        const almacen = tx.objectStore(nombre);
        // Cursor de claves: recorre el índice por fecha sin cargar las fotos.
        const pedido = almacen.index(INDICE_FECHA).openKeyCursor(vencidos);
        pedido.onsuccess = () => {
          const cursor = pedido.result;
          if (!cursor) return;
          almacen.delete(cursor.primaryKey);
          cursor.continue();
        };
      }
      return sinResultado;
    });
  } catch {
    // no-op
  }
}
