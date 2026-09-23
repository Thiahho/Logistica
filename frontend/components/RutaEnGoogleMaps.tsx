"use client";

import { useMemo, useState } from "react";
import { Check, Copy, Map as MapaIcono } from "lucide-react";
import { Button } from "@/components/ui/button";
import { enlacesRutaGoogleMaps } from "@/lib/dominio/googleMaps";
import type { EstadoRuta, OrigenRuta, ParadaDelDia } from "@/lib/dominio/tipos";

interface RutaEnGoogleMapsProps {
  origen: OrigenRuta | null;
  paradas: ParadaDelDia[];
  estado: EstadoRuta;
  /** "Copiar link" es para quien despacha (back-office); el repartidor no lo necesita. */
  mostrarCopiar?: boolean;
  /** Botones más altos para usar con el pulgar en la calle (PWA del repartidor). */
  grande?: boolean;
}

/**
 * Lleva la ruta armada a Google Maps: punto de salida + paradas en orden, listas para navegar desde
 * el teléfono. Una ruta larga se parte en tramos (ver MAX_PARADAS_POR_TRAMO) y cada uno tiene su botón.
 * "Copiar link" sirve para mandárselo al repartidor por WhatsApp.
 */
export function RutaEnGoogleMaps({ origen, paradas, estado, mostrarCopiar = true, grande = false }: RutaEnGoogleMapsProps) {
  const alto = grande ? "h-12 text-base" : "h-11";
  const [copiado, setCopiado] = useState<string | null>(null);
  const tramos = useMemo(() => enlacesRutaGoogleMaps({ origen, paradas, estado }), [origen, paradas, estado]);

  if (estado === "cerrada") return null;

  if (tramos.length === 0) {
    return (
      <div className="flex flex-col gap-1">
        <Button className={`${alto} w-full gap-2`} disabled>
          <MapaIcono className="size-4" />
          Abrir en Google Maps
        </Button>
        <p className="text-xs text-muted-foreground">
          {estado === "en_curso" ? "No quedan paradas pendientes." : "Todavía no hay paradas armadas."}
        </p>
      </div>
    );
  }

  async function copiar(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      setCopiado(url);
      setTimeout(() => setCopiado((actual) => (actual === url ? null : actual)), 2000);
    } catch {
      // Sin permiso de portapapeles (http, navegador viejo): el botón principal sigue sirviendo.
      setCopiado(null);
    }
  }

  const origenTexto = origen
    ? origen.esDeposito
      ? (origen.nombreDeposito ?? "el depósito")
      : origen.calleNumero
    : null;

  return (
    <div className="flex flex-col gap-2">
      {tramos.map((tramo, i) => (
        <div key={tramo.url} className="flex flex-col gap-2 sm:flex-row">
          <Button
            className={`${alto} flex-1 gap-2`}
            nativeButton={false}
            render={<a href={tramo.url} target="_blank" rel="noopener noreferrer" />}
          >
            <MapaIcono className="size-4" />
            {tramos.length === 1 ? "Abrir en Google Maps" : `Tramo ${i + 1} · paradas ${tramo.desde}–${tramo.hasta}`}
          </Button>
          {mostrarCopiar && (
            <Button variant="outline" className={`${alto} gap-2 sm:w-40`} onClick={() => copiar(tramo.url)}>
              {copiado === tramo.url ? <Check className="size-4" /> : <Copy className="size-4" />}
              {copiado === tramo.url ? "Link copiado" : "Copiar link"}
            </Button>
          )}
        </div>
      ))}
      <p className="text-xs text-muted-foreground">
        {tramos[0].saleDelOrigen && origenTexto
          ? `Sale de ${origenTexto} y sigue el orden de la ruta.`
          : "Sale de tu ubicación actual y sigue el orden de la ruta."}
        {tramos.length > 1 && " Al terminar un tramo, abrí el siguiente."}
      </p>
    </div>
  );
}
