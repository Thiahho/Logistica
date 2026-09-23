"use client";

import { useEffect, useState } from "react";
import {
  Autocomplete,
  AutocompleteContent,
  AutocompleteEmpty,
  AutocompleteInput,
  AutocompleteItem,
  AutocompleteList,
} from "@/components/ui/autocomplete";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";
import type { DestinatarioFrecuente } from "@/lib/dominio/tipos";

interface SugerenciaDestinatarioProps {
  clienteId: number | null;
  nombre: string;
  onNombreChange: (nombre: string) => void;
  onElegirSugerencia: (s: DestinatarioFrecuente) => void;
  id?: string;
  disabled?: boolean;
}

/**
 * Autocomplete (no Combobox: el valor es el texto libre tipeado, la lista solo sugiere — un
 * destinatario nuevo es el caso mayoritario). Sugiere destinatarios que este cliente ya usó,
 * leídos de GET /api/pedidos/destinatarios-frecuentes (acta changelog 3.5). Elegir uno prellena
 * también teléfono/localidad/calle en el formulario (ver onElegirSugerencia del padre).
 */
export function SugerenciaDestinatario({
  clienteId,
  nombre,
  onNombreChange,
  onElegirSugerencia,
  id,
  disabled,
}: SugerenciaDestinatarioProps) {
  const { fetchConSesion } = useAuth();
  const [sugerencias, setSugerencias] = useState<DestinatarioFrecuente[]>([]);
  const [cargando, setCargando] = useState(false);
  // A diferencia de Combobox (que tiene su propio trigger), Autocomplete no abre el popup solo
  // con foco — abre recién cuando el usuario tipea. Se controla `open` a mano para que la lista
  // de "últimos destinatarios" aparezca al enfocar el campo, sin tipear nada (ver más abajo).
  const [abierto, setAbierto] = useState(false);

  // Dispara también con nombre vacío (al elegir cliente / enfocar el campo): la lista de
  // "últimos destinatarios" tiene que aparecer antes de tipear nada, es la mayor parte del valor
  // de la feature. 300ms, más corto que el debounce de la cotización (400ms): acá la latencia
  // percibida mientras se tipea importa más y la query es barata (filtra por cliente_id primero).
  useEffect(() => {
    // Sin cliente elegido no hay nada que sugerir; se deja fuera del efecto (más abajo,
    // `sugerenciasVisibles`) para no resetear estado de forma síncrona dentro de un efecto.
    if (clienteId === null) return;
    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setCargando(true);
      try {
        const params = new URLSearchParams({ clienteId: String(clienteId) });
        if (nombre.trim()) params.set("q", nombre.trim());
        const resp = await fetchConSesion(`/api/pedidos/destinatarios-frecuentes?${params}`, {
          signal: abort.signal,
        });
        if (resp.ok) setSugerencias(await leerJson<DestinatarioFrecuente[]>(resp));
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setSugerencias([]);
      } finally {
        if (!abort.signal.aborted) setCargando(false);
      }
    }, 300);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
  }, [clienteId, nombre, fetchConSesion]);

  // Derivado, no reseteado en el efecto: sin cliente elegido no hay sugerencias válidas,
  // aunque quedaran algunas en memoria de una elección anterior.
  const sugerenciasVisibles = clienteId === null ? [] : sugerencias;

  return (
    <Autocomplete
      items={sugerenciasVisibles}
      value={nombre}
      onValueChange={onNombreChange}
      itemToStringValue={(s: DestinatarioFrecuente) => s.destinatarioNombre}
      filter={null}
      open={abierto}
      onOpenChange={setAbierto}
      disabled={disabled}
    >
      <AutocompleteInput
        id={id}
        placeholder="Nombre del destinatario"
        className="w-full"
        onFocus={() => setAbierto(true)}
      />
      <AutocompleteContent>
        {cargando && <p className="px-2 py-1.5 text-xs text-muted-foreground">Buscando…</p>}
        <AutocompleteEmpty>
          {clienteId === null ? "Elegí un cliente para ver destinatarios anteriores." : "Sin sugerencias."}
        </AutocompleteEmpty>
        <AutocompleteList>
          {(s: DestinatarioFrecuente) => (
            // `value={s}` es obligatorio, no decorativo: el click interno del primitive también
            // llama a onValueChange con itemToStringValue(value) para rellenar el input. Sin
            // esto, value quedaba null, itemToStringValue(null) resolvía a "" y pisaba —después
            // del setDestinatarioNombre de onElegirSugerencia— el nombre con string vacío,
            // mientras el resto de los campos (que no dependen de este mecanismo) sí quedaban bien.
            <AutocompleteItem
              key={`${s.destinatarioNombre}-${s.destinoUbicacionId}`}
              value={s}
              onClick={() => onElegirSugerencia(s)}
            >
              <div className="flex flex-col">
                <span>{s.destinatarioNombre}</span>
                <span className="text-xs text-muted-foreground">
                  {s.destinatarioTelefono} · {s.destinoCalleNumero}, {s.localidadNombre}
                </span>
              </div>
            </AutocompleteItem>
          )}
        </AutocompleteList>
      </AutocompleteContent>
    </Autocomplete>
  );
}
