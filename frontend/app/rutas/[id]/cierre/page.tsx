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
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { leerError, leerJson } from "@/lib/api/errores";
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
  const [sinDeclaracion, setSinDeclaracion] = useState(false);

  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/rutas/${id}`)
      .then((r) => leerJson<RutaDetalle>(r))
      .then((r) => {
        setRuta(r);
        // M1: el formulario arranca con lo que declaró la calle, no vacío — así "aprobar" es no
        // tocar nada y corregir es cambiar un número a propósito. otros_costos y pago_repartidor
        // siguen vacíos: no los declara nadie más que administración. Solo si todavía no se
        // escribió nada (una recarga no pisa lo que el usuario ya tipeó).
        if (r.estado !== "cerrada" && r.cierreRepartidorEn) {
          setKmInicial((v) => v || String(r.retiroKmInicial ?? ""));
          setKmFinal((v) => v || String(r.cierreRepartidorKmFinal ?? ""));
          setCombustible((v) => v || String(r.cierreRepartidorCombustible ?? ""));
          setPeajes((v) => v || String(r.cierreRepartidorPeajes ?? ""));
        }
      })
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la ruta."));
  }, [fetchConSesion, id]);

  useEffect(cargar, [cargar]);

  useEffect(() => {
    if (ruta?.estado === "cerrada") {
      fetchConSesion(`/api/rutas/${id}/resultado`)
        .then((r) => leerJson<ResultadoRuta>(r))
        .then(setResultado)
        .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el resultado."));
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
          sinDeclaracionDelRepartidor: sinDeclaracion,
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
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Cierre de ruta #${ruta.id} — ${ruta.fecha}`} />
      <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false} className="self-start">
        ← Rutas
      </Button>

      <DeclaracionDeLaCalle
        ruta={ruta}
        actuales={{ kmInicial, kmFinal, combustible, peajes }}
        cerrada={ruta.estado === "cerrada"}
      />

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
          {!ruta.cierreRepartidorEn && (
            <label className="flex items-start gap-2 rounded-lg border border-amber-500 bg-amber-50/60 p-3 text-sm">
              <input
                type="checkbox"
                className="mt-0.5"
                checked={sinDeclaracion}
                onChange={(e) => setSinDeclaracion(e.target.checked)}
              />
              <span>El repartidor no declaró su cierre. Cerrar igual, con datos que nadie verificó en la calle.</span>
            </label>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
          <Button type="submit" disabled={enviando || (!ruta.cierreRepartidorEn && !sinDeclaracion)}>
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
            <CardTitle className="text-base">
              Resultado económico
              {resultado.aprobacion && (
                <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs font-normal">
                  {resultado.aprobacion === "tal_cual"
                    ? "Aprobado tal cual"
                    : resultado.aprobacion === "corregido"
                      ? "Corregido por administración"
                      : "Cerrado sin declaración de la calle"}
                </span>
              )}
            </CardTitle>
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
            <div className="grid grid-cols-3 gap-4 pt-2 border-t">
              <TarjetaMetrica valor={resultado.efectivas} etiqueta="Efectivas" />
              <TarjetaMetrica valor={resultado.fallidas} etiqueta="Fallidas" tono={resultado.fallidas > 0 ? "alerta" : "normal"} />
              <TarjetaMetrica valor={resultado.reprogramadas} etiqueta="Reprogramadas" />
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

/** M5: la declaración de la calle al lado de lo que administración va a cerrar, con el delta cuando
 * difieren. Solo lectura: lo declarado no se edita (trg_congelar_declaracion_repartidor). */
function DeclaracionDeLaCalle({
  ruta,
  actuales,
  cerrada,
}: {
  ruta: RutaDetalle;
  actuales: { kmInicial: string; kmFinal: string; combustible: string; peajes: string };
  cerrada: boolean;
}) {
  if (!ruta.retiroConfirmadoEn && !ruta.cierreRepartidorEn) {
    return (
      <p className="rounded-lg border p-3 text-sm text-muted-foreground">
        El repartidor todavía no cargó nada desde la calle.
      </p>
    );
  }

  const filas: { etiqueta: string; declarado: number | null; actual: string }[] = [
    { etiqueta: "Km inicial", declarado: ruta.retiroKmInicial, actual: actuales.kmInicial },
    { etiqueta: "Km final", declarado: ruta.cierreRepartidorKmFinal, actual: actuales.kmFinal },
    { etiqueta: "Combustible", declarado: ruta.cierreRepartidorCombustible, actual: actuales.combustible },
    { etiqueta: "Peajes", declarado: ruta.cierreRepartidorPeajes, actual: actuales.peajes },
  ];
  const discrepanciaBultos =
    ruta.retiroBultosContados !== null && ruta.retiroBultosContados !== ruta.retiroBultosEsperados;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Declarado por el repartidor</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        {ruta.retiroConfirmadoEn && (
          <Fila
            etiqueta="Retiro"
            valor={
              <span className={discrepanciaBultos ? "font-medium text-destructive" : undefined}>
                {ruta.retiroBultosContados} de {ruta.retiroBultosEsperados} bultos
                {discrepanciaBultos && ruta.retiroObservaciones ? ` — "${ruta.retiroObservaciones}"` : ""}
              </span>
            }
          />
        )}
        {ruta.cierreRepartidorEn ? (
          <>
            <p className="text-xs text-muted-foreground">
              Cargado desde la calle a las {new Date(ruta.cierreRepartidorEn).toLocaleTimeString()}
            </p>
            {filas.map((f) => {
              const actual = f.actual === "" ? null : Number(f.actual);
              const difiere = !cerrada && f.declarado !== null && actual !== null && actual !== f.declarado;
              return (
                <Fila
                  key={f.etiqueta}
                  etiqueta={f.etiqueta}
                  valor={
                    <span className={difiere ? "font-medium text-destructive" : undefined}>
                      {f.declarado ?? "—"}
                      {difiere && actual !== null && f.declarado !== null
                        ? ` (Δ ${(actual - f.declarado).toLocaleString("es-AR")})`
                        : ""}
                    </span>
                  }
                />
              );
            })}
            {ruta.cierreRepartidorNotas && <Fila etiqueta="Notas" valor={ruta.cierreRepartidorNotas} />}
          </>
        ) : (
          <p className="text-xs text-muted-foreground">Todavía no declaró el cierre de su jornada.</p>
        )}
      </CardContent>
    </Card>
  );
}
