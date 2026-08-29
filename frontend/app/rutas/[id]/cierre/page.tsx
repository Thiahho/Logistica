"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError } from "@/lib/api/errores";
import type { ResultadoRuta, RutaDetalle } from "@/lib/dominio/tipos";

export default function CierreRutaPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <CierreRuta />
    </RequireRole>
  );
}

function CierreRuta() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();

  const [ruta, setRuta] = useState<RutaDetalle | null>(null);
  const [resultado, setResultado] = useState<ResultadoRuta | null>(null);

  const [kmInicial, setKmInicial] = useState("");
  const [kmFinal, setKmFinal] = useState("");
  const [combustible, setCombustible] = useState("");
  const [peajes, setPeajes] = useState("");
  const [otros, setOtros] = useState("");
  const [pagoRepartidor, setPagoRepartidor] = useState("");
  const [notas, setNotas] = useState("");

  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/rutas/${id}`)
      .then((r) => r.json())
      .then(setRuta);
  }, [fetchConSesion, id]);

  useEffect(cargar, [cargar]);

  useEffect(() => {
    if (ruta?.estado === "cerrada") {
      fetchConSesion(`/api/rutas/${id}/resultado`)
        .then((r) => r.json())
        .then(setResultado);
    }
  }, [ruta?.estado, fetchConSesion, id]);

  async function cerrar(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion(`/api/rutas/${id}/cierre`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          kmInicial: Number(kmInicial),
          kmFinal: Number(kmFinal),
          combustibleMonto: Number(combustible) || 0,
          peajesMonto: Number(peajes) || 0,
          otrosCostos: Number(otros) || 0,
          pagoRepartidor: Number(pagoRepartidor) || 0,
          notasCierre: notas || null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setResultado(await resp.json());
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cerrar la ruta.");
    } finally {
      setEnviando(false);
    }
  }

  if (!ruta) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Cierre de ruta" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Cierre de ruta #${ruta.id} — ${ruta.fecha}`} />
      <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false} className="self-start">
        ← Rutas
      </Button>

      {ruta.estado !== "cerrada" ? (
        <form onSubmit={cerrar} className="flex flex-col gap-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Kilómetros y costos</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-2">
                <Label htmlFor="km-inicial">Km inicial</Label>
                <Input
                  id="km-inicial"
                  type="number"
                  required
                  value={kmInicial}
                  onChange={(e) => setKmInicial(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="km-final">Km final</Label>
                <Input
                  id="km-final"
                  type="number"
                  required
                  value={kmFinal}
                  onChange={(e) => setKmFinal(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="combustible">Combustible</Label>
                <Input
                  id="combustible"
                  type="number"
                  step="0.01"
                  value={combustible}
                  onChange={(e) => setCombustible(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="peajes">Peajes</Label>
                <Input
                  id="peajes"
                  type="number"
                  step="0.01"
                  value={peajes}
                  onChange={(e) => setPeajes(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="otros">Otros costos</Label>
                <Input
                  id="otros"
                  type="number"
                  step="0.01"
                  value={otros}
                  onChange={(e) => setOtros(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="pago-repartidor">Pago al repartidor</Label>
                <Input
                  id="pago-repartidor"
                  type="number"
                  step="0.01"
                  value={pagoRepartidor}
                  onChange={(e) => setPagoRepartidor(e.target.value)}
                />
              </div>
              <div className="col-span-2 flex flex-col gap-2">
                <Label htmlFor="notas">Notas de cierre (opcional)</Label>
                <Input id="notas" value={notas} onChange={(e) => setNotas(e.target.value)} />
              </div>
            </CardContent>
          </Card>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <Button type="submit" disabled={enviando}>
            {enviando ? "Cerrando…" : "Cerrar ruta"}
          </Button>
        </form>
      ) : (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Ruta cerrada</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2 text-sm">
            <Fila
              etiqueta="Km recorridos"
              valor={ruta.kmInicial !== null && ruta.kmFinal !== null ? String(ruta.kmFinal - ruta.kmInicial) : "—"}
            />
            <Fila etiqueta="Combustible" valor={`$${(ruta.combustibleMonto ?? 0).toLocaleString("es-AR")}`} />
            <Fila etiqueta="Peajes" valor={`$${(ruta.peajesMonto ?? 0).toLocaleString("es-AR")}`} />
            <Fila etiqueta="Otros costos" valor={`$${(ruta.otrosCostos ?? 0).toLocaleString("es-AR")}`} />
            <Fila etiqueta="Pago al repartidor" valor={`$${(ruta.pagoRepartidor ?? 0).toLocaleString("es-AR")}`} />
            {ruta.notasCierre && <Fila etiqueta="Notas" valor={ruta.notasCierre} />}
          </CardContent>
        </Card>
      )}

      {resultado && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Resultado económico</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2 text-sm">
            <Fila etiqueta="Ingresos" valor={`$${resultado.ingresos.toLocaleString("es-AR")}`} />
            <Fila etiqueta="Costos" valor={`$${resultado.costos.toLocaleString("es-AR")}`} />
            <div className="flex justify-between border-t pt-2 font-semibold">
              <span>Margen</span>
              <span className={resultado.margen < 0 ? "text-destructive" : undefined}>
                ${resultado.margen.toLocaleString("es-AR")}
              </span>
            </div>
            <div className="grid grid-cols-3 gap-4 text-center pt-2 border-t">
              <div className="rounded-lg border p-3">
                <p className="text-2xl font-semibold">{resultado.efectivas}</p>
                <p className="text-xs text-muted-foreground">Efectivas</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-2xl font-semibold">{resultado.fallidas}</p>
                <p className="text-xs text-muted-foreground">Fallidas</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-2xl font-semibold">{resultado.reprogramadas}</p>
                <p className="text-xs text-muted-foreground">Reprogramadas</p>
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function Fila({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <span className="text-muted-foreground shrink-0">{etiqueta}</span>
      <span className="text-right">{valor}</span>
    </div>
  );
}
