"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { ProgresoParadas } from "@/components/ProgresoParadas";
import { DisponibilidadBadge, EstadoRutaBadge } from "@/components/EstadoBadge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerJson } from "@/lib/api/errores";
import type { RepartidorDetalle } from "@/lib/dominio/tipos";

function hoyLocal(): string {
  const d = new Date();
  const sinOffset = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
  return sinOffset.toISOString().slice(0, 10);
}

function haceDias(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  const sinOffset = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
  return sinOffset.toISOString().slice(0, 10);
}

export default function RepartidorDetallePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      {/* useSearchParams exige límite de Suspense (mismo patrón que /facturas y /login). */}
      <Suspense fallback={null}>
        <Detalle />
      </Suspense>
    </RequireRole>
  );
}

/** Historial de rutas del rango, sin un solo importe: RNF-08 rige igual acá aunque quien mira sea
 * back-office, y el pago al repartidor es B4/E2, fuera de alcance. */
function Detalle() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();
  const parametros = useSearchParams();

  // El rango viaja en la URL desde el listado, así que volver y entrar a otro repartidor no
  // pierde las fechas que se venían mirando.
  const [desde, setDesde] = useState(() => parametros.get("desde") ?? haceDias(6));
  const [hasta, setHasta] = useState(() => parametros.get("hasta") ?? hoyLocal());
  const [detalle, setDetalle] = useState<RepartidorDetalle | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/repartidores/${id}?desde=${desde}&hasta=${hasta}`)
      .then((r) => leerJson<RepartidorDetalle>(r))
      .then((datos) => {
        setDetalle(datos);
        setError(null);
      })
      .catch((err) =>
        setError(err instanceof Error ? err.message : "No se pudo cargar el repartidor."),
      );
  }, [fetchConSesion, id, desde, hasta]);

  useEffect(cargar, [cargar]);

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6">
      <CabeceraSesion titulo={detalle?.nombre ?? "Repartidor"} />

      <div className="flex flex-wrap items-end gap-4">
        <Button variant="outline" render={<Link href="/repartidores" />} nativeButton={false}>
          ← Repartidores
        </Button>
        <div className="flex flex-col gap-2">
          <Label htmlFor="desde">Desde</Label>
          <Input
            id="desde"
            type="date"
            value={desde}
            max={hasta}
            onChange={(e) => setDesde(e.target.value)}
            className="w-40"
          />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="hasta">Hasta</Label>
          <Input
            id="hasta"
            type="date"
            value={hasta}
            min={desde}
            onChange={(e) => setHasta(e.target.value)}
            className="w-40"
          />
        </div>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {!detalle ? (
        error ? null : <p className="text-muted-foreground">Cargando…</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
            <DisponibilidadBadge disponibilidad={detalle.disponibilidad} size="md" />
            <span>{detalle.email}</span>
            {!detalle.activo && <span className="text-destructive">Usuario dado de baja</span>}
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <TarjetaMetrica valor={detalle.rutasRango} etiqueta="Rutas del rango" />
            <TarjetaMetrica valor={detalle.paradasRango} etiqueta="Paradas" />
            <TarjetaMetrica valor={detalle.completadasRango} etiqueta="Entregadas" />
            <TarjetaMetrica
              valor={detalle.fallidasRango}
              etiqueta="Fallidas"
              tono={detalle.fallidasRango > 0 ? "alerta" : "normal"}
            />
          </div>

          {detalle.rutas.length === 0 ? (
            <p className="text-muted-foreground">
              Sin rutas asignadas entre {detalle.desde} y {detalle.hasta}.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Fecha</TableHead>
                  <TableHead>Ruta</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Vehículo</TableHead>
                  <TableHead>Avance</TableHead>
                  <TableHead className="text-right">Paradas</TableHead>
                  <TableHead className="text-right">Entregadas</TableHead>
                  <TableHead className="text-right">Fallidas</TableHead>
                  <TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {detalle.rutas.map((r) => (
                  <TableRow key={r.rutaId}>
                    <TableCell>{r.fecha}</TableCell>
                    <TableCell>#{r.rutaId}</TableCell>
                    <TableCell>
                      <EstadoRutaBadge estado={r.estado} />
                    </TableCell>
                    <TableCell>
                      {r.vehiculoPatente ?? <span className="text-muted-foreground">—</span>}
                    </TableCell>
                    <TableCell className="w-40">
                      {r.paradas === 0 ? (
                        <span className="text-muted-foreground">Sin paradas</span>
                      ) : (
                        <ProgresoParadas
                          total={r.paradas}
                          completadas={r.completadas}
                          fallidas={r.fallidas}
                        />
                      )}
                    </TableCell>
                    <TableCell className="text-right">{r.paradas}</TableCell>
                    <TableCell className="text-right">{r.completadas}</TableCell>
                    <TableCell className="text-right">{r.fallidas}</TableCell>
                    <TableCell>
                      <Button
                        size="sm"
                        variant="outline"
                        render={<Link href={`/rutas/${r.rutaId}`} />}
                        nativeButton={false}
                      >
                        Ver ruta
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
