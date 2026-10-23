/** Cuánto se espera la respuesta de un pedido común antes de darlo por colgado. Las respuestas de la
 * API son de menos de un segundo; el margen es para una señal mala o un backend que está despertando. */
export const LIMITE_PEDIDO_MS = 30_000;

/** Lo mismo para un pedido que sube un archivo (la foto de la entrega, un comprobante, una planilla):
 * con mala señal la subida sola puede llevar más que el límite común. */
export const LIMITE_SUBIDA_MS = 120_000;

/** Lo mismo para una operación que el servidor tarda en resolver aunque la señal sea buena (el cierre de
 * facturación, cerrar la planificación con sus consultas de distancia, un exportable, una importación).
 * Mismo valor que la subida: no hay medición que justifique otro. */
export const LIMITE_OPERACION_LARGA_MS = 120_000;

/** `RequestInit` más el límite de espera. Sin `limiteMs` se usa el común, o el de subida si el cuerpo
 * lleva un archivo. */
export type OpcionesDePedido = RequestInit & { limiteMs?: number };

/**
 * El servidor no respondió dentro del límite. Es un error aparte para que una pantalla pueda decir
 * "la conexión está lenta, probá de nuevo" en vez de un fallo genérico; las que solo muestran
 * `err.message` ya muestran este texto.
 */
export class ErrorDeTiempoAgotado extends Error {
  constructor() {
    super("La conexión está lenta y el servidor no respondió a tiempo. Probá de nuevo.");
    this.name = "ErrorDeTiempoAgotado";
  }
}

/** Sin respuesta del servidor: se venció el límite o `fetch` no llegó a conectar (sin señal, DNS, etc.,
 * que el navegador informa como `TypeError`). Un 4xx/5xx no entra acá: eso sí es una respuesta. */
export function esFallaDeConexion(err: unknown): boolean {
  return err instanceof ErrorDeTiempoAgotado || err instanceof TypeError;
}

export function limitePorDefecto(cuerpo: RequestInit["body"]): number {
  const llevaArchivo =
    (typeof FormData !== "undefined" && cuerpo instanceof FormData) ||
    (typeof Blob !== "undefined" && cuerpo instanceof Blob);
  return llevaArchivo ? LIMITE_SUBIDA_MS : LIMITE_PEDIDO_MS;
}

/**
 * `fetch` con límite de espera hasta que llegan las cabeceras de la respuesta. Leer el cuerpo queda
 * fuera del límite a propósito: una descarga grande (un exportable) no se corta por la mitad.
 *
 * La señal de quien llama sigue funcionando y se distingue del límite: si aborta quien llama, sale el
 * mismo `AbortError` de siempre; si se vence el reloj, sale `ErrorDeTiempoAgotado`.
 */
export async function fetchConLimite(url: string, init: RequestInit, limiteMs: number): Promise<Response> {
  const control = new AbortController();
  const externa = init.signal ?? null;
  const abortarPorExterna = () => control.abort(externa?.reason);
  if (externa?.aborted) abortarPorExterna();
  else externa?.addEventListener("abort", abortarPorExterna, { once: true });

  let vencido = false;
  const reloj = setTimeout(() => {
    vencido = true;
    control.abort();
  }, limiteMs);

  try {
    return await fetch(url, { ...init, signal: control.signal });
  } catch (err) {
    if (vencido && !externa?.aborted) throw new ErrorDeTiempoAgotado();
    throw err;
  } finally {
    clearTimeout(reloj);
    externa?.removeEventListener("abort", abortarPorExterna);
  }
}

/** Espera una promesa hasta el límite sin cancelarla: quien la comparte la sigue viendo terminar. */
export function esperarConLimite<T>(promesa: Promise<T>, limiteMs: number): Promise<T> {
  let reloj: ReturnType<typeof setTimeout> | undefined;
  const vencimiento = new Promise<never>((_, reject) => {
    reloj = setTimeout(() => reject(new ErrorDeTiempoAgotado()), limiteMs);
  });
  return Promise.race([promesa, vencimiento]).finally(() => clearTimeout(reloj));
}
