"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { EstadoRutaBadge } from "@/components/EstadoBadge";
import { ProgresoParadas } from "@/components/ProgresoParadas";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useSondeo } from "@/lib/hooks/useSondeo";
import type { ResumenJornada } from "@/lib/dominio/tipos";

function hoyLocal(): string {
  const d = new Date();
  const sinOffset = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
  return sinOffset.toISOString().slice(0, 10);
}

export default function JornadaPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <Jornada />
    </RequireRole>
  );
}

/**
 * Monitor del día en curso (jornada §9.3: "armado y cierre de ruta, ejecución en calle" es
 * función básica del ciclo diario) — NO es el tablero de indicadores (B2/E3, Anexo I §5, sigue
 * fuera de alcance): cuenta filas de rutas/ruta_paradas para UNA fecha, sin acumular ni comparar
 * períodos. Mismo encuadre que /cobranza (acta changelog 4.3), que declara la misma distinción.
 */
function Jornada() {
  const [fecha, setFecha] = useState(hoyLocal());
  const { datos, error, actualizadoEn, cargando } = useSondeo<ResumenJornada>(
    `/api/jornada/resumen?fecha=${fecha}`,
    { intervaloMs: 20_000 },
  );

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6">
      <CabeceraSesion titulo="Jornada" />

      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="fecha-jornada">Fecha</Label>
          <Input
            id="fecha-jornada"
            type="date"
            value={fecha}
            onChange={(e) => setFecha(e.target.value)}
            className="w-40"
          />
        </div>
        <IndicadorActualizacion actualizadoEn={actualizadoEn} error={error} />
      </div>

      {cargando && !datos ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : !datos ? (
        <p className="text-sm text-destructive">{error ?? "No se pudo cargar la jornada."}</p>
      ) : (
        <>
          <section className="flex flex-col gap-2">
            <p className="text-sm font-medium text-muted-foreground">Rutas</p>
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <TarjetaMetrica valor={datos.rutas.planificadas} etiqueta="Planificadas" />
              <TarjetaMetrica valor={datos.rutas.enCurso} etiqueta="En curso" />
              <TarjetaMetrica valor={datos.rutas.cerradas} etiqueta="Cerradas" />
              <TarjetaMetrica valor={datos.rutas.total} etiqueta="Total del día" />
            </div>
          </section>

          <section className="flex flex-col gap-2">
            <p className="text-sm font-medium text-muted-foreground">Paradas</p>
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <TarjetaMetrica valor={datos.paradas.total} etiqueta="Total" />
              <TarjetaMetrica valor={datos.paradas.pendientes} etiqueta="Pendientes" />
              <TarjetaMetrica valor={datos.paradas.completadas} etiqueta="Completadas" />
              <TarjetaMetrica
                valor={datos.paradas.fallidas}
                etiqueta="Fallidas"
                tono={datos.paradas.fallidas > 0 ? "alerta" : "normal"}
              />
            </div>
          </section>

          <section className="flex flex-col gap-2">
            <p className="text-sm font-medium text-muted-foreground">Rutas del día</p>
            {datos.rutasDelDia.length === 0 ? (
              <p className="text-muted-foreground">No hay rutas para esta fecha.</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {datos.rutasDelDia.map((r) => (
                  <li key={r.id}>
                    <Link
                      href={`/rutas/${r.id}`}
                      className="flex flex-col gap-2 rounded-lg border p-3 hover:bg-accent sm:flex-row sm:items-center sm:justify-between"
                    >
                      <div className="flex items-center gap-3">
                        <span className="font-medium">Ruta #{r.id}</span>
                        <EstadoRutaBadge estado={r.estado} />
                        <span className="text-sm text-muted-foreground">
                          {r.repartidorNombre ?? "Sin repartidor"} · {r.vehiculoPatente ?? "Sin vehículo"}
                        </span>
                      </div>
                      <div className="flex items-center gap-3 sm:w-48">
                        <div className="flex-1">
                          <ProgresoParadas total={r.paradas} completadas={r.completadas} fallidas={r.fallidas} />
                        </div>
                        <span className="shrink-0 text-xs text-muted-foreground">
                          {r.completadas + r.fallidas}/{r.paradas}
                        </span>
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="flex flex-col gap-2">
            <p className="text-sm font-medium text-muted-foreground">Repartidores</p>
            {datos.novedadesAbiertas > 0 && (
              <p className="rounded-lg border border-amber-500 bg-amber-50/60 p-3 text-sm">
                {datos.novedadesAbiertas} novedad(es) del repartidor esperando respuesta — entrá a la ruta para
                responderlas.
              </p>
            )}
            {datos.declaracionesPendientes > 0 && (
              <p className="rounded-lg border border-blue-500 bg-blue-50/60 p-3 text-sm">
                {datos.declaracionesPendientes} ruta(s) con el cierre del repartidor esperando tu revisión.
              </p>
            )}
            {datos.repartidores.length === 0 ? (
              <p className="text-muted-foreground">Ningún repartidor tiene ruta asignada esta fecha.</p>
            ) : (
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                {datos.repartidores.map((rp) => (
                  <div key={rp.repartidorId} className="flex flex-col gap-3 rounded-lg border p-4">
                    <div className="flex items-center justify-between">
                      <span className="font-medium">{rp.nombre}</span>
                      <EstadoRutaBadge estado={rp.rutaEstado} />
                    </div>
                    <p className="text-sm text-muted-foreground">{rp.vehiculoPatente ?? "Sin vehículo"}</p>
                    <ProgresoParadas total={rp.paradas} completadas={rp.completadas} fallidas={rp.fallidas} />
                    <p className="text-xs text-muted-foreground">
                      {rp.completadas + rp.fallidas} de {rp.paradas} resueltas
                      {rp.pendientes > 0 ? ` · ${rp.pendientes} pendientes` : ""}
                    </p>
                    <div className="flex flex-col gap-0.5 text-xs">
                      {rp.retiroConfirmadoEn === null ? (
                        <span className="text-amber-700">Retiro sin firmar</span>
                      ) : (
                        <span
                          className={
                            rp.retiroBultosContados !== rp.retiroBultosEsperados
                              ? "font-medium text-destructive"
                              : "text-muted-foreground"
                          }
                        >
                          Retiro {new Date(rp.retiroConfirmadoEn).toLocaleTimeString("es-AR")}: {rp.retiroBultosContados} de{" "}
                          {rp.retiroBultosEsperados} bultos
                          {rp.retiroBultosContados !== rp.retiroBultosEsperados ? " — no coincide" : ""}
                        </span>
                      )}
                      {rp.novedadesAbiertas > 0 && (
                        <span className="font-medium text-amber-700">
                          {rp.novedadesAbiertas} novedad(es) sin responder
                        </span>
                      )}
                      {rp.cierreRepartidorEn && rp.rutaEstado === "en_curso" && (
                        <span className="font-medium text-blue-700">Cierre declarado: pendiente de revisión</span>
                      )}
                    </div>
                    <div className="flex flex-col gap-0.5 text-xs text-muted-foreground">
                      {rp.primeraLlegada && <span>Primera llegada: {new Date(rp.primeraLlegada).toLocaleTimeString("es-AR")}</span>}
                      {rp.ultimaActividad && <span>Última actividad: {new Date(rp.ultimaActividad).toLocaleTimeString("es-AR")}</span>}
                    </div>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" render={<Link href={`/rutas/${rp.rutaId}`} />} nativeButton={false}>
                        Ver ruta
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        render={<Link href={`/rutas?repartidorId=${rp.repartidorId}`} />}
                        nativeButton={false}
                      >
                        Historial
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}

/** Reloj chico que recalcula el "hace Xs" cada 5s — el fetch real lo maneja useSondeo, esto es
 * puramente cosmético. `Date.now()` vive en el efecto (permitido), nunca en el cuerpo del
 * render: llamarlo ahí es una función impura, prohibida por las reglas de pureza de React. */
function IndicadorActualizacion({ actualizadoEn, error }: { actualizadoEn: Date | null; error: string | null }) {
  const [segundos, setSegundos] = useState(0);

  useEffect(() => {
    if (!actualizadoEn) return;
    const calcular = () => setSegundos(Math.max(0, Math.round((Date.now() - actualizadoEn.getTime()) / 1000)));
    calcular();
    const id = setInterval(calcular, 5000);
    return () => clearInterval(id);
  }, [actualizadoEn]);

  if (!actualizadoEn) return null;
  const texto = segundos < 5 ? "recién actualizado" : `actualizado hace ${segundos}s`;

  return (
    <p className={`text-xs ${error ? "text-destructive" : "text-muted-foreground"}`}>
      {error ? `${error} — mostrando datos ${texto}` : texto}
    </p>
  );
}
