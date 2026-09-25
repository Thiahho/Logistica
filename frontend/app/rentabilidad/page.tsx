"use client";

import { useCallback, useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ResultadoMes } from "@/lib/dominio/tipos";
import { EstructuraObjetivo } from "./EstructuraObjetivo";

const pesos = (n: number) => `$${n.toLocaleString("es-AR")}`;

function mesActual(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

const ESTADOS = {
  debajo: { texto: "Debajo", clase: "text-amber-700" },
  dentro: { texto: "Dentro", clase: "text-emerald-700" },
  encima: { texto: "Encima", clase: "text-amber-700" },
} as const;

export default function RentabilidadPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <Rentabilidad />
    </RequireRole>
  );
}

/**
 * B7 — costos fijos y resultado del mes contra la estructura objetivo (acta RF-43, changelog 4.21).
 * Ingresos: lo facturable que nació en el mes. Costos variables: las rutas cerradas del mes. Fijos: los
 * cargados acá. No es el tablero de indicadores (B2): es la cuenta del mes en plata.
 */
function Rentabilidad() {
  const { fetchConSesion } = useAuth();
  const [mes, setMes] = useState(mesActual);
  const [datos, setDatos] = useState<ResultadoMes | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [categoria, setCategoria] = useState("");
  const [descripcion, setDescripcion] = useState("");
  const [monto, setMonto] = useState("");
  const [trabajando, setTrabajando] = useState(false);

  const cargar = useCallback(() => {
    if (!mes) return;
    fetchConSesion(`/api/rentabilidad?mes=${mes}`)
      .then((r) => leerJson<ResultadoMes>(r))
      .then((d) => {
        setDatos(d);
        setError(null);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo calcular el mes."));
  }, [fetchConSesion, mes]);

  useEffect(cargar, [cargar]);

  async function accion(url: string, init: RequestInit, alTerminar?: () => void) {
    setError(null);
    setTrabajando(true);
    try {
      const r = await fetchConSesion(url, { headers: { "Content-Type": "application/json" }, ...init });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      alTerminar?.();
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setTrabajando(false);
    }
  }

  function agregarCosto(e: React.FormEvent) {
    e.preventDefault();
    accion(
      "/api/costos-fijos",
      { method: "POST", body: JSON.stringify({ mes, categoria, descripcion: descripcion || null, monto: Number(monto) }) },
      () => {
        setDescripcion("");
        setMonto("");
      },
    );
  }

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6 max-w-5xl">
      <CabeceraSesion titulo="Rentabilidad" />

      <div className="flex flex-col gap-2 self-start">
        <Label htmlFor="mes">Mes</Label>
        <Input id="mes" type="month" value={mes} onChange={(e) => setMes(e.target.value)} className="w-44" />
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}

      {datos && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <TarjetaMetrica valor={pesos(datos.ingresos)} etiqueta="Ingresos" />
            <TarjetaMetrica valor={pesos(datos.variables)} etiqueta={`Variables (${datos.rutasCerradas} rutas)`} />
            <TarjetaMetrica valor={pesos(datos.fijos)} etiqueta="Fijos" />
            <TarjetaMetrica
              valor={`${pesos(datos.margen)}${datos.pctMargen !== null ? ` · ${datos.pctMargen}%` : ""}`}
              etiqueta="Margen"
              tono={datos.margen < 0 ? "alerta" : "normal"}
            />
          </div>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Contra la estructura objetivo</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              {datos.tramos.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  Todavía no hay estructura objetivo cargada. Estos son los números del mes sobre los ingresos:
                </p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Tramo</TableHead>
                      <TableHead className="text-right">Monto</TableHead>
                      <TableHead className="text-right">Real</TableHead>
                      <TableHead className="text-right">Objetivo</TableHead>
                      <TableHead>Estado</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {datos.tramos.map((t) => (
                      <TableRow key={t.id}>
                        <TableCell className="font-medium">{t.nombre}</TableCell>
                        <TableCell className="text-right">{pesos(t.monto)}</TableCell>
                        <TableCell className="text-right">{t.pct !== null ? `${t.pct}%` : "—"}</TableCell>
                        <TableCell className="text-right">
                          {t.pctMin}% a {t.pctMax}%
                        </TableCell>
                        <TableCell className={t.estado ? ESTADOS[t.estado].clase : "text-muted-foreground"}>
                          {t.estado ? ESTADOS[t.estado].texto : "sin ingresos"}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
              {datos.tramos.length === 0 && (
                <ul className="text-sm">
                  {[
                    ["Pago a repartidores", datos.pagoRepartidor],
                    ["Combustible", datos.combustible],
                    ["Peajes", datos.peajes],
                    ["Otros costos de ruta", datos.otrosCostos],
                    ["Costos fijos", datos.fijos],
                    ["Margen", datos.margen],
                  ].map(([nombre, valor]) => (
                    <li key={nombre as string} className="flex justify-between border-b py-1">
                      <span>{nombre}</span>
                      <span>
                        {pesos(valor as number)}
                        {datos.ingresos > 0 && ` · ${Math.round(((valor as number) * 10000) / datos.ingresos) / 100}%`}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Costos fijos del mes</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              {datos.costosFijos.length === 0 ? (
                <div className="flex flex-wrap items-center gap-3 text-sm">
                  <span className="text-muted-foreground">No hay costos fijos cargados para este mes.</span>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={trabajando}
                    onClick={() =>
                      accion("/api/costos-fijos/copiar-mes-anterior", { method: "POST", body: JSON.stringify({ mes }) })
                    }
                  >
                    Copiar los del mes anterior
                  </Button>
                </div>
              ) : (
                <ul className="flex flex-col text-sm">
                  {datos.costosFijos.map((c) => (
                    <li key={c.id} className="flex items-center justify-between gap-3 border-b py-2">
                      <span>
                        <span className="font-medium">{c.categoria}</span>
                        {c.descripcion && <span className="text-muted-foreground"> · {c.descripcion}</span>}
                      </span>
                      <span className="flex items-center gap-3">
                        {pesos(c.monto)}
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={trabajando}
                          aria-label={`Borrar ${c.categoria}`}
                          onClick={() => accion(`/api/costos-fijos/${c.id}`, { method: "DELETE" })}
                        >
                          Borrar
                        </Button>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <form onSubmit={agregarCosto} className="grid gap-3 sm:grid-cols-[1fr_1fr_auto_auto] sm:items-end">
                <div className="flex flex-col gap-2">
                  <Label htmlFor="categoria">Categoría</Label>
                  <Input id="categoria" required maxLength={100} value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="alquiler, seguros…" />
                </div>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="descripcion">Descripción (opcional)</Label>
                  <Input id="descripcion" maxLength={500} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
                </div>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="monto">Monto</Label>
                  <Input id="monto" type="number" min="0" step="0.01" required value={monto} onChange={(e) => setMonto(e.target.value)} className="sm:w-36" />
                </div>
                <Button type="submit" disabled={trabajando}>
                  Agregar
                </Button>
              </form>
            </CardContent>
          </Card>

          <EstructuraObjetivo categorias={Object.keys(datos.fijosPorCategoria)} alGuardar={cargar} />
        </>
      )}
    </div>
  );
}
