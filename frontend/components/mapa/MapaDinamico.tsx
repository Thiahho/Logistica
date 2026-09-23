"use client";

import dynamic from "next/dynamic";

// Leaflet toca `window` al importarse: tiene que quedar fuera del render de servidor.
// Verificado contra node_modules/next/dist/docs/01-app/02-guides/lazy-loading.md — ssr: false
// solo es válido dentro de un Client Component, que es justo lo que este archivo es.
export const MapaDinamico = dynamic(() => import("./Mapa"), {
  ssr: false,
  loading: () => <div className="h-80 animate-pulse rounded-lg border bg-muted" />,
});
