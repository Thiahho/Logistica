"use client";

import { useEffect, useState } from "react";
import {
  Autocomplete,
  AutocompleteContent,
  AutocompleteInput,
  AutocompleteItem,
  AutocompleteList,
} from "@/components/ui/autocomplete";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";

interface SugerenciaObservacionesProps {
  /** GET que devuelve las observaciones ya usadas: "/api/pedidos/observaciones-frecuentes?clienteId=…"
   * en el BackOffice, "/api/mi-cuenta/observaciones-frecuentes" en el portal. null = todavía no hay
   * cliente elegido: sin sugerencias. */
  endpoint: string | null;
  /** Nombre del destinatario de este envío: sus observaciones salen primero. */
  destinatario: string;
  value: string;
  onValueChange: (observaciones: string) => void;
  id?: string;
}

/**
 * Campo de observaciones, opcional y de texto libre, que además sugiere las que ya se usaron con ese
 * cliente (mismo patrón que SugerenciaDestinatario: Autocomplete, la lista solo sugiere). Una
 * observación nueva no se guarda aparte: queda en el envío y aparece sola la próxima vez.
 */
export function SugerenciaObservaciones({ endpoint, destinatario, value, onValueChange, id }: SugerenciaObservacionesProps) {
  const { fetchConSesion } = useAuth();
  const [sugerencias, setSugerencias] = useState<string[]>([]);
  // Autocomplete no abre el popup con foco, solo al tipear: se controla `open` a mano para que las
  // observaciones anteriores aparezcan al enfocar el campo (ver SugerenciaDestinatario).
  const [abierto, setAbierto] = useState(false);

  const destinatarioTrim = destinatario.trim();

  // Con debounce porque el destinatario se va tipeando. La lista es corta: se pide entera y lo que se
  // escribe en el campo la filtra acá, sin volver al servidor.
  useEffect(() => {
    if (endpoint === null) return;
    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      try {
        const params = new URLSearchParams();
        if (destinatarioTrim) params.set("destinatario", destinatarioTrim);
        const resp = await fetchConSesion(`${endpoint}${endpoint.includes("?") ? "&" : "?"}${params}`, {
          signal: abort.signal,
        });
        if (resp.ok) setSugerencias(await leerJson<string[]>(resp));
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setSugerencias([]); // opcional: sin sugerencias el campo sigue siendo texto libre
      }
    }, 300);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
  }, [endpoint, destinatarioTrim, fetchConSesion]);

  // Derivado, no reseteado en el efecto: sin cliente no valen las de una elección anterior. La que ya
  // está escrita tal cual no se ofrece de nuevo, así el popup se cierra al elegirla.
  const texto = value.trim().toLowerCase();
  const visibles =
    endpoint === null ? [] : sugerencias.filter((s) => s.toLowerCase().includes(texto) && s.toLowerCase() !== texto);

  return (
    <Autocomplete
      items={visibles}
      value={value}
      onValueChange={onValueChange}
      filter={null}
      // Sin nada que sugerir no se abre: se comporta como un campo de texto común.
      open={abierto && visibles.length > 0}
      onOpenChange={setAbierto}
    >
      <AutocompleteInput id={id} className="w-full" onFocus={() => setAbierto(true)} />
      <AutocompleteContent>
        <p className="px-2 pt-1.5 text-xs text-muted-foreground">Usadas antes con este cliente</p>
        <AutocompleteList>
          {(s: string) => (
            <AutocompleteItem key={s} value={s} className="whitespace-normal">
              {s}
            </AutocompleteItem>
          )}
        </AutocompleteList>
      </AutocompleteContent>
    </Autocomplete>
  );
}
