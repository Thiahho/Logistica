import type { useAuth } from "@/lib/auth/AuthProvider";
import type { Punto } from "@/lib/dominio/geo";
import type { Recorrido } from "@/lib/dominio/tipos";
import { leerJson } from "./errores";

type FetchConSesion = ReturnType<typeof useAuth>["fetchConSesion"];

/**
 * POST /api/recorrido — solo para el planificador (armar ruta), que edita en caliente y está
 * online por definición (escritorio). El repartidor NO usa esto: su recorrido viaja ya trazado
 * dentro de GET /api/mis-paradas/dia (RNF-07, no puede depender de una llamada en vivo en la calle).
 *
 * Devuelve null ante cualquier fallo (red, abort, OSRM caído — el backend ya responde 204 en ese
 * caso): el mapa cae a línea recta entre los puntos, nunca rompe la pantalla de armado.
 */
export async function trazarRecorrido(
  fetchConSesion: FetchConSesion,
  puntos: Punto[],
  signal?: AbortSignal,
): Promise<Recorrido | null> {
  if (puntos.length < 2) return null;
  try {
    const resp = await fetchConSesion("/api/recorrido", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ puntos }),
      signal,
    });
    if (resp.status === 204) return null;
    return await leerJson<Recorrido>(resp);
  } catch {
    return null;
  }
}
