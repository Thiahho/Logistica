"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
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
import type { Disponibilidad, RepartidorListado } from "@/lib/dominio/tipos";

/** Misma corrección de offset que /jornada: toISOString() sobre un Date local devuelve el día
 * anterior al oeste de Greenwich. */
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

export default function RepartidoresPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ListaRepartidores />
    </RequireRole>
  );
}

/**
 * Disponibilidad y carga por repartidor (RF-34, acta changelog 4.6).
 *
 * A diferencia de /jornada, que parte de las rutas del día y por lo tanto solo muestra a quien ya
 * tiene una asignada, acá la lista arranca de los usuarios con rol repartidor — el que está libre
 * aparece, que es justamente el que hace falta ver para decidir a quién asignar.
 *
 * El selector de fechas gobierna SOLO el acumulado. La columna de disponibilidad es siempre el
 * estado de hoy y no se mueve al cambiar el rango.
 */
function ListaRepartidores() {
  const { fetchConSesion } = useAuth();
  const [desde, setDesde] = useState(() => haceDias(6));
  const [hasta, setHasta] = useState(hoyLocal);
  const [repartidores, setRepartidores] = useState<RepartidorListado[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/repartidores?desde=${desde}&hasta=${hasta}`)
      .then((r) => leerJson<RepartidorListado[]>(r))
      .then((datos) => {
        setRepartidores(datos);
        setError(null);
      })
      .catch((err) =>
        setError(err instanceof Error ? err.message : "No se pudieron cargar los repartidores."),
      );
  }, [fetchConSesion, desde, hasta]);

  useEffect(cargar, [cargar]);

  const contar = (d: Disponibilidad) =>
    repartidores?.filter((r) => r.disponibilidad === d).length ?? 0;

  return (
    <div className="p-8 flex flex-col gap-6">
      <CabeceraSesion titulo="Repartidores" />

      <div className="flex flex-wrap items-end gap-4">
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
        <p className="text-xs text-muted-foreground pb-2">
          El rango afecta solo la carga acumulada. La disponibilidad es siempre la de hoy.
        </p>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {!repartidores ? (
        error ? null : <p className="text-muted-foreground">Cargando…</p>
      ) : repartidores.length === 0 ? (
        <p className="text-muted-foreground">
          No hay ningún usuario con rol repartidor. Se dan de alta desde Usuarios.
        </p>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <TarjetaMetrica valor={contar("en_ruta")} etiqueta="En ruta" />
            <TarjetaMetrica valor={contar("asignado")} etiqueta="Asignados" />
            <TarjetaMetrica valor={contar("libre")} etiqueta="Libres" />
            <TarjetaMetrica valor={contar("inactivo")} etiqueta="Inactivos" />
          </div>

          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Disponibilidad</TableHead>
                <TableHead>Ruta de hoy</TableHead>
                <TableHead>Avance de hoy</TableHead>
                <TableHead className="text-right">Rutas</TableHead>
                <TableHead className="text-right">Paradas</TableHead>
                <TableHead className="text-right">Entregadas</TableHead>
                <TableHead className="text-right">Fallidas</TableHead>
                <TableHead></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {repartidores.map((r) => (
                <TableRow key={r.id}>
                  <TableCell className="font-medium">{r.nombre}</TableCell>
                  <TableCell>
                    <DisponibilidadBadge disponibilidad={r.disponibilidad} />
                  </TableCell>
                  <TableCell>
                    {r.rutaHoyId === null || r.rutaHoyEstado === null ? (
                      <span className="text-muted-foreground">—</span>
                    ) : (
                      <Link href={`/rutas/${r.rutaHoyId}`} className="flex items-center gap-2 hover:underline">
                        <span>#{r.rutaHoyId}</span>
                        <EstadoRutaBadge estado={r.rutaHoyEstado} />
                        {r.vehiculoHoyPatente && (
                          <span className="text-xs text-muted-foreground">{r.vehiculoHoyPatente}</span>
                        )}
                      </Link>
                    )}
                  </TableCell>
                  <TableCell className="w-40">
                    {r.paradasHoy === 0 ? (
                      <span className="text-muted-foreground">—</span>
                    ) : (
                      <div className="flex flex-col gap-1">
                        <ProgresoParadas
                          total={r.paradasHoy}
                          completadas={r.completadasHoy}
                          fallidas={r.fallidasHoy}
                        />
                        <span className="text-xs text-muted-foreground">
                          {r.completadasHoy + r.fallidasHoy}/{r.paradasHoy}
                        </span>
                      </div>
                    )}
                  </TableCell>
                  <TableCell className="text-right">{r.rutasRango}</TableCell>
                  <TableCell className="text-right">{r.paradasRango}</TableCell>
                  <TableCell className="text-right">{r.completadasRango}</TableCell>
                  <TableCell className="text-right">{r.fallidasRango}</TableCell>
                  <TableCell>
                    <Button
                      size="sm"
                      variant="outline"
                      render={<Link href={`/repartidores/${r.id}?desde=${desde}&hasta=${hasta}`} />}
                      nativeButton={false}
                    >
                      Ver detalle
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </>
      )}
    </div>
  );
}
