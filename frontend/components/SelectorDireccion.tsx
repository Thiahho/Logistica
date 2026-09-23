"use client";

import { useEffect, useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SelectorLocalidad, type LocalidadConocida } from "@/components/SelectorLocalidad";
import { CampoLinkMapa } from "@/components/CampoLinkMapa";
import { Button } from "@/components/ui/button";
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
  urlMapaAplicada?: boolean;
  urlMapaError?: string | null;
}

interface UbicacionResuelta {
  id: number;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
  urlMapaAplicada: boolean;
  urlMapaError: string | null;
}

/** Respuesta de POST .../ubicaciones/desde-mapa: dirección y localidad propuestas desde el link. */
interface DireccionDesdeMapa {
  lat: number | null;
  lng: number | null;
  calleNumero: string | null;
  localidadId: number | null;
  localidadNombre: string | null;
  partido: string | null;
  error: string | null;
}

interface SelectorDireccionProps {
  /** Estado inicial al montar (reabrir un armado ya guardado). No es controlado: el componente
   * es dueño de calle/localidad, igual que pedidos/nuevo lo es hoy — un cambio posterior de este
   * prop no se refleja (remontar con una `key` distinta si hace falta forzarlo). */
  inicial?: DireccionResuelta | null;
  /** Se llama con la ubicación resuelta, o con null en cuanto el texto deja de coincidir con lo
   * resuelto (desde el onChange del input, NO desde un efecto: evita set-state-in-effect en el padre). */
  onCambio: (direccion: DireccionResuelta | null) => void;
  /** Se llama en cuanto se elige (o limpia) la localidad, sin esperar a que la calle esté
   * geocodificada — para mostrar el precio sugerido por zona apenas se conoce la localidad. */
  onLocalidadCambio?: (localidadId: number | null) => void;
  idPrefijo?: string;
  disabled?: boolean;
  /** B5: /mis-envios/nuevo pasa "/api/mi-cuenta" (ver SelectorLocalidad.basePath) — el rol
   * 'cliente' no puede llegar a /api/ubicaciones ni /api/localidades (BackOffice de clase). */
  basePath?: string;
  /** Muestra el campo opcional "Link de Google Maps": el servidor toma el punto exacto del link en
   * vez de geocodificar por calle. Apagado por defecto (depósitos, rutas). */
  permitirLinkMapa?: boolean;
}

/**
 * Combobox de localidad + Input de calle + geocodificación con debounce, extraído de
 * pedidos/nuevo/page.tsx (mismo patrón: la ubicación resuelta queda atada a (calle, localidad) y
 * su vigencia se deriva, nunca se resetea en un efecto). Reusado por el selector de punto de
 * partida de una ruta (acta changelog 3.6). pedidos/nuevo NO se migró a este componente todavía
 * — aplicarSugerencia escribe ese estado desde afuera y necesitaría una variante controlada.
 */
export function SelectorDireccion({
  inicial, onCambio, onLocalidadCambio, idPrefijo = "direccion", disabled, basePath = "/api", permitirLinkMapa = false,
}: SelectorDireccionProps) {
  const { fetchConSesion } = useAuth();

  const [calleNumero, setCalleNumero] = useState(inicial?.calleNumero ?? "");
  const [localidadId, setLocalidadId] = useState<number | null>(inicial?.localidadId ?? null);
  const [localidadConocida, setLocalidadConocida] = useState<LocalidadConocida | null>(
    inicial ? { id: inicial.localidadId, nombre: inicial.localidadNombre ?? "", partido: null, zonaId: null } : null,
  );
  const [urlMapa, setUrlMapa] = useState("");
  const [ubicacionResuelta, setUbicacionResuelta] = useState<{
    calleNumero: string;
    localidadId: number;
    urlMapa: string;
    direccion: DireccionResuelta;
  } | null>(inicial ? { calleNumero: inicial.calleNumero, localidadId: inicial.localidadId, urlMapa: "", direccion: inicial } : null);
  const [geocodificando, setGeocodificando] = useState(false);
  const [errorUbicacion, setErrorUbicacion] = useState<string | null>(null);
  // Lectura del link: el servidor propone calle y localidad, y hasta que la persona las confirme (o
  // las edite) la dirección no queda resuelta — un pin equivocado no puede cargarse sin que nadie lo vea.
  const [leyendoLink, setLeyendoLink] = useState(false);
  const [errorLink, setErrorLink] = useState<string | null>(null);
  const [urlLeida, setUrlLeida] = useState("");
  const [porConfirmar, setPorConfirmar] = useState(false);

  const calleTrim = calleNumero.trim();
  // Sin el campo visible, el link no cuenta (aunque quedara un valor de antes).
  const urlTrim = permitirLinkMapa ? urlMapa.trim() : "";
  const direccionVigente =
    ubicacionResuelta &&
    ubicacionResuelta.calleNumero === calleTrim &&
    ubicacionResuelta.localidadId === localidadId &&
    ubicacionResuelta.urlMapa === urlTrim
      ? ubicacionResuelta.direccion
      : null;

  function actualizarCalle(valor: string) {
    setCalleNumero(valor);
    setPorConfirmar(false); // corregir a mano es verificar
    const trim = valor.trim();
    const sigueVigente =
      ubicacionResuelta?.calleNumero === trim && ubicacionResuelta?.localidadId === localidadId && ubicacionResuelta?.urlMapa === urlTrim;
    if (!sigueVigente) onCambio(null);
  }

  function actualizarUrlMapa(valor: string) {
    setUrlMapa(valor);
    setPorConfirmar(false);
    setErrorLink(null);
    const nueva = valor.trim();
    const sigueVigente =
      ubicacionResuelta?.calleNumero === calleTrim && ubicacionResuelta?.localidadId === localidadId && ubicacionResuelta?.urlMapa === nueva;
    if (!sigueVigente) onCambio(null);
  }

  function actualizarLocalidad(localidad: LocalidadConocida | null) {
    setPorConfirmar(false); // elegir otra localidad es verificar
    setLocalidadConocida(localidad);
    const nuevoId = localidad?.id ?? null;
    setLocalidadId(nuevoId);
    onLocalidadCambio?.(nuevoId);
    const sigueVigente =
      ubicacionResuelta?.calleNumero === calleTrim && ubicacionResuelta?.localidadId === nuevoId && ubicacionResuelta?.urlMapa === urlTrim;
    if (!sigueVigente) onCambio(null);
  }

  // Al pegar un link: se lee la dirección y la localidad del punto (una sola vez por link) y se
  // completan los campos para que la persona los revise. Sin setState síncrono en el cuerpo del efecto.
  useEffect(() => {
    if (!urlTrim || urlTrim === urlLeida) return;

    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setLeyendoLink(true);
      setErrorLink(null);
      try {
        const resp = await fetchConSesion(`${basePath}/ubicaciones/desde-mapa`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ urlMapa: urlTrim }),
          signal: abort.signal,
        });
        if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
        const d: DireccionDesdeMapa = await resp.json();
        setUrlLeida(urlTrim);
        if (d.error) {
          setErrorLink(d.error);
          return;
        }
        if (d.calleNumero) setCalleNumero(d.calleNumero);
        if (d.localidadId !== null && d.localidadNombre) {
          setLocalidadId(d.localidadId);
          setLocalidadConocida({ id: d.localidadId, nombre: d.localidadNombre, partido: d.partido, zonaId: null });
          onLocalidadCambio?.(d.localidadId);
        }
        setPorConfirmar(true);
        onCambio(null);
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setUrlLeida(urlTrim);
        setErrorLink(err instanceof Error ? err.message : "No se pudo leer el link.");
      } finally {
        if (!abort.signal.aborted) setLeyendoLink(false);
      }
    }, 700);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onCambio/onLocalidadCambio no son reactivos (callbacks estables del padre)
  }, [urlTrim, urlLeida, fetchConSesion, basePath]);

  // Mismo patrón que pedidos/nuevo: debounce 600ms (una dirección nueva puede pegarle a
  // Nominatim, rate limit ~1 req/s), sin setState síncrono en el cuerpo del efecto — todo pasa
  // por el callback asincrónico del setTimeout.
  useEffect(() => {
    if (!calleTrim || localidadId === null) return;
    // Un link recién leído espera la confirmación; y mientras se lee, no se adelanta la resolución.
    if (porConfirmar || leyendoLink || (urlTrim && urlTrim !== urlLeida)) return;
    if (
      ubicacionResuelta?.calleNumero === calleTrim &&
      ubicacionResuelta.localidadId === localidadId &&
      ubicacionResuelta.urlMapa === urlTrim
    )
      return;

    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setGeocodificando(true);
      setErrorUbicacion(null);
      try {
        const resp = await fetchConSesion(`${basePath}/ubicaciones`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ calleNumero: calleTrim, localidadId, referencia: null, urlMapa: urlTrim || null }),
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
          urlMapaAplicada: u.urlMapaAplicada,
          urlMapaError: u.urlMapaError,
        };
        setUbicacionResuelta({ calleNumero: calleTrim, localidadId, urlMapa: urlTrim, direccion });
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
  }, [calleTrim, localidadId, urlTrim, urlLeida, porConfirmar, leyendoLink, ubicacionResuelta, fetchConSesion, localidadConocida, basePath]);

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
          basePath={basePath}
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
        {direccionVigente && !geocodificando && !errorUbicacion && !direccionVigente.urlMapaAplicada && (
          <p className={`text-sm ${direccionDudosa ? "text-destructive" : "text-muted-foreground"}`}>
            {direccionDudosa
              ? "Dirección sin confirmar: se guarda igual, pero el recorrido puede degradar."
              : `Ubicación encontrada (confianza ${direccionVigente.geoConfianza}).`}
          </p>
        )}
      </div>
      {permitirLinkMapa && (
        <CampoLinkMapa
          id={`${idPrefijo}-mapa`}
          value={urlMapa}
          onChange={actualizarUrlMapa}
          disabled={disabled}
          aplicado={direccionVigente?.urlMapaAplicada}
          error={direccionVigente?.urlMapaError ?? errorLink}
          leyendo={leyendoLink}
        />
      )}
      {permitirLinkMapa && porConfirmar && (
        <div role="group" aria-label="Verificar dirección" className="flex flex-col gap-2 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm">
          <p className="font-medium text-amber-900">Verificá la dirección de tu link</p>
          <p>
            {calleTrim || <span className="text-destructive">Sin calle: escribila arriba</span>}
            {localidadConocida?.nombre ? `, ${localidadConocida.nombre}` : ""}
          </p>
          {calleTrim && !/\d/.test(calleTrim) && (
            <p className="text-xs text-amber-800">No detectamos la altura: agregala en «Dirección» si la sabés.</p>
          )}
          <p className="text-xs text-amber-800">
            Sacamos estos datos del punto del mapa y pueden no ser exactos. Corregí la calle o la localidad en los
            campos de arriba si hace falta.
          </p>
          <Button
            type="button"
            className="h-11"
            disabled={!calleTrim || localidadId === null}
            onClick={() => setPorConfirmar(false)}
          >
            Confirmar dirección
          </Button>
        </div>
      )}
    </div>
  );
}
