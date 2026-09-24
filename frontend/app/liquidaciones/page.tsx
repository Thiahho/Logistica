"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { LiquidacionResumen, ListaPaginada, PrevisualizacionLiquidacion } from "@/lib/dominio/tipos";
import { TablaRutasLiquidadas, pesos } from "./TablaRutasLiquidadas";

/** toISOString() sobre un Date local devuelve el día anterior al oeste de Greenwich: se corrige el offset. */
function fechaLocal(d: Date): string {
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}

interface RepartidorSeleccion {
  id: string;
  nombre: string;
}

export default function LiquidacionesPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <Liquidaciones />
    </RequireRole>
  );
}

/**
 * B4 — liquidación al repartidor (acta RF-41, changelog 4.21). Se elige repartidor y período, se ve qué
 * rutas cerradas sin liquidar entran y con qué pago, y se emite el comprobante. Emitido no se edita:
 * una corrección va en la liquidación siguiente.
 */
function Liquidaciones() {
  const { fetchConSesion } = useAuth();
  const [repartidores, setRepartidores] = useState<RepartidorSeleccion[]>([]);
  const [repartidorId, setRepartidorId] = useState("");
  const [desde, setDesde] = useState(() => {
    const d = new Date();
    return fechaLocal(new Date(d.getFullYear(), d.getMonth(), 1));
  });
  const [hasta, setHasta] = useState(() => fechaLocal(new Date()));
  const [nota, setNota] = useState("");
  const [previa, setPrevia] = useState<PrevisualizacionLiquidacion | null>(null);
  const [emitidas, setEmitidas] = useState<LiquidacionResumen[] | null>(null);
  const [trabajando, setTrabajando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/usuarios/seleccion?rol=repartidor")
      .then((r) => leerJson<RepartidorSeleccion[]>(r))
      .then(setRepartidores)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los repartidores."));
  }, [fetchConSesion]);

  const cargarEmitidas = useCallback(() => {
    fetchConSesion("/api/liquidaciones?pagina=1&tamanioPagina=50")
      .then((r) => leerJson<ListaPaginada<LiquidacionResumen>>(r))
      .then((l) => setEmitidas(l.items))
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar las liquidaciones."));
  }, [fetchConSesion]);

  useEffect(cargarEmitidas, [cargarEmitidas]);

  async function previsualizar() {
    setError(null);
    setPrevia(null);
    setTrabajando(true);
    try {
      const r = await fetchConSesion(
        `/api/liquidaciones/previsualizacion?repartidorId=${repartidorId}&desde=${desde}&hasta=${hasta}`,
      );
      setPrevia(await leerJson<PrevisualizacionLiquidacion>(r));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo previsualizar.");
    } finally {
      setTrabajando(false);
    }
  }

  async function emitir() {
    if (!previa) return;
    setError(null);
    setTrabajando(true);
    try {
      const r = await fetchConSesion("/api/liquidaciones", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ repartidorId, desde, hasta, nota: nota || null }),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setPrevia(null);
      setNota("");
      cargarEmitidas();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo emitir la liquidación.");
    } finally {
      setTrabajando(false);
    }
  }

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6 max-w-5xl">
      <CabeceraSesion titulo="Liquidaciones" />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Nueva liquidación</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="grid gap-4 sm:grid-cols-3">
            <div className="flex flex-col gap-2">
              <Label>Repartidor</Label>
              <Select
                items={repartidores.map((r) => ({ value: r.id, label: r.nombre }))}
                value={repartidorId || null}
                onValueChange={(v) => {
                  setRepartidorId(v ?? "");
                  setPrevia(null);
                }}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Elegir" />
                </SelectTrigger>
                <SelectContent>
                  {repartidores.map((r) => (
                    <SelectItem key={r.id} value={r.id}>
                      {r.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="desde">Desde</Label>
              <Input id="desde" type="date" value={desde} onChange={(e) => { setDesde(e.target.value); setPrevia(null); }} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="hasta">Hasta</Label>
              <Input id="hasta" type="date" value={hasta} onChange={(e) => { setHasta(e.target.value); setPrevia(null); }} />
            </div>
          </div>
          <Button className="self-start" variant="outline" disabled={!repartidorId || trabajando} onClick={previsualizar}>
            Ver rutas a liquidar
          </Button>

          {previa && (
            <div className="flex flex-col gap-4 border-t pt-4">
              {previa.rutas.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  {previa.repartidorNombre} no tiene rutas cerradas sin liquidar en el período.
                </p>
              ) : (
                <>
                  <TablaRutasLiquidadas rutas={previa.rutas} total={previa.total} />
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="nota">Nota (opcional)</Label>
                    <Input id="nota" value={nota} maxLength={2000} onChange={(e) => setNota(e.target.value)} />
                  </div>
                  <p className="text-xs text-muted-foreground">
                    Una liquidación emitida no se edita ni se borra, y sus rutas no cambian de pago. Una corrección
                    va en la liquidación siguiente.
                  </p>
                  <Button className="self-start" disabled={trabajando} onClick={emitir}>
                    {trabajando ? "Emitiendo…" : `Emitir liquidación por ${pesos(previa.total)}`}
                  </Button>
                </>
              )}
            </div>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Emitidas</CardTitle>
        </CardHeader>
        <CardContent>
          {emitidas === null ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : emitidas.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no se emitió ninguna liquidación.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>N.º</TableHead>
                  <TableHead>Repartidor</TableHead>
                  <TableHead>Período</TableHead>
                  <TableHead className="text-right">Rutas</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                  <TableHead>Emitida</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {emitidas.map((l) => (
                  <TableRow key={l.id}>
                    <TableCell>
                      <Link className="underline" href={`/liquidaciones/${l.id}`}>
                        #{l.id}
                      </Link>
                    </TableCell>
                    <TableCell>{l.repartidorNombre}</TableCell>
                    <TableCell>
                      {l.desde} al {l.hasta}
                    </TableCell>
                    <TableCell className="text-right">{l.cantidadRutas}</TableCell>
                    <TableCell className="text-right">{pesos(l.total)}</TableCell>
                    <TableCell>{new Date(l.emitidaEn).toLocaleDateString("es-AR")}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
