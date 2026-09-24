"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ObjetivoRentabilidad } from "@/lib/dominio/tipos";

const FUENTES_FIJAS: { valor: string; etiqueta: string }[] = [
  { valor: "pago_repartidor", etiqueta: "Pago a repartidores" },
  { valor: "combustible", etiqueta: "Combustible" },
  { valor: "peajes", etiqueta: "Peajes" },
  { valor: "otros_costos", etiqueta: "Otros costos de ruta" },
  { valor: "fijos", etiqueta: "Todos los fijos" },
  { valor: "margen", etiqueta: "Margen" },
];

interface Borrador {
  nombre: string;
  pctMin: string;
  pctMax: string;
  fuentes: string[];
}

/**
 * B7 (acta RF-43): la estructura económica objetivo, con nombre libre por tramo — la Empresa todavía no
 * definió qué es cada uno (la infografía dice 60-65% / 10-15% / 20-30%). Se guarda entera, no tramo por
 * tramo: son pocos y se piensan juntos.
 */
export function EstructuraObjetivo({ categorias, alGuardar }: { categorias: string[]; alGuardar: () => void }) {
  const { fetchConSesion } = useAuth();
  const [tramos, setTramos] = useState<Borrador[] | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion("/api/objetivos-rentabilidad")
      .then((r) => leerJson<ObjetivoRentabilidad[]>(r))
      .then((lista) =>
        setTramos(lista.map((t) => ({ nombre: t.nombre, pctMin: String(t.pctMin), pctMax: String(t.pctMax), fuentes: t.fuentes }))),
      )
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la estructura objetivo."));
  }, [fetchConSesion]);

  useEffect(cargar, [cargar]);

  const fuentes = [
    ...FUENTES_FIJAS,
    ...categorias.map((c) => ({ valor: `fijos:${c}`, etiqueta: `Fijos: ${c}` })),
  ];

  function cambiar(i: number, cambios: Partial<Borrador>) {
    setTramos((t) => t!.map((x, j) => (j === i ? { ...x, ...cambios } : x)));
  }

  async function guardar() {
    setError(null);
    setMensaje(null);
    setGuardando(true);
    try {
      const r = await fetchConSesion("/api/objetivos-rentabilidad", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(
          tramos!.map((t) => ({ nombre: t.nombre, pctMin: Number(t.pctMin), pctMax: Number(t.pctMax), fuentes: t.fuentes })),
        ),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setMensaje("Estructura guardada.");
      alGuardar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Estructura objetivo</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {!tramos ? (
          <p className="text-sm text-muted-foreground">{error ?? "Cargando…"}</p>
        ) : (
          <>
            {tramos.length === 0 && (
              <p className="text-sm text-muted-foreground">Sin tramos. Agregá uno por cada parte de la estructura (por ejemplo, costos operativos, fijos y margen).</p>
            )}
            {tramos.map((t, i) => (
              <fieldset key={i} className="flex flex-col gap-3 rounded-lg border p-3">
                <div className="grid gap-3 sm:grid-cols-[1fr_auto_auto_auto] sm:items-center">
                  <Input aria-label="Nombre del tramo" placeholder="Nombre" maxLength={100} value={t.nombre} onChange={(e) => cambiar(i, { nombre: e.target.value })} />
                  <label className="flex items-center gap-2 text-sm">
                    <span className="w-14 shrink-0 text-muted-foreground sm:w-auto">Mín %</span>
                    <Input type="number" min="0" max="100" step="0.01" className="sm:w-24" value={t.pctMin} onChange={(e) => cambiar(i, { pctMin: e.target.value })} />
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <span className="w-14 shrink-0 text-muted-foreground sm:w-auto">Máx %</span>
                    <Input type="number" min="0" max="100" step="0.01" className="sm:w-24" value={t.pctMax} onChange={(e) => cambiar(i, { pctMax: e.target.value })} />
                  </label>
                  <Button type="button" size="sm" variant="outline" onClick={() => setTramos(tramos.filter((_, j) => j !== i))}>
                    Quitar
                  </Button>
                </div>
                <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
                  {fuentes.map((f) => (
                    <label key={f.valor} className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={t.fuentes.includes(f.valor)}
                        onChange={(e) =>
                          cambiar(i, {
                            fuentes: e.target.checked ? [...t.fuentes, f.valor] : t.fuentes.filter((x) => x !== f.valor),
                          })
                        }
                      />
                      {f.etiqueta}
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => setTramos([...tramos, { nombre: "", pctMin: "", pctMax: "", fuentes: [] }])}
              >
                Agregar tramo
              </Button>
              <Button type="button" disabled={guardando} onClick={guardar}>
                {guardando ? "Guardando…" : "Guardar estructura"}
              </Button>
            </div>
          </>
        )}
        {mensaje && <p className="text-sm text-muted-foreground">{mensaje}</p>}
        {tramos && error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  );
}
