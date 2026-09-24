"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { RangoConfig } from "@/lib/dominio/tipos";

/** Campos editables de un rango; vacío = sin umbral (no exige nada) o sin límite. */
const CAMPOS = [
  { clave: "minEnviosTrimestre", etiqueta: "Envíos / trim.", paso: "1" },
  { clave: "minFacturacionTrimestre", etiqueta: "Facturación / trim. $", paso: "0.01" },
  { clave: "minAntiguedadMeses", etiqueta: "Antigüedad (meses)", paso: "1" },
  { clave: "minSemanasActivas", etiqueta: "Semanas activas", paso: "1" },
  { clave: "minPctPagosEnTermino", etiqueta: "Pagos en término %", paso: "0.01" },
  { clave: "descuentoPct", etiqueta: "Descuento %", paso: "0.01" },
  { clave: "limiteCredito", etiqueta: "Límite de crédito $", paso: "0.01" },
  { clave: "prioridad", etiqueta: "Prioridad al armar", paso: "1" },
] as const;

type Clave = (typeof CAMPOS)[number]["clave"];
type Borrador = Record<Clave, string>;

const UMBRALES: Clave[] = [
  "minEnviosTrimestre",
  "minFacturacionTrimestre",
  "minAntiguedadMeses",
  "minSemanasActivas",
  "minPctPagosEnTermino",
];

function borradorDe(r: RangoConfig): Borrador {
  return Object.fromEntries(CAMPOS.map(({ clave }) => [clave, r[clave] === null ? "" : String(r[clave])])) as Borrador;
}

const numero = (v: string) => (v.trim() === "" ? null : Number(v));

/**
 * B3 (acta RF-42, definiciones J y F del Anexo I): umbrales y efectos de cada rango. Configuración de la
 * Empresa, sin valores de fábrica. Un umbral vacío no exige nada; un rango sin ningún umbral no se
 * alcanza por cálculo, solo por ajuste manual. Cambiar un umbral no mueve a nadie hasta el próximo
 * recálculo trimestral; el descuento rige para lo que se cotice desde ahora.
 */
export function RangosConfig() {
  const { fetchConSesion } = useAuth();
  const [rangos, setRangos] = useState<RangoConfig[] | null>(null);
  const [borradores, setBorradores] = useState<Record<string, Borrador>>({});
  const [guardando, setGuardando] = useState<string | null>(null);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion("/api/rangos")
      .then((r) => leerJson<RangoConfig[]>(r))
      .then((lista) => {
        setRangos(lista);
        setBorradores(Object.fromEntries(lista.map((r) => [r.codigo, borradorDe(r)])));
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los rangos."));
  }, [fetchConSesion]);

  useEffect(cargar, [cargar]);

  async function guardar(codigo: string) {
    const b = borradores[codigo];
    setError(null);
    setMensaje(null);
    setGuardando(codigo);
    try {
      const r = await fetchConSesion(`/api/rangos/${codigo}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          minEnviosTrimestre: numero(b.minEnviosTrimestre),
          minFacturacionTrimestre: numero(b.minFacturacionTrimestre),
          minAntiguedadMeses: numero(b.minAntiguedadMeses),
          minSemanasActivas: numero(b.minSemanasActivas),
          minPctPagosEnTermino: numero(b.minPctPagosEnTermino),
          descuentoPct: numero(b.descuentoPct) ?? 0,
          limiteCredito: numero(b.limiteCredito),
          prioridad: numero(b.prioridad) ?? 0,
        }),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setMensaje("Guardado.");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Rangos de cliente</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">
          Cada trimestre, un cliente queda en el rango más alto cuyos umbrales cumple todos (un umbral vacío no exige
          nada). El descuento se aplica sobre la tarifa general, no sobre la lista propia de un cliente. El límite de
          crédito solo avisa al cargar un pedido. La prioridad ordena los pedidos pendientes al armar una ruta, nunca las
          paradas.{" "}
          <Link className="underline" href="/clientes/rangos">
            Recalcular el trimestre
          </Link>
        </p>
        {!rangos ? (
          <p className="text-sm text-muted-foreground">{error ?? "Cargando…"}</p>
        ) : (
          <div className="overflow-x-auto">
            <Table tarjetas={false}>
              <TableHeader>
                <TableRow>
                  <TableHead>Rango</TableHead>
                  {CAMPOS.map((c) => (
                    <TableHead key={c.clave} className="min-w-28">
                      {c.etiqueta}
                    </TableHead>
                  ))}
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {rangos.map((r) => {
                  const b = borradores[r.codigo];
                  if (!b) return null;
                  const esSinRango = r.codigo === "sin_rango";
                  return (
                    <TableRow key={r.codigo}>
                      <TableCell className="font-medium">{r.nombre}</TableCell>
                      {CAMPOS.map((c) => (
                        <TableCell key={c.clave}>
                          <Input
                            type="number"
                            min="0"
                            step={c.paso}
                            className="w-28"
                            aria-label={`${c.etiqueta} de ${r.nombre}`}
                            disabled={esSinRango && UMBRALES.includes(c.clave)}
                            placeholder={esSinRango && UMBRALES.includes(c.clave) ? "—" : ""}
                            value={b[c.clave]}
                            onChange={(e) =>
                              setBorradores((todos) => ({ ...todos, [r.codigo]: { ...b, [c.clave]: e.target.value } }))
                            }
                          />
                        </TableCell>
                      ))}
                      <TableCell>
                        <Button size="sm" disabled={guardando === r.codigo} onClick={() => guardar(r.codigo)}>
                          Guardar
                        </Button>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
        {mensaje && <p className="text-sm text-muted-foreground">{mensaje}</p>}
        {rangos && error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  );
}
