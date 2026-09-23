"use client";

import { useEffect, useMemo, useState } from "react";
import { ComboboxBusqueda, type OpcionCombobox } from "@/components/ComboboxBusqueda";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerError, leerJson } from "@/lib/api/errores";

export interface LocalidadConocida {
  id: number;
  nombre: string;
  partido: string | null;
  zonaId: number | null;
}

interface SugerenciaLocalidad {
  nombre: string;
  partido: string | null;
}

interface ResultadoBusquedaLocalidad {
  existentes: LocalidadConocida[];
  sugeridas: SugerenciaLocalidad[];
}

interface SelectorLocalidadProps {
  value: number | null;
  onValueChange: (localidad: LocalidadConocida | null) => void;
  /** La localidad ya elegida, para que se vea su nombre sin depender de una búsqueda (reabrir un
   * armado guardado, o aplicar una sugerencia de destinatario frecuente que ya trae el id). */
  conocida?: LocalidadConocida | null;
  placeholder?: string;
  id?: string;
  disabled?: boolean;
  /** B5 (diseño_b5_portal_carga.md §4): el rol 'cliente' no puede llegar a
   * /api/localidades (BackOffice de clase) — /mis-envios/nuevo pasa "/api/mi-cuenta", el espejo
   * de estas mismas dos acciones que expone MiCuentaController. Default sin cambios para el resto
   * de los llamadores. */
  basePath?: string;
}

const PREFIJO_EXISTENTE = "e:";
const PREFIJO_SUGERIDA = "s:";
const RESULTADO_VACIO: ResultadoBusquedaLocalidad = { existentes: [], sugeridas: [] };

/**
 * Combobox de localidad que busca contra el catálogo propio y, si este se queda corto, también
 * contra OSM (`GET /api/localidades/buscar`) — el catálogo hoy solo tiene lo que alguien cargó a
 * mano (5 filas), y una dirección real en cualquier otro lado no tenía forma de entrar. Elegir una
 * sugerencia la da de alta sola (`POST /api/localidades`, acta changelog 3.9) y el servidor le
 * asigna la zona midiendo su distancia al depósito contra los rangos de km de /tarifas (changelog
 * 4.11). Si no se pudo medir o ningún rango la cubre, queda sin zona y no cotiza hasta que
 * administración se la asigne a mano.
 *
 * Server-driven (mismo `ComboboxBusqueda` que usa el resto del sistema, con `textoBusqueda`, el
 * modo que su propio docblock ya preveía): a diferencia del filtrado en memoria, acá el input queda
 * controlado, así que este componente sincroniza el texto mostrado a mano en cada selección — no
 * hay array local completo del que la librería pueda derivarlo sola.
 */
export function SelectorLocalidad({
  value,
  onValueChange,
  conocida,
  placeholder = "Buscar localidad…",
  id,
  disabled,
  basePath = "/api",
}: SelectorLocalidadProps) {
  const { fetchConSesion } = useAuth();

  const [texto, setTexto] = useState(conocida?.nombre ?? "");
  const [resultadoBusqueda, setResultadoBusqueda] = useState<ResultadoBusquedaLocalidad>(RESULTADO_VACIO);
  const [buscando, setBuscando] = useState(false);
  const [creando, setCreando] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conocidas, setConocidas] = useState<Record<number, LocalidadConocida>>(
    conocida ? { [conocida.id]: conocida } : {},
  );

  // Sincroniza el texto visible con una localidad asignada desde afuera (reabrir un armado ya
  // guardado, o aplicar una sugerencia de destinatario frecuente) — patrón "ajustar estado cuando
  // cambia una prop" (react.dev), en el cuerpo del render y no en un efecto: evita el
  // set-state-in-effect y el reflow extra de un efecto que solo espeja una prop.
  const [ultimaConocidaId, setUltimaConocidaId] = useState<number | null>(conocida?.id ?? null);
  if (conocida && conocida.id !== ultimaConocidaId) {
    setUltimaConocidaId(conocida.id);
    setTexto(conocida.nombre);
    setConocidas((c) => ({ ...c, [conocida.id]: conocida }));
  }

  // Igual que en la resolución de dirección: la rama de "muy corto para buscar" no limpia el
  // resultado de forma síncrona en el efecto — `resultado`, más abajo, cae solo a RESULTADO_VACIO.
  useEffect(() => {
    const q = texto.trim();
    if (q.length < 2) return;
    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setBuscando(true);
      setError(null);
      try {
        const resp = await fetchConSesion(`${basePath}/localidades/buscar?q=${encodeURIComponent(q)}`, {
          signal: abort.signal,
        });
        const r = await leerJson<ResultadoBusquedaLocalidad>(resp);
        setResultadoBusqueda(r);
        setConocidas((c) => {
          const copia = { ...c };
          for (const l of r.existentes) copia[l.id] = l;
          return copia;
        });
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setError(err instanceof Error ? err.message : "No se pudo buscar la localidad.");
      } finally {
        if (!abort.signal.aborted) setBuscando(false);
      }
    }, 400);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
  }, [texto, fetchConSesion, basePath]);

  const resultado = texto.trim().length < 2 ? RESULTADO_VACIO : resultadoBusqueda;

  const items: OpcionCombobox[] = useMemo(() => {
    const existentesItems = resultado.existentes.map((l) => ({
      value: `${PREFIJO_EXISTENTE}${l.id}`,
      label: l.nombre,
      detalle: l.partido ?? undefined,
    }));
    const sugeridasItems = resultado.sugeridas.map((s) => ({
      value: `${PREFIJO_SUGERIDA}${s.nombre}|${s.partido ?? ""}`,
      label: `${s.nombre} (nueva)`,
      detalle: s.partido ?? "Sugerida por dirección",
    }));
    if (value !== null && !existentesItems.some((i) => i.value === `${PREFIJO_EXISTENTE}${value}`)) {
      const c = conocidas[value];
      if (c) existentesItems.unshift({ value: `${PREFIJO_EXISTENTE}${c.id}`, label: c.nombre, detalle: c.partido ?? undefined });
    }
    return [...existentesItems, ...sugeridasItems];
  }, [resultado, value, conocidas]);

  async function onSeleccionar(v: string | null) {
    if (v === null) {
      onValueChange(null);
      return;
    }
    if (v.startsWith(PREFIJO_EXISTENTE)) {
      const idElegido = Number(v.slice(PREFIJO_EXISTENTE.length));
      const c = conocidas[idElegido] ?? resultado.existentes.find((l) => l.id === idElegido) ?? null;
      if (c) {
        setTexto(c.nombre);
        onValueChange(c);
      }
      return;
    }
    const [nombre, partido] = v.slice(PREFIJO_SUGERIDA.length).split("|");
    setCreando(nombre);
    setError(null);
    try {
      const resp = await fetchConSesion(`${basePath}/localidades`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ nombre, partido: partido || null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const nueva = await leerJson<LocalidadConocida>(resp);
      setConocidas((c) => ({ ...c, [nueva.id]: nueva }));
      setTexto(nueva.nombre);
      onValueChange(nueva);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear la localidad.");
    } finally {
      setCreando(null);
    }
  }

  const valorCombobox = value !== null ? `${PREFIJO_EXISTENTE}${value}` : null;

  return (
    <div className="flex flex-col gap-1">
      <ComboboxBusqueda
        id={id}
        items={items}
        value={valorCombobox}
        onValueChange={onSeleccionar}
        textoBusqueda={texto}
        onTextoBusquedaChange={setTexto}
        cargando={buscando || creando !== null}
        placeholder={placeholder}
        mensajeVacio={texto.trim().length < 2 ? "Escribí al menos 2 letras…" : "Sin coincidencias."}
        disabled={disabled}
      />
      {creando && <p className="text-xs text-muted-foreground">Agregando {creando} al catálogo…</p>}
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  );
}
