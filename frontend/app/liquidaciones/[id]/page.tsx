"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { LiquidacionDetalle } from "@/lib/dominio/tipos";
import { TablaRutasLiquidadas, pesos } from "../TablaRutasLiquidadas";

export default function LiquidacionPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <Comprobante />
    </RequireRole>
  );
}

/** Comprobante de liquidación (acta RF-41): se imprime desde el navegador o se baja en CSV. */
function Comprobante() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();
  const [liq, setLiq] = useState<LiquidacionDetalle | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion(`/api/liquidaciones/${id}`)
      .then((r) => leerJson<LiquidacionDetalle>(r))
      .then(setLiq)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la liquidación."));
  }, [fetchConSesion, id]);

  async function descargarCsv() {
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/liquidaciones/${id}/csv`);
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const url = URL.createObjectURL(await resp.blob());
      const a = document.createElement("a");
      a.href = url;
      a.download = `liquidacion-${id}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo descargar.");
    }
  }

  if (!liq) {
    return (
      <div className="p-4 md:p-8">
        <CabeceraSesion titulo="Liquidación" />
        <p className={error ? "text-sm text-destructive" : "text-muted-foreground"}>{error ?? "Cargando…"}</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6 max-w-5xl">
      <CabeceraSesion titulo={`Liquidación #${liq.id}`} />
      <div className="flex flex-wrap gap-2 print:hidden">
        <Button variant="outline" render={<Link href="/liquidaciones" />} nativeButton={false}>
          ← Liquidaciones
        </Button>
        <Button variant="outline" onClick={() => window.print()}>
          Imprimir
        </Button>
        <Button variant="outline" onClick={descargarCsv}>
          Descargar CSV
        </Button>
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {liq.repartidorNombre} — {liq.desde} al {liq.hasta}
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="grid gap-2 text-sm sm:grid-cols-3">
            <Dato etiqueta="Rutas" valor={String(liq.cantidadRutas)} />
            <Dato etiqueta="Total" valor={pesos(liq.total)} />
            <Dato
              etiqueta="Emitida"
              valor={`${new Date(liq.emitidaEn).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short", hour12: false })} por ${liq.emitidaPorNombre}`}
            />
          </div>
          {liq.nota && <p className="text-sm">Nota: {liq.nota}</p>}
          <TablaRutasLiquidadas rutas={liq.rutas} total={liq.total} />
        </CardContent>
      </Card>
    </div>
  );
}

function Dato({ etiqueta, valor }: { etiqueta: string; valor: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-muted-foreground">{etiqueta}</span>
      <span className="font-medium">{valor}</span>
    </div>
  );
}
