/**
 * Borrador local de la captura de una entrega: lo que el repartidor ya cargó (receptor, DNI, foto)
 * sobrevive a una recarga o a que el teléfono cierre la pestaña mientras no hay señal. No es una cola
 * offline: nada se envía solo, el repartidor vuelve a tocar "Confirmar entrega".
 *
 * Vive en IndexedDB y no en localStorage porque la foto pesa varios MB. Los campos y la foto van en
 * almacenes separados para no reescribir la foto en cada tecla.
 *
 * Todo se guarda cifrado (AES-GCM de WebCrypto): en el teléfono no queda legible ni el nombre, ni el
 * DNI, ni la foto, ni el nombre o el tipo del archivo. La llave no está en el teléfono: la entrega el
 * servidor a la sesión (/api/auth/clave-borradores) y AuthProvider la tiene solo en memoria, sin
 * posibilidad de exportarla. Sin sesión, lo guardado no se puede leer. Cada registro lleva un IV
 * propio y queda atado a su almacén, a su clave (usuario y parada) y a su fecha: copiado a otra
 * parada o a otro usuario, o con la fecha cambiada para estirarle la vida, no descifra.
 *
 * No hay camino sin cifrar: sin llave o sin WebCrypto (un navegador viejo, o una página servida por
 * http) no se guarda ni se recupera nada. Y nada de acá lanza: sin IndexedDB (navegación privada,
 * cuota llena) la pantalla sigue andando igual, solo que sin borrador.
 */

const BASE = "logistica-borradores";
// 2: los registros pasan a guardarse cifrados. La versión 1 los guardaba legibles.
const VERSION = 2;
const ALMACEN_CAMPOS = "captura-parada-campos";
const ALMACEN_FOTOS = "captura-parada-fotos";
const ALMACENES = [ALMACEN_CAMPOS, ALMACEN_FOTOS];
const INDICE_FECHA = "guardadoEn";
const ALGORITMO = "AES-GCM";
const BYTES_IV = 12;
// La foto cifrada empieza con este largo (4 bytes) y, a continuación, su nombre y su tipo en JSON.
const BYTES_LARGO_CABECERA = 4;

/** Un borrador es de la jornada: las paradas del día siguiente son otras. Pasado un día se borra,
 * para no dejar nombres, DNI y fotos de terceros guardados en el teléfono, ni siquiera cifrados. */
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

/** Lo único que queda escrito en el teléfono, igual en los dos almacenes. `clave` y `guardadoEn` van
 * a la vista porque IndexedDB los necesita para buscar y para purgar por fecha. */
interface RegistroCifrado {
  clave: string;
  guardadoEn: number;
  iv: Uint8Array<ArrayBuffer>;
  cifrado: ArrayBuffer;
}

interface CabeceraDeFoto {
  nombre: string;
  tipo: string;
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

/** Si este navegador puede cifrar. `crypto.subtle` no existe fuera de un contexto seguro (https o localhost). */
export function hayCifrado(): boolean {
  return typeof crypto !== "undefined" && !!crypto.subtle;
}

function abrir(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const pedido = indexedDB.open(BASE, VERSION);
    pedido.onupgradeneeded = () => {
      // Lo que hubiera es de un esquema anterior, que guardaba los datos legibles: se destruye.
      for (const nombre of Array.from(pedido.result.objectStoreNames)) pedido.result.deleteObjectStore(nombre);
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

// Cifrar es asíncrono y pasa antes de abrir la transacción (IndexedDB la cierra si se espera otra
// cosa en el medio), así que dos operaciones seguidas podrían llegar al disco al revés: una foto de
// varios MB terminaría de cifrarse después del "descartar" que vino atrás y lo desharía. Por eso las
// operaciones sobre los borradores corren de a una, en el orden en que se pidieron.
let cola: Promise<unknown> = Promise.resolve();

function enOrden<T>(tarea: () => Promise<T>): Promise<T> {
  const turno = cola.then(tarea, tarea);
  cola = turno.catch(sinResultado);
  return turno;
}

/** Lo que el cifrado autentica además del contenido: un registro movido de almacén, copiado bajo la
 * clave de otro usuario u otra parada, o con la fecha cambiada, deja de descifrar. */
function datosAsociados(almacen: string, clave: string, guardadoEn: number): Uint8Array<ArrayBuffer> {
  return new TextEncoder().encode(JSON.stringify([almacen, clave, guardadoEn]));
}

async function cifrar(
  llave: CryptoKey,
  almacen: string,
  clave: string,
  guardadoEn: number,
  contenido: Uint8Array<ArrayBuffer>,
): Promise<RegistroCifrado> {
  // Un IV nuevo en cada escritura: AES-GCM se rompe si una misma llave repite IV.
  const iv = crypto.getRandomValues(new Uint8Array(BYTES_IV));
  const cifrado = await crypto.subtle.encrypt(
    { name: ALGORITMO, iv, additionalData: datosAsociados(almacen, clave, guardadoEn) },
    llave,
    contenido,
  );
  return { clave, guardadoEn, iv, cifrado };
}

/** Rechaza si el registro no es de esta llave, fue tocado o viene de otro formato. */
async function descifrar(llave: CryptoKey, almacen: string, registro: RegistroCifrado): Promise<Uint8Array<ArrayBuffer>> {
  const contenido = await crypto.subtle.decrypt(
    { name: ALGORITMO, iv: registro.iv, additionalData: datosAsociados(almacen, registro.clave, registro.guardadoEn) },
    llave,
    registro.cifrado,
  );
  return new Uint8Array(contenido);
}

async function descifrarCampos(llave: CryptoKey, registro: RegistroCifrado): Promise<CamposDeCaptura> {
  const leido: Partial<CamposDeCaptura> = JSON.parse(
    new TextDecoder().decode(await descifrar(llave, ALMACEN_CAMPOS, registro)),
  );
  return {
    receptorNombre: String(leido.receptorNombre ?? ""),
    documento: String(leido.documento ?? ""),
    sinDocumento: leido.sinDocumento === true,
    sinDocumentoMotivo: String(leido.sinDocumentoMotivo ?? ""),
  };
}

/** El nombre y el tipo del archivo viajan adentro del cifrado, delante de los bytes de la foto. */
async function empaquetarFoto(foto: File): Promise<Uint8Array<ArrayBuffer>> {
  const cabecera = new TextEncoder().encode(
    JSON.stringify({ nombre: foto.name, tipo: foto.type } satisfies CabeceraDeFoto),
  );
  const bytes = new Uint8Array(await foto.arrayBuffer());
  const paquete = new Uint8Array(BYTES_LARGO_CABECERA + cabecera.length + bytes.length);
  new DataView(paquete.buffer).setUint32(0, cabecera.length);
  paquete.set(cabecera, BYTES_LARGO_CABECERA);
  paquete.set(bytes, BYTES_LARGO_CABECERA + cabecera.length);
  return paquete;
}

async function descifrarFoto(llave: CryptoKey, registro: RegistroCifrado): Promise<File> {
  const paquete = await descifrar(llave, ALMACEN_FOTOS, registro);
  const finDeCabecera = BYTES_LARGO_CABECERA + new DataView(paquete.buffer).getUint32(0);
  const cabecera: Partial<CabeceraDeFoto> = JSON.parse(
    new TextDecoder().decode(paquete.subarray(BYTES_LARGO_CABECERA, finDeCabecera)),
  );
  return new File([paquete.subarray(finDeCabecera)], String(cabecera.nombre ?? "foto"), {
    type: String(cabecera.tipo ?? ""),
  });
}

/** Descifra un registro vigente. Devuelve null si no hay o está vencido, y "ilegible" si está pero no
 * se pudo descifrar (otra llave, tocado, formato viejo). */
async function abrirRegistro<T>(
  registro: RegistroCifrado | undefined,
  ahora: number,
  leer: (registro: RegistroCifrado) => Promise<T>,
): Promise<T | null | "ilegible"> {
  if (!registro || borradorVencido(registro.guardadoEn, ahora)) return null;
  try {
    return await leer(registro);
  } catch {
    return "ilegible";
  }
}

/** Lo guardado para esa clave, o null si no hay nada, está vencido, no se puede descifrar con esa
 * llave o el almacenamiento no anda. Un registro que no descifra no sirve más: se borra. */
export function leerBorrador(clave: string, llave: CryptoKey, ahora = Date.now()): Promise<BorradorDeCaptura | null> {
  return enOrden(async () => {
    try {
      if (!hayCifrado()) return null;
      const guardado = await enTransaccion("readonly", (tx) => {
        const pedidoCampos = tx.objectStore(ALMACEN_CAMPOS).get(clave);
        const pedidoFoto = tx.objectStore(ALMACEN_FOTOS).get(clave);
        return () => ({
          campos: pedidoCampos.result as RegistroCifrado | undefined,
          foto: pedidoFoto.result as RegistroCifrado | undefined,
        });
      });
      const campos = await abrirRegistro(guardado.campos, ahora, (r) => descifrarCampos(llave, r));
      const foto = await abrirRegistro(guardado.foto, ahora, (r) => descifrarFoto(llave, r));

      const ilegibles: string[] = [];
      if (campos === "ilegible") ilegibles.push(ALMACEN_CAMPOS);
      if (foto === "ilegible") ilegibles.push(ALMACEN_FOTOS);
      if (ilegibles.length > 0) {
        await enTransaccion("readwrite", (tx) => {
          for (const almacen of ilegibles) tx.objectStore(almacen).delete(clave);
          return sinResultado;
        });
      }

      const borrador = { campos: campos === "ilegible" ? null : campos, foto: foto === "ilegible" ? null : foto };
      return borrador.campos || borrador.foto ? borrador : null;
    } catch {
      return null;
    }
  });
}

/** Guarda los campos del formulario; si quedaron todos vacíos, borra el registro en vez de dejar uno en blanco. */
export function guardarCampos(clave: string, llave: CryptoKey, campos: CamposDeCaptura, ahora = Date.now()): Promise<void> {
  return enOrden(async () => {
    try {
      const registro =
        camposVacios(campos) || !hayCifrado()
          ? null
          : await cifrar(llave, ALMACEN_CAMPOS, clave, ahora, new TextEncoder().encode(JSON.stringify(campos)));
      await enTransaccion("readwrite", (tx) => {
        const almacen = tx.objectStore(ALMACEN_CAMPOS);
        // Sin poder cifrar tampoco queda lo anterior: mostraría al recargar campos que ya no son los cargados.
        if (!registro) almacen.delete(clave);
        else almacen.put(registro);
        return sinResultado;
      });
    } catch {
      // Sin almacenamiento o sin poder cifrar: la captura sigue solo en memoria, como antes.
    }
  });
}

/** Guarda la foto elegida (la original, sin comprimir: la compresión sigue siendo al enviar), o la borra con null. */
export function guardarFoto(clave: string, llave: CryptoKey, foto: File | null, ahora = Date.now()): Promise<void> {
  return enOrden(async () => {
    try {
      const registro =
        !foto || !hayCifrado() ? null : await cifrar(llave, ALMACEN_FOTOS, clave, ahora, await empaquetarFoto(foto));
      await enTransaccion("readwrite", (tx) => {
        const almacen = tx.objectStore(ALMACEN_FOTOS);
        if (!registro) almacen.delete(clave);
        else almacen.put(registro);
        return sinResultado;
      });
    } catch {
      // Cuota llena, sin almacenamiento o sin poder cifrar: la foto sigue solo en memoria, como antes.
    }
  });
}

/** Borrar no necesita la llave: se puede descartar un borrador aunque no haya con qué leerlo. */
export function descartarBorrador(clave: string): Promise<void> {
  return enOrden(async () => {
    try {
      await enTransaccion("readwrite", (tx) => {
        for (const nombre of ALMACENES) tx.objectStore(nombre).delete(clave);
        return sinResultado;
      });
    } catch {
      // no-op
    }
  });
}

/** Borra todos los borradores de este teléfono, de cualquier parada y de cualquier usuario: al cerrar
 * sesión o cuando entra otra persona, no queda nada de la anterior, ni cifrado. */
export function borrarTodosLosBorradores(): Promise<void> {
  return enOrden(async () => {
    try {
      await enTransaccion("readwrite", (tx) => {
        for (const nombre of ALMACENES) tx.objectStore(nombre).clear();
        return sinResultado;
      });
    } catch {
      // no-op
    }
  });
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
