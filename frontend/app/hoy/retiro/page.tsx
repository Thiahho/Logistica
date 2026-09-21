"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { PenLine } from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraRepartidor } from "@/components/CabeceraRepartidor";
import { FirmaCanvas, type FirmaCanvasHandle } from "@/components/FirmaCanvas";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { leerError, leerJson } from "@/lib/api/errores";
import { limpiarUuidDeRetiro, uuidDeRetiro } from "@/lib/captura/dispositivo";
import type { JornadaDelDia, RetiroResultado } from "@/lib/dominio/tipos";

export default function RetiroPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <Retiro />
    </RequireRole>
  );
}

function Retiro() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();
  const firmaRef = useRef<FirmaCanvasHandle>(null);

  const [jornada, setJornada] = useState<JornadaDelDia | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [contados, setContados] = useState("");
  const [kmInicial, setKmInicial] = useState("");
  const [observaciones, setObservaciones] = useState("");
  const [hayFirma, setHayFirma] = useState(false);

  const [enviando, setEnviando] = useState(false);
  const [envioError, setEnvioError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas/dia")
      .then((r) => leerJson<JornadaDelDia>(r))
      .then(setJornada)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la ruta."));
  }, [fetchConSesion]);

  if (errorCarga) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Retiro" />
        <p className="text-sm text-destructive">{errorCarga}</p>
      </div>
    );
  }

  if (!jornada) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Retiro" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }

  if (jornada.rutaId === null) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Retiro" />
        <p className="text-muted-foreground">No tenés una ruta en curso.</p>
      </div>
    );
  }

  const rutaId = jornada.rutaId;
  const contadosNum = contados === "" ? null : Number(contados);
  const hayDiscrepancia = contadosNum !== null && contadosNum !== jornada.bultosEsperados;
  const faltaObservacion = hayDiscrepancia && !observaciones.trim();
  const listo =
    contadosNum !== null && contadosNum >= 0 && kmInicial !== "" && Number(kmInicial) >= 0 && hayFirma && !faltaObservacion;

  if (jornada.retiroConfirmadoEn) {
    return (
      <div className="p-4 flex flex-col gap-4">
        <CabeceraRepartidor titulo="Retiro" />
        <p className="rounded-2xl border bg-card shadow-sm p-4">
          El retiro ya está firmado ({new Date(jornada.retiroConfirmadoEn).toLocaleTimeString()}).
        </p>
        <Button className="h-12 text-base" render={<Link href="/hoy" />} nativeButton={false}>
          Ir a mi ruta
        </Button>
      </div>
    );
  }

  async function confirmar() {
    setEnviando(true);
    setEnvioError(null);
    try {
      const firma = await firmaRef.current?.exportar();
      if (!firma) throw new Error("El retiro necesita la firma del repartidor.");

      const form = new FormData();
      form.append("deviceUuid", uuidDeRetiro(rutaId));
      form.append("capturadaEn", new Date().toISOString());
      form.append("bultosContados", String(contadosNum));
      form.append("kmInicial", String(Number(kmInicial)));
      if (observaciones.trim()) form.append("observaciones", observaciones.trim());
      form.append("firma", firma, "firma.jpg");

      const resp = await fetchConSesion("/api/mi-jornada/retiro", { method: "POST", body: form });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      await leerJson<RetiroResultado>(resp);
      limpiarUuidDeRetiro(rutaId);
      router.replace("/hoy");
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo confirmar el retiro.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <CabeceraRepartidor titulo="Retiro de la ruta" volverA="/hoy" />

      <div className="rounded-2xl border-2 border-bf-azul bg-bf-celeste/10 p-4 text-center">
        <p className="text-xs font-semibold uppercase tracking-wide text-bf-azul">Bultos de la ruta</p>
        <p className="text-5xl font-bold">{jornada.bultosEsperados}</p>
        <p className="text-sm text-muted-foreground">Contá lo que cargás y firmá antes de salir.</p>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="contados">Bultos contados</Label>
        <Input
          id="contados"
          type="number"
          inputMode="numeric"
          min={0}
          className="h-12 text-base"
          value={contados}
          onChange={(e) => setContados(e.target.value)}
        />
      </div>

      {hayDiscrepancia && (
        <div className="flex flex-col gap-2">
          <Label htmlFor="observaciones">
            No coincide con los {jornada.bultosEsperados} esperados: explicá la diferencia
          </Label>
          <Textarea
            id="observaciones"
            className="text-base"
            value={observaciones}
            onChange={(e) => setObservaciones(e.target.value)}
          />
        </div>
      )}

      <div className="flex flex-col gap-2">
        <Label htmlFor="km">Kilometraje del odómetro al salir</Label>
        <Input
          id="km"
          type="number"
          inputMode="numeric"
          min={0}
          className="h-12 text-base"
          value={kmInicial}
          onChange={(e) => setKmInicial(e.target.value)}
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label className="flex items-center gap-1.5">
          <PenLine className="size-4" />
          Firma
        </Label>
        <FirmaCanvas ref={firmaRef} onCambio={setHayFirma} />
      </div>

      {envioError && <p className="text-sm text-destructive">{envioError}</p>}

      <Button className="h-12 w-full text-base" disabled={!listo || enviando} onClick={confirmar}>
        {enviando ? "Enviando…" : "Confirmar retiro"}
      </Button>
    </div>
  );
}
