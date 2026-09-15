"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";

interface OpcionesSondeo {
  intervaloMs?: number;
  /** false para pausar del todo (ej. la ruta todavía no se conoce). */
  habilitado?: boolean;
  /** false: carga una vez y no programa más ciclos (ej. una ruta `planificada`/`cerrada`, donde
   * nada se mueve solo). Distinto de `habilitado=false`, que ni siquiera carga la primera vez. */
  repetir?: boolean;
}

/**
 * Primer polling del proyecto (jornada §9.3) — hasta acá, cero setInterval/refetchInterval en
 * todo el frontend; el resto de las pantallas se refresca a mano (`recargar()` tras una
 * mutación). Tres reglas, ninguna opcional:
 *
 *  1. Pausa mientras la pestaña está oculta (`document.hidden`) y refresca al instante al volver
 *     al foco — sin esto, una pestaña de /jornada olvidada pega ~180 requests/hora.
 *  2. `AbortController` por ciclo, mismo patrón que el debounce de recorrido en
 *     app/rutas/[id]/armar/page.tsx: un ciclo lento no puede pisar a uno más nuevo.
 *  3. Un error transitorio NO borra `datos` — se conserva lo último bueno, marcado por `error`
 *     como desactualizado. Distinto del `cargar()`/`setErrorCarga` de las páginas de detalle, que
 *     asumen una sola carga y sí pueden vaciar todo al fallar.
 *
 * `cargar` encadena `.then()/.catch()/.finally()` (no async/await) a propósito, mismo estilo que
 * useListadoPaginado y el resto de las páginas de detalle: los `setState` quedan dentro de
 * callbacks, nunca en el cuerpo síncrono del efecto que la dispara (react-hooks/set-state-in-effect).
 */
export function useSondeo<T>(
  ruta: string,
  { intervaloMs = 20000, habilitado = true, repetir = true }: OpcionesSondeo = {},
) {
  const { fetchConSesion } = useAuth();
  const [datos, setDatos] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actualizadoEn, setActualizadoEn] = useState<Date | null>(null);
  const [cargando, setCargando] = useState(true);

  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // `ruta` cambia de recurso (ej. el selector de fecha de /jornada) — a diferencia de un ciclo de
  // sondeo más sobre el mismo recurso, ahí sí hay que soltar lo anterior en vez de mostrarlo
  // "desactualizado" mientras llega lo nuevo. Ajustado durante el render (no en un efecto), el
  // patrón que documenta React para "resetear estado cuando cambia una prop": el valor anterior
  // se guarda en estado, no en un ref (un ref no puede leerse ni escribirse durante el render).
  const [rutaAnterior, setRutaAnterior] = useState(ruta);
  if (rutaAnterior !== ruta) {
    setRutaAnterior(ruta);
    setDatos(null);
    setError(null);
    setCargando(true);
  }

  const cargar = useCallback(() => {
    if (!habilitado || (typeof document !== "undefined" && document.hidden)) return;

    abortRef.current?.abort();
    const abort = new AbortController();
    abortRef.current = abort;

    fetchConSesion(ruta, { signal: abort.signal })
      .then((resp) => leerJson<T>(resp))
      .then((json) => {
        setDatos(json);
        setError(null);
        setActualizadoEn(new Date());
      })
      .catch((err) => {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setError(err instanceof Error ? err.message : "No se pudo actualizar.");
      })
      .finally(() => setCargando(false));
  }, [fetchConSesion, ruta, habilitado]);

  useEffect(() => {
    if (!habilitado) return;
    cargar();
    if (!repetir) return () => abortRef.current?.abort();

    function programar() {
      timeoutRef.current = setTimeout(() => {
        cargar();
        programar();
      }, intervaloMs);
    }
    programar();

    function onVisibilidad() {
      if (!document.hidden) {
        if (timeoutRef.current) clearTimeout(timeoutRef.current);
        cargar();
        programar();
      }
    }
    document.addEventListener("visibilitychange", onVisibilidad);

    return () => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
      document.removeEventListener("visibilitychange", onVisibilidad);
      abortRef.current?.abort();
    };
  }, [cargar, intervaloMs, habilitado, repetir]);

  return { datos, error, actualizadoEn, cargando, recargar: cargar };
}
