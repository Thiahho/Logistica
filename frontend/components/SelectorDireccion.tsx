"use client";

import { useEffect, useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SelectorLocalidad, type LocalidadConocida } from "@/components/SelectorLocalidad";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerError } from "@/lib/api/errores";

export interface DireccionResuelta {
  ubicacionId: number;
  calleNumero: string;
  localidadId: number;
  localidadNombre: string | null;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
}

interface UbicacionResuelta {
  id: number;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
}

interface SelectorDireccionProps {
  /** Estado inicial al montar (reabrir un armado ya guardado). No es controlado: el componente
   * es dueño de calle/localidad, igual que pedidos/nuevo lo es hoy — un cambio posterior de este
   * prop no se refleja (remontar con una `key` distinta si hace falta forzarlo). */
  inicial?: DireccionResuelta | null;
  /** Se llama con la ubicación resuelta, o con null en cuanto el texto deja de coincidir con lo
   * resuelto (desde el onChange del input, NO desde un efecto: evita set-state-in-effect en el padre). */
  onCambio: (direccion: DireccionResuelta | null) => void;
  idPrefijo?: string;
  disabled?: boolean;
}

/**
 * Combobox de localidad + Input de calle + geocodificación con debounce, extraído de
 * pedidos/nuevo/page.tsx (mismo patrón: la ubicación resuelta queda atada a (calle, localidad) y
 * su vigencia se deriva, nunca se resetea en un efecto). Reusado por el selector de punto de
 * partida de una ruta (acta changelog 3.6). pedidos/nuevo NO se migró a este componente todavía
 * — aplicarSugerencia escribe ese estado desde afuera y necesitaría una variante controlada.
 */
export function SelectorDireccion({ inicial, onCambio, idPrefijo = "direccion", disabled }: SelectorDireccionProps) {
  const { fetchConSesion } = useAuth();

  const [calleNumero, setCalleNumero] = useState(inicial?.calleNumero ?? "");
  const [localidadId, setLocalidadId] = useState<number | null>(inicial?.localidadId ?? null);
  const [localidadConocida, setLocalidadConocida] = useState<LocalidadConocida | null>(
    inicial ? { id: inicial.localidadId, nombre: inicial.localidadNombre ?? "", partido: null, zonaId: null } : null,
  );
  const [ubicacionResuelta, setUbicacionResuelta] = useState<{
    calleNumero: string;
    localidadId: number;
    direccion: DireccionResuelta;
  } | null>(inicial ? { calleNumero: inicial.calleNumero, localidadId: inicial.localidadId, direccion: inicial } : null);
  const [geocodificando, setGeocodificando] = useState(false);
  const [errorUbicacion, setErrorUbicacion] = useState<string | null>(null);

  const calleTrim = calleNumero.trim();
  const direccionVigente =
    ubicacionResuelta && ubicacionResuelta.calleNumero === calleTrim && ubicacionResuelta.localidadId === localidadId
      ? ubicacionResuelta.direccion
      : null;

  function actualizarCalle(valor: string) {
    setCalleNumero(valor);
    const trim = valor.trim();
    const sigueVigente = ubicacionResuelta?.calleNumero === trim && ubicacionResuelta?.localidadId === localidadId;
    if (!sigueVigente) onCambio(null);
  }

  function actualizarLocalidad(localidad: LocalidadConocida | null) {
    setLocalidadConocida(localidad);
    const nuevoId = localidad?.id ?? null;
    setLocalidadId(nuevoId);
    const sigueVigente = ubicacionResuelta?.calleNumero === calleTrim && ubicacionResuelta?.localidadId === nuevoId;
    if (!sigueVigente) onCambio(null);
  }

  // Mismo patrón que pedidos/nuevo: debounce 600ms (una dirección nueva puede pegarle a
  // Nominatim, rate limit ~1 req/s), sin setState síncrono en el cuerpo del efecto — todo pasa
  // por el callback asincrónico del setTimeout.
  useEffect(() => {
    if (!calleTrim || localidadId === null) return;
    if (ubicacionResuelta?.calleNumero === calleTrim && ubicacionResuelta.localidadId === localidadId) return;

    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setGeocodificando(true);
      setErrorUbicacion(null);
      try {
        const resp = await fetchConSesion("/api/ubicaciones", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ calleNumero: calleTrim, localidadId, referencia: null }),
          signal: abort.signal,
        });
        if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
        const u: UbicacionResuelta = await resp.json();
        const direccion: DireccionResuelta = {
          ubicacionId: u.id,
          calleNumero: calleTrim,
          localidadId,
          localidadNombre: localidadConocida?.nombre ?? null,
          lat: u.lat,
          lng: u.lng,
          geoConfianza: u.geoConfianza,
        };
        setUbicacionResuelta({ calleNumero: calleTrim, localidadId, direccion });
        onCambio(direccion);
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setErrorUbicacion(err instanceof Error ? err.message : "No se pudo geocodificar la dirección.");
      } finally {
        if (!abort.signal.aborted) setGeocodificando(false);
      }
    }, 600);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onCambio no es reactivo (callback estable del padre)
  }, [calleTrim, localidadId, ubicacionResuelta, fetchConSesion, localidadConocida]);

  const direccionDudosa =
    direccionVigente !== null && direccionVigente.geoConfianza !== "alta" && direccionVigente.geoConfianza !== "media";

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor={`${idPrefijo}-localidad`}>Localidad</Label>
        <SelectorLocalidad
          id={`${idPrefijo}-localidad`}
          value={localidadId}
          onValueChange={actualizarLocalidad}
          conocida={localidadConocida}
          placeholder="Buscar localidad…"
          disabled={disabled}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor={`${idPrefijo}-calle`}>Dirección</Label>
        <Input
          id={`${idPrefijo}-calle`}
          value={calleNumero}
          onChange={(e) => actualizarCalle(e.target.value)}
          placeholder="Calle y número"
          disabled={disabled}
        />
        {geocodificando && <p className="text-sm text-muted-foreground">Geocodificando…</p>}
        {errorUbicacion && !geocodificando && <p className="text-sm text-destructive">{errorUbicacion}</p>}
        {direccionVigente && !geocodificando && !errorUbicacion && (
          <p className={`text-sm ${direccionDudosa ? "text-destructive" : "text-muted-foreground"}`}>
            {direccionDudosa
              ? "Dirección sin confirmar: se guarda igual, pero el recorrido puede degradar."
              : `Ubicación encontrada (confianza ${direccionVigente.geoConfianza}).`}
          </p>
        )}
      </div>
    </div>
  );
}
