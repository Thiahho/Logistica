"use client";

import { useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { RangoRecalculado } from "@/lib/dominio/tipos";

const NOMBRES: Record<string, string> = {
  sin_rango: "Sin rango",
  bronce: "Bronce",
  plata: "Plata",
  oro: "Oro",
  empresa: "Empresa",
};

/** Los últimos cuatro trimestres ya cerrados, del más reciente al más viejo ("2026-T2"). Uno en curso no
 * se recalcula: daría un rango con datos incompletos. */
function trimestresCerrados(): string[] {
  const hoy = new Date();
  let anio = hoy.getFullYear();
  let t = Math.floor(hoy.getMonth() / 3); // el trimestre en curso es t + 1; el último cerrado es t
  const lista: string[] = [];
  for (let i = 0; i < 4; i++) {
    if (t === 0) {
      anio -= 1;
      t = 4;
    }
    lista.push(`${anio}-T${t}`);
    t -= 1;
  }
  return lista;
}

export default function RangosPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <Recalculo />
    </RequireRole>
  );
}

/**
 * B3 — recálculo trimestral de rangos (acta RF-42, D4). Lo dispara administración: el sistema no tiene
 * scheduler. Primero se ve qué le tocaría a cada cliente con lo medido; aplicar escribe el rango y deja
 * historial de cada cambio. Recalcular dos veces el mismo trimestre no cambia nada.
 */
function Recalculo() {
  const { fetchConSesion } = useAuth();
  const opciones = trimestresCerrados();
  const [trimestre, setTrimestre] = useState(opciones[0]);
  const [resultado, setResultado] = useState<RangoRecalculado[] | null>(null);
  const [aplicado, setAplicado] = useState(false);
  const [trabajando, setTrabajando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function llamar(url: string) {
    setError(null);
    setTrabajando(true);
    try {
      const r = await fetchConSesion(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ trimestre }),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setResultado(await leerJson<RangoRecalculado[]>(r));
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo recalcular.");
      return false;
    } finally {
      setTrabajando(false);
    }
  }

  const cambian = resultado?.filter((r) => r.efectivoAnterior !== r.efectivoNuevo).length ?? 0;

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6 max-w-5xl">
      <CabeceraSesion titulo="Rangos — recálculo trimestral" />
      <Button variant="outline" render={<Link href="/clientes" />} nativeButton={false} className="self-start">
        ← Clientes
      </Button>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Trimestre</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            Umbrales y descuentos de cada rango en{" "}
            <Link className="underline" href="/tarifas">
              Tarifas
            </Link>
            . Un ajuste manual vigente se sigue aplicando sobre el resultado; uno vencido se limpia.
          </p>
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-2">
              <Label>Trimestre cerrado</Label>
              <Select
                items={opciones.map((o) => ({ value: o, label: o }))}
                value={trimestre}
                onValueChange={(v) => {
                  if (v) setTrimestre(v);
                  setResultado(null);
                  setAplicado(false);
                }}
              >
                <SelectTrigger className="w-36">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {opciones.map((o) => (
                    <SelectItem key={o} value={o}>
                      {o}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button
              variant="outline"
              disabled={trabajando}
              onClick={async () => {
                setAplicado(false);
                await llamar("/api/rangos/recalculo/previsualizacion");
              }}
            >
              Ver resultado
            </Button>
            {resultado && !aplicado && (
              <Button
                disabled={trabajando}
                onClick={async () => {
                  if (await llamar("/api/rangos/recalculo")) setAplicado(true);
                }}
              >
                Aplicar recálculo ({cambian} cambian)
              </Button>
            )}
          </div>
          {aplicado && <p className="text-sm text-muted-foreground">Recálculo aplicado. Cada cambio quedó en el historial del cliente.</p>}
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      {resultado && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">
              {resultado.length} clientes activos · {cambian} cambian de rango
            </CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Cliente</TableHead>
                  <TableHead className="text-right">Envíos</TableHead>
                  <TableHead className="text-right">Facturación</TableHead>
                  <TableHead className="text-right">Semanas</TableHead>
                  <TableHead className="text-right">Antigüedad</TableHead>
                  <TableHead className="text-right">Pagos en término</TableHead>
                  <TableHead>Rango</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {resultado.map((r) => (
                  <TableRow key={r.clienteId}>
                    <TableCell>
                      <Link className="underline" href={`/clientes/${r.clienteId}`}>
                        {r.razonSocial}
                      </Link>
                    </TableCell>
                    <TableCell className="text-right">{r.criterios.envios}</TableCell>
                    <TableCell className="text-right">${r.criterios.facturacion.toLocaleString("es-AR")}</TableCell>
                    <TableCell className="text-right">{r.criterios.semanasActivas}</TableCell>
                    <TableCell className="text-right">{r.criterios.antiguedadMeses} meses</TableCell>
                    <TableCell className="text-right">
                      {r.criterios.facturasVencidas > 0 ? `${r.criterios.pctPagosEnTermino}%` : "sin vencimientos"}
                    </TableCell>
                    <TableCell>
                      <span className={r.efectivoAnterior !== r.efectivoNuevo ? "font-medium" : undefined}>
                        {NOMBRES[r.efectivoAnterior]}
                        {r.efectivoAnterior !== r.efectivoNuevo && ` → ${NOMBRES[r.efectivoNuevo]}`}
                      </span>
                      {r.ajusteVencido && <span className="block text-xs text-muted-foreground">ajuste manual vencido</span>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
