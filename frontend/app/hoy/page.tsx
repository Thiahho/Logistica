"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  Maximize2,
  MapPin,
  Navigation,
  Package,
  X,
} from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { leerJson } from "@/lib/api/errores";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa, VarianteMarcador } from "@/components/mapa/Mapa";
import type { JornadaDelDia, ParadaDelDia } from "@/lib/dominio/tipos";

export default function HoyPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <GuiaDeRuta />
    </RequireRole>
  );
}

const VARIANTE_POR_ESTADO: Record<string, VarianteMarcador> = {
  pendiente: "pendiente",
  completada: "completada",
  fallida: "fallida",
};

const ESTILO_ESTADO: Record<string, string> = {
  pendiente: "bg-blue-100 text-blue-700",
  completada: "bg-green-100 text-green-700",
  fallida: "bg-red-100 text-red-700",
};

const ETIQUETA_ESTADO: Record<string, string> = {
  pendiente: "Pendiente",
  completada: "Entregada",
  fallida: "Fallida",
};

function urlComoLlegar(p: { lat: number | null; lng: number | null }) {
  return `https://www.google.com/maps/dir/?api=1&destination=${p.lat},${p.lng}&travelmode=driving`;
}

function GuiaDeRuta() {
  const { fetchConSesion } = useAuth();
  const [jornada, setJornada] = useState<JornadaDelDia | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [mapaExpandido, setMapaExpandido] = useState(false);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas/dia")
      .then((r) => leerJson<JornadaDelDia>(r))
      .then(setJornada)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la jornada."));
  }, [fetchConSesion]);

  if (error) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Hoy" />
        <p className="text-sm text-destructive">{error}</p>
      </div>
    );
  }

  if (!jornada) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Hoy" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }

  if (jornada.rutaId === null) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Hoy" />
        <p className="text-muted-foreground">No tenés una ruta en curso.</p>
      </div>
    );
  }

  // Marcadores: origen de la ruta (depósito o donde quedó la camioneta, acta changelog 3.6) +
  // paradas con coordenada. Degradación consciente en ambos casos — nunca se inventa posición
  // para lo que no la tiene: el origen puede no estar geocodificado (dirección tipeada a mano
  // al armar) igual que una parada puede no estarlo.
  const marcadores: MarcadorMapa[] = [
    ...(jornada.origen && jornada.origen.lat !== null && jornada.origen.lng !== null
      ? [
          {
            id: "origen",
            punto: { lat: jornada.origen.lat, lng: jornada.origen.lng },
            etiqueta: jornada.origen.esDeposito ? "D" : "P",
            variante: "origen" as const,
            titulo: jornada.origen.esDeposito
              ? (jornada.origen.nombreDeposito ?? "Depósito")
              : `Partida: ${jornada.origen.calleNumero}${jornada.origen.localidad ? `, ${jornada.origen.localidad}` : ""}`,
          },
        ]
      : []),
    ...jornada.paradas
      .filter((p) => p.lat !== null && p.lng !== null)
      .map((p) => ({
        id: p.paradaId,
        punto: { lat: p.lat as number, lng: p.lng as number },
        etiqueta: String(p.orden),
        variante: VARIANTE_POR_ESTADO[p.estado] ?? "pendiente",
        titulo: `${p.calleNumero}${p.localidad ? `, ${p.localidad}` : ""}`,
      })),
  ];

  const resueltas = jornada.completadas + jornada.fallidas;
  const pctCompletadas = jornada.total > 0 ? (jornada.completadas / jornada.total) * 100 : 0;
  const pctFallidas = jornada.total > 0 ? (jornada.fallidas / jornada.total) * 100 : 0;
  const proxima = jornada.paradas.find((p) => p.estado === "pendiente");

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <CabeceraSesion titulo="Hoy" />

      {/* Panel de progreso — "ventana" de estado, siempre visible arriba. */}
      <div className="rounded-lg border p-4 flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <span className="text-2xl font-semibold">
            {resueltas} <span className="text-base font-normal text-muted-foreground">de {jornada.total}</span>
          </span>
          <span className="text-sm text-muted-foreground">paradas resueltas</span>
        </div>
        <div className="flex h-2.5 w-full overflow-hidden rounded-full bg-muted">
          {pctCompletadas > 0 && <div className="h-full bg-green-600" style={{ width: `${pctCompletadas}%` }} />}
          {pctFallidas > 0 && <div className="h-full bg-red-600" style={{ width: `${pctFallidas}%` }} />}
        </div>
        {jornada.origen && !jornada.origen.esDeposito && (
          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <MapPin className="size-3.5 shrink-0" />
            Salís desde: {jornada.origen.calleNumero}
            {jornada.origen.localidad ? `, ${jornada.origen.localidad}` : ""}
          </p>
        )}
      </div>

      {/* Próxima parada — la "ventana" funcional que importa primero: qué sigue, y cómo llegar. */}
      {proxima && (
        <div className="rounded-lg border-2 border-blue-600 p-4 flex flex-col gap-3 bg-blue-50/50">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-blue-700">Próxima parada</span>
            <span className="flex size-7 items-center justify-center rounded-full bg-blue-600 text-sm font-bold text-white">
              {proxima.orden}
            </span>
          </div>
          <div>
            <p className="font-medium">{proxima.pedidos.map((ped) => ped.destinatarioNombre).join(" · ")}</p>
            <p className="text-sm text-muted-foreground">
              {proxima.calleNumero}
              {proxima.localidad ? `, ${proxima.localidad}` : ""}
            </p>
          </div>
          <div className="flex gap-2">
            {proxima.lat !== null && proxima.lng !== null && (
              <Button
                className="h-12 flex-1 text-base"
                render={<a href={urlComoLlegar(proxima)} target="_blank" rel="noopener noreferrer" />}
                nativeButton={false}
              >
                <Navigation className="size-4" />
                Cómo llegar
              </Button>
            )}
            <Button
              variant="outline"
              className="h-12 flex-1 text-base"
              render={<Link href={`/hoy/parada/${proxima.paradaId}`} />}
              nativeButton={false}
            >
              Ver detalle
            </Button>
          </div>
        </div>
      )}

      <div className="relative">
        {/* El mapa chico se saca del árbol mientras está expandido (no solo se tapa): los
        controles propios de Leaflet (zoom, atribución) usan z-index >1000 en su propia hoja de
        estilos, por encima de cualquier overlay razonable de la página — dos mapas vivos a la
        vez terminaban con los controles del chico asomando sobre el modal. */}
        {!mapaExpandido && (
          <>
            <MapaDinamico marcadores={marcadores} recorrido={jornada.recorrido?.linea ?? null} alto="h-64" />
            {marcadores.length > 0 && (
              <Button
                variant="outline"
                size="icon"
                className="absolute right-2 top-2 z-[400] size-9 bg-background shadow"
                onClick={() => setMapaExpandido(true)}
              >
                <Maximize2 className="size-4" />
              </Button>
            )}
          </>
        )}
      </div>

      {mapaExpandido && (
        <div className="fixed inset-0 z-50 flex flex-col bg-background">
          <div className="flex items-center justify-between border-b p-3">
            <span className="font-medium">Mapa de la jornada</span>
            <Button variant="outline" size="icon" className="size-9" onClick={() => setMapaExpandido(false)}>
              <X className="size-4" />
            </Button>
          </div>
          <div className="flex-1">
            <MapaDinamico marcadores={marcadores} recorrido={jornada.recorrido?.linea ?? null} alto="h-full" />
          </div>
        </div>
      )}

      {jornada.paradas.length === 0 ? (
        <p className="text-muted-foreground">No tenés paradas asignadas hoy.</p>
      ) : (
        <div className="flex flex-col gap-2">
          <p className="text-sm font-medium text-muted-foreground">Todas las paradas</p>
          <ul className="flex flex-col gap-2">
            {jornada.paradas.map((p) => (
              <ParadaCard key={p.paradaId} parada={p} />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function ParadaCard({ parada: p }: { parada: ParadaDelDia }) {
  const resuelta = p.estado !== "pendiente";
  return (
    <li>
      <Link
        href={`/hoy/parada/${p.paradaId}`}
        className={`flex min-h-16 items-center gap-3 rounded-lg border p-3 ${resuelta ? "opacity-60" : ""}`}
      >
        <span
          className={`flex size-8 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
            p.estado === "completada"
              ? "bg-green-600 text-white"
              : p.estado === "fallida"
                ? "bg-red-600 text-white"
                : "bg-neutral-200 text-neutral-700"
          }`}
        >
          {p.orden}
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium">{p.pedidos.map((ped) => ped.destinatarioNombre).join(" · ")}</p>
          <p className="truncate text-sm text-muted-foreground">
            {p.calleNumero}
            {p.localidad ? `, ${p.localidad}` : ""}
            {(p.lat === null || p.lng === null) && " · sin coordenadas"}
          </p>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1">
          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${ESTILO_ESTADO[p.estado]}`}>
            {ETIQUETA_ESTADO[p.estado]}
          </span>
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <Package className="size-3" />
            {p.pedidos.reduce((n, ped) => n + ped.bultos, 0)}
          </span>
        </div>
      </Link>
    </li>
  );
}
