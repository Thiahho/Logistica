"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraRepartidor } from "@/components/CabeceraRepartidor";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { leerError, leerJson } from "@/lib/api/errores";
import type { CierreJornadaResultado, JornadaDelDia } from "@/lib/dominio/tipos";

export default function CierreJornadaPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <CierreJornada />
    </RequireRole>
  );
}

// Persistido por ruta hasta que el servidor confirma: el reintento del mismo botón tiene que
// reusar el uuid para que se reconozca como duplicado (RNF-02).
const PREFIJO_UUID = "logistica:device-uuid:cierre:";

function uuidDeCierre(rutaId: number): string {
  try {
    const existente = localStorage.getItem(PREFIJO_UUID + rutaId);
    if (existente) return existente;
    const nuevo = crypto.randomUUID();
    localStorage.setItem(PREFIJO_UUID + rutaId, nuevo);
    return nuevo;
  } catch {
    return crypto.randomUUID();
  }
}

function limpiarUuidDeCierre(rutaId: number) {
  try {
    localStorage.removeItem(PREFIJO_UUID + rutaId);
  } catch {
    // no-op
  }
}

function CierreJornada() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [jornada, setJornada] = useState<JornadaDelDia | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [kmFinal, setKmFinal] = useState("");
  const [combustible, setCombustible] = useState("");
  const [peajes, setPeajes] = useState("");
  const [notas, setNotas] = useState("");

  const [enviando, setEnviando] = useState(false);
  const [envioError, setEnvioError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<CierreJornadaResultado | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas/dia")
      .then((r) => leerJson<JornadaDelDia>(r))
      .then(setJornada)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la jornada."));
  }, [fetchConSesion]);

  if (errorCarga) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Cierre de jornada" />
        <p className="text-sm text-destructive">{errorCarga}</p>
      </div>
    );
  }

  if (!jornada) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Cierre de jornada" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }

  if (jornada.rutaId === null) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Cierre de jornada" />
        <p className="text-muted-foreground">No tenés una ruta en curso.</p>
      </div>
    );
  }

  const rutaId = jornada.rutaId;
  const pendientes = jornada.total - jornada.completadas - jornada.fallidas - jornada.canceladas;
  const yaCerrada = jornada.cierreRepartidorEn !== null || resultado !== null;

  async function confirmar() {
    setEnviando(true);
    setEnvioError(null);
    try {
      const resp = await fetchConSesion("/api/mi-jornada/cierre", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          deviceUuid: uuidDeCierre(rutaId),
          capturadaEn: new Date().toISOString(),
          kmFinal: Number(kmFinal),
          combustibleMonto: Number(combustible) || 0,
          peajesMonto: Number(peajes) || 0,
          notas: notas.trim() || null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setResultado(await leerJson<CierreJornadaResultado>(resp));
      limpiarUuidDeCierre(rutaId);
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo cerrar la jornada.");
    } finally {
      setEnviando(false);
    }
  }

  if (yaCerrada) {
    return (
      <div className="p-4 flex flex-col gap-4">
        <CabeceraRepartidor titulo="Cierre de jornada" />
        <div className="rounded-2xl border-2 border-green-600 bg-green-50/60 p-4 flex flex-col gap-2">
          <p className="font-semibold">Jornada cerrada</p>
          {resultado && (
            <p className="text-sm">
              {resultado.entregadas} entregadas · {resultado.fallidas} fallidas · {resultado.kmRecorridos} km
            </p>
          )}
          <p className="text-sm text-muted-foreground">
            Pendiente de revisión de administración: lo que cargaste todavía no es el cierre de la ruta.
          </p>
        </div>
        <Button className="h-12 text-base" onClick={() => router.replace("/hoy")}>
          Volver a Hoy
        </Button>
      </div>
    );
  }

  const listo = pendientes === 0 && kmFinal !== "" && Number(kmFinal) >= 0;

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <CabeceraRepartidor titulo="Cerrar la jornada" volverA="/hoy" />

      <div className="rounded-2xl border bg-card shadow-sm p-4 grid grid-cols-3 text-center">
        <div>
          <p className="text-2xl font-bold text-bf-azul">{jornada.completadas}</p>
          <p className="text-xs text-muted-foreground">Entregadas</p>
        </div>
        <div>
          <p className="text-2xl font-bold text-bf-azul">{jornada.fallidas}</p>
          <p className="text-xs text-muted-foreground">Fallidas</p>
        </div>
        <div>
          <p className="text-2xl font-bold text-bf-azul">{pendientes}</p>
          <p className="text-xs text-muted-foreground">Pendientes</p>
        </div>
      </div>

      {pendientes > 0 && (
        <p className="rounded-lg border border-amber-500 bg-amber-50/60 p-3 text-sm">
          Te quedan {pendientes} parada(s) pendientes. La jornada se cierra cuando la calle terminó.
        </p>
      )}

      <div className="flex flex-col gap-2">
        <Label htmlFor="km-final">Kilometraje del odómetro al volver</Label>
        <Input
          id="km-final"
          type="number"
          inputMode="numeric"
          min={0}
          className="h-12 text-base"
          value={kmFinal}
          onChange={(e) => setKmFinal(e.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="combustible">Combustible que pagaste ($)</Label>
        <Input
          id="combustible"
          type="number"
          inputMode="decimal"
          min={0}
          step="0.01"
          className="h-12 text-base"
          value={combustible}
          onChange={(e) => setCombustible(e.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="peajes">Peajes que pagaste ($)</Label>
        <Input
          id="peajes"
          type="number"
          inputMode="decimal"
          min={0}
          step="0.01"
          className="h-12 text-base"
          value={peajes}
          onChange={(e) => setPeajes(e.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="notas">Notas (opcional)</Label>
        <Textarea id="notas" className="text-base" value={notas} onChange={(e) => setNotas(e.target.value)} />
      </div>

      {envioError && <p className="text-sm text-destructive">{envioError}</p>}

      <Button className="h-12 w-full text-base" disabled={!listo || enviando} onClick={confirmar}>
        {enviando ? "Enviando…" : "Cerrar la jornada"}
      </Button>
    </div>
  );
}
