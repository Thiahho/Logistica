export interface ErrorApi {
  status: number;
  mensaje: string;
}

/**
 * Backend .NET: los errores de reglas de negocio (triggers, checks — ManejadorExcepciones.cs)
 * llegan como ProblemDetails (RFC 7807), con el mensaje en `detail`. Si el cuerpo no es JSON
 * válido (un 401/403 sin cuerpo, por ejemplo), cae al texto plano.
 */
export async function leerError(resp: Response): Promise<ErrorApi> {
  const texto = await resp.text();
  try {
    const problema = JSON.parse(texto);
    return { status: resp.status, mensaje: problema.detail ?? problema.title ?? texto };
  } catch {
    return { status: resp.status, mensaje: texto || `Error ${resp.status}` };
  }
}

/**
 * Reemplazo de `.then(r => r.json())` para GETs de listado: sin esto, una respuesta no-2xx
 * (401 tras fallar el refresh, 500, etc.) se parseaba igual como si fuera el array/objeto
 * esperado — pantalla colgada en "Cargando…" o `.map` explotando sobre un ProblemDetails.
 */
export async function leerJson<T>(resp: Response): Promise<T> {
  if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
  return resp.json() as Promise<T>;
}
