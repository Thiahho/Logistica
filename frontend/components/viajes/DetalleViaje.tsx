"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useAuth } from "@/lib/auth/AuthProvider";
import { EstadoPedidoBadge } from "@/components/EstadoBadge";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa, VarianteMarcador } from "@/components/mapa/Mapa";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { leerError, leerJson } from "@/lib/api/errores";
import { etiquetaEstadoViaje, etiquetaTipoVehiculo, type EstadoPedido, type ViajeDetalle } from "@/lib/dominio/tipos";

const pesos = (n: number) => `$${n.toLocaleString("es-AR", { maximumFractionDigits: 2 })}`;

function variante(estado: EstadoPedido, enRuta: boolean): VarianteMarcador {
  if (!enRuta) return "dudosa";
  if (estado === "Entregado") return "completada";
  if (estado === "Fallido" || estado === "Devuelto") return "fallida";
  if (estado === "Cancelado") return "cancelada";
  return "pendiente";
}

/**
 * Detalle de un viaje: mapa con el recorrido en el orden de su ruta (el que Operación haya dejado),
 * las paradas con el estado de cada envío, y el avance. Lo usan el portal y el BackOffice: cambia la
 * ruta de la API, a dónde lleva cada parada y si se puede cancelar.
 */
export function DetalleViaje({
  ruta,
  hrefParada,
  rutaCancelar,
  mostrarRuta = false,
}: {
  /** GET del detalle: `/api/mi-cuenta/viajes/{id}` o `/api/viajes/{id}`. */
  ruta: string;
  /** A dónde lleva tocar una parada (el detalle del envío). */
  hrefParada: (pedidoId: number) => string;
  /** POST para cancelar el viaje entero; sin esto no se ofrece (empleado). */
  rutaCancelar?: string;
  /** BackOffice: enlace a la ruta propuesta. */
  mostrarRuta?: boolean;
}) {
  const { fetchConSesion } = useAuth();
  const [viaje, setViaje] = useState<ViajeDetalle | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cancelando, setCancelando] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(() => {
    fetchConSesion(ruta)
      .then(async (r) => {
        if (!r.ok) throw new Error((await leerError(r)).mensaje);
        return leerJson<ViajeDetalle>(r);
      })
      .then(setViaje)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el viaje."));
  }, [fetchConSesion, ruta]);

  useEffect(cargar, [cargar]);

  async function cancelar() {
    if (!rutaCancelar) return;
    setGuardando(true);
    setError(null);
    try {
      const resp = await fetchConSesion(rutaCancelar, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ motivo: motivo.trim() || null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setCancelando(false);
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cancelar el viaje.");
    } finally {
      setGuardando(false);
    }
  }

  if (error && !viaje) return <p className="text-sm text-destructive">{error}</p>;
  if (!viaje) return <div className="h-80 animate-pulse rounded-xl border bg-muted" />;

  const activas = viaje.paradas.filter((p) => p.estado !== "Cancelado");
  const marcadores: MarcadorMapa[] = [
    ...(viaje.origen.lat !== null && viaje.origen.lng !== null
      ? [{ id: "origen", punto: { lat: viaje.origen.lat, lng: viaje.origen.lng }, etiqueta: "D", imagen: "/logo-solo.png",
           variante: "origen" as const, titulo: `Salida: ${viaje.origen.nombre}` }]
      : []),
    ...viaje.paradas.flatMap((p) => p.lat !== null && p.lng !== null
      ? [{ id: p.pedidoId, punto: { lat: p.lat, lng: p.lng }, etiqueta: String(p.orden),
           variante: variante(p.estado, p.enRuta), titulo: `${p.orden}. ${p.destinatarioNombre} — ${p.calleNumero}` }]
      : []),
  ];
  const puedeCancelar = rutaCancelar && (viaje.estado === "planificado" || viaje.estado === "sin_ruta")
    && viaje.paradas.every((p) => p.estado === "Borrador" || p.estado === "Cancelado");

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span className="rounded-full bg-accent px-3 py-1 font-medium text-bf-azul">{etiquetaEstadoViaje(viaje.estado)}</span>
        <span className="text-muted-foreground">
          Entrega {viaje.fechaEntrega.split("-").reverse().join("/")}
          {viaje.tipoVehiculo && ` · ${etiquetaTipoVehiculo(viaje.tipoVehiculo)}`}
          {` · cargó ${viaje.creadoPor}`}
          {mostrarRuta && ` · ${viaje.clienteRazonSocial}`}
        </span>
        {mostrarRuta && viaje.rutaId && (
          <Link href={`/rutas/${viaje.rutaId}`} className="font-medium text-bf-azul underline-offset-2 hover:underline">
            Ver ruta #{viaje.rutaId}
          </Link>
        )}
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <TarjetaMetrica valor={activas.length} etiqueta="Paradas" />
        <TarjetaMetrica valor={`${viaje.entregadas}/${activas.length}`} etiqueta="Entregadas" />
        <TarjetaMetrica valor={viaje.fallidas} etiqueta="Fallidas" tono={viaje.fallidas > 0 ? "alerta" : "normal"} />
        {viaje.total !== null ? (
          <TarjetaMetrica valor={pesos(viaje.total)} etiqueta="Total del viaje" />
        ) : (
          <TarjetaMetrica valor={viaje.kmEstimados !== null ? `${viaje.kmEstimados.toLocaleString("es-AR")} km` : "—"} etiqueta="Recorrido estimado" />
        )}
      </div>

      <MapaDinamico marcadores={marcadores} recorrido={viaje.linea} alto="h-96" />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Paradas en orden de recorrido</CardTitle>
        </CardHeader>
        <CardContent>
          <ol className="flex flex-col divide-y">
            {viaje.paradas.map((p) => (
              <li key={p.pedidoId}>
                <Link
                  href={hrefParada(p.pedidoId)}
                  className="flex items-center gap-3 py-3 text-sm transition-colors hover:bg-muted/30"
                >
                  <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-accent font-semibold text-bf-azul">
                    {p.orden}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{p.destinatarioNombre}</p>
                    <p className="truncate text-muted-foreground">
                      {p.calleNumero}{p.localidad ? `, ${p.localidad}` : ""} · {p.bultos} {p.bultos === 1 ? "bulto" : "bultos"}
                      {!p.enRuta && p.estado === "Borrador" && <span className="text-amber-700"> · dirección a verificar</span>}
                    </p>
                  </div>
                  {p.precio !== null && <span className="hidden shrink-0 tabular-nums md:block">{pesos(p.precio)}</span>}
                  <EstadoPedidoBadge estado={p.estado} />
                </Link>
              </li>
            ))}
          </ol>
        </CardContent>
      </Card>

      {puedeCancelar && (
        <Card>
          <CardContent className="flex flex-col gap-3 pt-6">
            {!cancelando ? (
              <Button variant="outline" className="self-start text-destructive" onClick={() => setCancelando(true)}>
                Cancelar viaje
              </Button>
            ) : (
              <>
                <p className="text-sm">
                  Se cancelan las {activas.length} paradas. Como todavía no salió, no tiene costo. No se puede deshacer.
                </p>
                <Input aria-label="Motivo (opcional)" placeholder="Motivo (opcional)" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                <div className="flex gap-2">
                  <Button variant="destructive" disabled={guardando} onClick={cancelar}>
                    {guardando ? "Cancelando…" : "Sí, cancelar viaje"}
                  </Button>
                  <Button variant="ghost" onClick={() => setCancelando(false)}>
                    No, volver
                  </Button>
                </div>
              </>
            )}
          </CardContent>
        </Card>
      )}

      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  );
}
