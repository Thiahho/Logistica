"use client";

import "leaflet/dist/leaflet.css";
import L from "leaflet";
import { useEffect, useMemo } from "react";
import { MapContainer, Marker, Polyline, TileLayer, useMap } from "react-leaflet";
import type { Punto } from "@/lib/dominio/geo";

export type VarianteMarcador = "pendiente" | "completada" | "fallida" | "origen" | "dudosa";

export interface MarcadorMapa {
  id: number | string;
  punto: Punto;
  etiqueta: string;
  variante: VarianteMarcador;
  titulo: string;
  seleccionado?: boolean;
}

export interface MapaProps {
  marcadores: MarcadorMapa[];
  /** Polyline ya trazada por OSRM (backend). null: se dibuja línea recta entre marcadores en el
   * mismo orden — degradación consciente, nunca se rompe el mapa por un proveedor externo caído. */
  recorrido?: Punto[] | null;
  alto?: string;
  onSeleccionar?: (id: number | string) => void;
}

// Color por variante, con la misma excepción a "siempre por token" que ya usa clientes/page.tsx
// para los semáforos (verde/amarillo/rojo): son indicadores de estado, no decoración.
const COLOR_VARIANTE: Record<VarianteMarcador, string> = {
  pendiente: "#2563eb", // blue-600
  completada: "#16a34a", // green-600
  fallida: "#dc2626", // red-600
  origen: "#171717", // neutral-900
  dudosa: "#d97706", // amber-600
};

function icono(m: MarcadorMapa): L.DivIcon {
  const color = COLOR_VARIANTE[m.variante];
  const anillo = m.seleccionado ? "0 0 0 3px white, 0 0 0 5px " + color : "0 1px 3px rgba(0,0,0,.4)";
  return L.divIcon({
    className: "",
    html: `<span style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:9999px;background:${color};color:#fff;font-size:12px;font-weight:700;box-shadow:${anillo};border:2px solid white;">${m.etiqueta}</span>`,
    iconSize: [28, 28],
    iconAnchor: [14, 14],
  });
}

/** Encuadra el mapa cada vez que cambia el conjunto de puntos visibles. Vive adentro de
 * <MapContainer> porque useMap solo funciona en su árbol. */
function AjustarVista({ puntos }: { puntos: Punto[] }) {
  const mapa = useMap();

  useEffect(() => {
    if (puntos.length === 0) return;
    if (puntos.length === 1) {
      mapa.setView([puntos[0].lat, puntos[0].lng], 15);
      return;
    }
    mapa.fitBounds(
      puntos.map((p) => [p.lat, p.lng] as [number, number]),
      { padding: [32, 32] },
    );
  }, [mapa, puntos]);

  // ResizeObserver: el mapa suele montar dentro de un <Card> que todavía se está acomodando
  // (flex/grid), y sin esto quedan tiles grises hasta el próximo resize de la ventana.
  useEffect(() => {
    const contenedor = mapa.getContainer();
    const obs = new ResizeObserver(() => mapa.invalidateSize());
    obs.observe(contenedor);
    return () => obs.disconnect();
  }, [mapa]);

  return null;
}

export default function Mapa({ marcadores, recorrido, alto = "h-80", onSeleccionar }: MapaProps) {
  const puntosVisibles = useMemo(() => marcadores.map((m) => m.punto), [marcadores]);
  const centro = puntosVisibles[0] ?? { lat: -34.6037, lng: -58.3816 };

  const lineaRecorrido = useMemo<[number, number][] | null>(() => {
    const puntos = recorrido && recorrido.length > 1 ? recorrido : puntosVisibles.length > 1 ? puntosVisibles : null;
    return puntos ? puntos.map((p) => [p.lat, p.lng]) : null;
  }, [recorrido, puntosVisibles]);

  if (marcadores.length === 0) {
    return (
      <div className={`${alto} flex items-center justify-center rounded-lg border text-sm text-muted-foreground`}>
        No hay direcciones geolocalizadas para mostrar en el mapa.
      </div>
    );
  }

  return (
    // `isolate` (isolation: isolate) no es cosmético: Leaflet reparte z-index altos por diseño
    // (.leaflet-pane 400, .leaflet-top/.leaflet-control 1000) y su contenedor no crea contexto de
    // apilamiento propio, así que esos valores competían directo contra el z-50 de Dialog y del
    // popup de Combobox (components/ui/dialog.tsx, combobox.tsx) y les ganaban: el mapa se dibujaba
    // encima del diálogo de reasignar repartidor en /rutas/[id] y tapaba su desplegable. Aislar acá
    // encierra toda la escala interna de Leaflet en un contexto propio y lo arregla para cualquier
    // diálogo, popover o dropdown que se superponga al mapa, sin subir z-index en cadena.
    <div className={`${alto} isolate overflow-hidden rounded-lg border`}>
      <MapContainer center={[centro.lat, centro.lng]} zoom={13} scrollWheelZoom style={{ height: "100%", width: "100%" }}>
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          maxZoom={19}
        />
        {lineaRecorrido && (
          <Polyline
            positions={lineaRecorrido}
            pathOptions={{
              color: "#2563eb",
              weight: 4,
              opacity: 0.7,
              // Sin recorrido trazado por OSRM (degradado a línea recta): punteado, para no
              // sugerir que ese es el trayecto real por calles.
              dashArray: recorrido && recorrido.length > 1 ? undefined : "6 8",
            }}
          />
        )}
        {marcadores.map((m) => (
          <Marker
            key={m.id}
            position={[m.punto.lat, m.punto.lng]}
            icon={icono(m)}
            title={m.titulo}
            eventHandlers={onSeleccionar ? { click: () => onSeleccionar(m.id) } : undefined}
          />
        ))}
        <AjustarVista puntos={puntosVisibles} />
      </MapContainer>
    </div>
  );
}
