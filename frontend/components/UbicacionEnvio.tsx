"use client";

import { MapPin } from "lucide-react";
import { Button } from "@/components/ui/button";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";

interface UbicacionEnvioProps {
  calleNumero: string;
  localidad: string | null;
  lat: number | null;
  lng: number | null;
  /** La geocodificación no es confiable (direccionDudosa): no se muestra un punto que puede estar mal. */
  dudosa?: boolean;
  entregado?: boolean;
}

/** Link de Google Maps: al punto si hay coordenadas confiables; si no, búsqueda por la dirección. */
export function urlGoogleMaps({ calleNumero, localidad, lat, lng, dudosa }: Omit<UbicacionEnvioProps, "entregado">): string {
  const consulta =
    lat !== null && lng !== null && !dudosa
      ? `${lat},${lng}`
      : encodeURIComponent(localidad ? `${calleNumero}, ${localidad}` : calleNumero);
  return `https://www.google.com/maps/search/?api=1&query=${consulta}`;
}

/** Dónde se entrega el envío: dirección, mapa (solo con coordenadas confiables) y botón grande para
 * abrirlo en Google Maps — pensado para el pulgar en el teléfono. */
export function UbicacionEnvio({ calleNumero, localidad, lat, lng, dudosa, entregado }: UbicacionEnvioProps) {
  const hayPunto = lat !== null && lng !== null && !dudosa;

  return (
    <div className="flex flex-col gap-3">
      <p className="flex items-start gap-2 text-sm">
        <MapPin className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        <span>
          {calleNumero}
          {localidad ? `, ${localidad}` : ""}
        </span>
      </p>

      {hayPunto && (
        <div>
          <MapaDinamico
            alto="h-48"
            marcadores={[
              {
                id: "destino",
                punto: { lat, lng },
                etiqueta: "●",
                variante: entregado ? "completada" : "pendiente",
                titulo: calleNumero,
              },
            ]}
          />
        </div>
      )}

      <Button
        variant="outline"
        className="h-11 w-full gap-2"
        nativeButton={false}
        render={
          <a
            href={urlGoogleMaps({ calleNumero, localidad, lat, lng, dudosa })}
            target="_blank"
            rel="noopener noreferrer"
          />
        }
      >
        <MapPin className="size-4" />
        Abrir en Google Maps
      </Button>
    </div>
  );
}
