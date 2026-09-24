"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { ParametrosLiquidacion } from "./ParametrosLiquidacion";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaTipoVehiculo,
  type HuecoKm,
  type LocalidadDeZona,
  type LocalidadPendiente,
  type ResultadoRecalculo,
  type TarifaGeneral,
  type TarifasResponse,
} from "@/lib/dominio/tipos";

export default function TarifasPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <ListaTarifas />
    </RequireRole>
  );
}

function ListaTarifas() {
  const { fetchConSesion } = useAuth();
  const [tarifas, setTarifas] = useState<TarifaGeneral[] | null>(null);
  const [huecos, setHuecos] = useState<HuecoKm[]>([]);
  const [preciosCamioneta, setPreciosCamioneta] = useState<Record<number, string>>({});
  const [preciosMoto, setPreciosMoto] = useState<Record<number, string>>({});
  const [kmDesdes, setKmDesdes] = useState<Record<number, string>>({});
  const [kmHastas, setKmHastas] = useState<Record<number, string>>({});
  const [guardandoZona, setGuardandoZona] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [pendientes, setPendientes] = useState<LocalidadPendiente[] | null>(null);
  const [zonaElegida, setZonaElegida] = useState<Record<number, string>>({});
  const [asignandoLocalidad, setAsignandoLocalidad] = useState<number | null>(null);
  const [errorPendientes, setErrorPendientes] = useState<string | null>(null);
  const [localidades, setLocalidades] = useState<LocalidadDeZona[] | null>(null);
  const [recalculando, setRecalculando] = useState(false);
  const [resultadoRecalculo, setResultadoRecalculo] = useState<ResultadoRecalculo | null>(null);

  const cargar = () => {
    fetchConSesion("/api/tarifas")
      .then((r) => leerJson<TarifasResponse>(r))
      .then((datos) => {
        setTarifas(datos.zonas);
        setHuecos(datos.huecos);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar las tarifas."));
  };

  const cargarPendientes = () => {
    fetchConSesion("/api/localidades/pendientes")
      .then((r) => leerJson<LocalidadPendiente[]>(r))
      .then(setPendientes)
      .catch((err) => setErrorPendientes(err instanceof Error ? err.message : "No se pudieron cargar las localidades pendientes."));
    fetchConSesion("/api/localidades/con-zona")
      .then((r) => leerJson<LocalidadDeZona[]>(r))
      .then(setLocalidades)
      .catch(() => setLocalidades([]));
  };

  useEffect(cargar, [fetchConSesion]);
  useEffect(cargarPendientes, [fetchConSesion]);

  async function asignarZona(localidadId: number) {
    const zonaId = zonaElegida[localidadId];
    if (!zonaId) return;
    setAsignandoLocalidad(localidadId);
    setErrorPendientes(null);
    try {
      const resp = await fetchConSesion(`/api/localidades/${localidadId}/zona`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ zonaId: Number(zonaId) }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      cargarPendientes();
    } catch (err) {
      setErrorPendientes(err instanceof Error ? err.message : "No se pudo asignar la zona.");
    } finally {
      setAsignandoLocalidad(null);
    }
  }

  async function volverAAutomatica(localidadId: number) {
    setAsignandoLocalidad(localidadId);
    setErrorPendientes(null);
    try {
      const resp = await fetchConSesion(`/api/localidades/${localidadId}/zona/automatica`, { method: "PUT" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      cargarPendientes();
    } catch (err) {
      setErrorPendientes(err instanceof Error ? err.message : "No se pudo volver a la zona automática.");
    } finally {
      setAsignandoLocalidad(null);
    }
  }

  async function recalcular() {
    setRecalculando(true);
    setErrorPendientes(null);
    setResultadoRecalculo(null);
    try {
      const resp = await fetchConSesion("/api/localidades/recalcular", { method: "POST" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setResultadoRecalculo(await leerJson<ResultadoRecalculo>(resp));
      cargarPendientes();
    } catch (err) {
      setErrorPendientes(err instanceof Error ? err.message : "No se pudieron recalcular las zonas.");
    } finally {
      setRecalculando(false);
    }
  }

  function valorPrecioCamioneta(t: TarifaGeneral) {
    return preciosCamioneta[t.zonaId] ?? (t.precioCamioneta !== null ? String(t.precioCamioneta) : "");
  }

  function valorPrecioMoto(t: TarifaGeneral) {
    return preciosMoto[t.zonaId] ?? (t.precioMoto !== null ? String(t.precioMoto) : "");
  }

  function valorKmDesde(t: TarifaGeneral) {
    return kmDesdes[t.zonaId] ?? (t.kmDesde !== null ? String(t.kmDesde) : "");
  }

  function valorKmHasta(t: TarifaGeneral) {
    return kmHastas[t.zonaId] ?? (t.kmHasta !== null ? String(t.kmHasta) : "");
  }

  async function guardar(t: TarifaGeneral) {
    setError(null);
    setGuardandoZona(t.zonaId);
    try {
      const precioCamionetaTexto = valorPrecioCamioneta(t);
      const precioMotoTexto = valorPrecioMoto(t);
      const kmDesdeTexto = valorKmDesde(t);
      const kmHastaTexto = valorKmHasta(t);
      const [respCamioneta, respMoto, respKm] = await Promise.all([
        fetchConSesion(`/api/tarifas/${t.zonaId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            tipoVehiculo: "camioneta",
            precio: precioCamionetaTexto === "" ? null : Number(precioCamionetaTexto),
          }),
        }),
        fetchConSesion(`/api/tarifas/${t.zonaId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            tipoVehiculo: "moto",
            precio: precioMotoTexto === "" ? null : Number(precioMotoTexto),
          }),
        }),
        fetchConSesion(`/api/zonas/${t.zonaId}/km`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            kmDesde: kmDesdeTexto === "" ? null : Number(kmDesdeTexto),
            kmHasta: kmHastaTexto === "" ? null : Number(kmHastaTexto),
          }),
        }),
      ]);
      if (!respCamioneta.ok) throw new Error((await leerError(respCamioneta)).mensaje);
      if (!respMoto.ok) throw new Error((await leerError(respMoto)).mensaje);
      if (!respKm.ok) throw new Error((await leerError(respKm)).mensaje);
      cargar();
      cargarPendientes(); // cambiar un rango reasigna las zonas automáticas en el servidor
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar la tarifa.");
    } finally {
      setGuardandoZona(null);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-4xl flex flex-col gap-6">
      <CabeceraSesion titulo="Tarifas — lista general" />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Zona automática de las localidades</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <p className="text-sm text-muted-foreground">
            Cada localidad recibe sola la zona cuyo rango de km la cubre, midiendo desde el depósito
            (0 km). Una zona que fijes a mano no se pisa. Si cambiaste de depósito o quedaron
            localidades sin medir, recalculá.
          </p>
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="outline" size="sm" onClick={recalcular} disabled={recalculando}>
              {recalculando ? "Recalculando… (puede tardar)" : "Recalcular distancias y zonas"}
            </Button>
            {resultadoRecalculo && (
              <span className="text-sm text-muted-foreground">
                {resultadoRecalculo.asignadas} con zona · {resultadoRecalculo.sinZona} sin zona
                {resultadoRecalculo.sinCoordenadas > 0 && ` (${resultadoRecalculo.sinCoordenadas} sin medir)`}
              </span>
            )}
          </div>
          {errorPendientes && <p className="text-sm text-destructive">{errorPendientes}</p>}

          {pendientes !== null && pendientes.length > 0 && (
            <div className="flex flex-col gap-2">
              <p className="text-sm font-medium">Sin zona ({pendientes.length})</p>
              {pendientes.map((p) => (
                <div key={p.id} className="flex flex-wrap items-center gap-2 rounded-lg border p-3">
                  <div className="flex-1 min-w-48">
                    <p className="font-medium">
                      {p.nombre}
                      {p.partido && p.partido !== p.nombre ? ` — ${p.partido}` : ""}
                    </p>
                    <p className="text-xs text-amber-600">
                      {p.motivo === "sin_coordenadas"
                        ? "No se pudo medir la distancia al depósito"
                        : `A ${p.distanciaKmDeposito} km del depósito, fuera de todo rango de zona`}
                    </p>
                  </div>
                  <ComboboxBusqueda
                    items={(tarifas ?? []).map((t) => ({
                      value: String(t.zonaId),
                      label: `${t.zonaCodigo} — ${t.zonaNombre}`,
                    }))}
                    value={zonaElegida[p.id] ?? null}
                    onValueChange={(v) => setZonaElegida((z) => ({ ...z, [p.id]: v ?? "" }))}
                    placeholder="Elegir zona"
                    className="w-56"
                  />
                  <Button
                    size="sm"
                    disabled={!zonaElegida[p.id] || asignandoLocalidad === p.id}
                    onClick={() => asignarZona(p.id)}
                  >
                    {asignandoLocalidad === p.id ? "Asignando…" : "Asignar"}
                  </Button>
                </div>
              ))}
            </div>
          )}

          {localidades !== null && localidades.length > 0 && (
            <details className="rounded-lg border p-3">
              <summary className="cursor-pointer text-sm font-medium">
                Todas las localidades ({localidades.length})
              </summary>
              <ul className="mt-3 flex flex-col gap-2">
                {localidades.map((l) => (
                  <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                    <span>
                      {l.nombre}
                      <span className="ml-2 text-xs text-muted-foreground">
                        {l.zonaCodigo ? `Zona ${l.zonaCodigo}` : "sin zona"}
                        {l.distanciaKmDeposito !== null && ` · ${l.distanciaKmDeposito} km`}
                        {" · "}
                        {l.zonaManual ? "manual" : "automática"}
                      </span>
                    </span>
                    {l.zonaManual && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={asignandoLocalidad === l.id}
                        onClick={() => volverAAutomatica(l.id)}
                      >
                        Volver a automática
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Precio por zona</CardTitle>
          <p className="text-sm text-muted-foreground">
            0 km = el depósito. La primera zona tiene que empezar en 0 para que las localidades
            cercanas (y el propio depósito) reciban zona.
          </p>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {error && <p className="text-sm text-destructive mb-4">{error}</p>}
          {huecos.length > 0 && (
            <p className="text-sm text-amber-600 mb-4">
              Hay tramos de km sin ninguna zona activa que los cubra (auditoría §7): {" "}
              {huecos.map((h, i) => (
                <span key={i}>
                  {i > 0 && ", "}
                  {h.hastaKm !== null ? `${h.desdeKm}–${h.hastaKm} km` : `${h.desdeKm}+ km`}
                </span>
              ))}
              . Una localidad ahí queda sin zona automática.
            </p>
          )}
          {!tarifas ? (
            error ? null : <p className="text-muted-foreground">Cargando…</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Zona</TableHead>
                  <TableHead>Km desde</TableHead>
                  <TableHead>Km hasta</TableHead>
                  <TableHead>Precio {etiquetaTipoVehiculo("camioneta")}</TableHead>
                  <TableHead>Precio {etiquetaTipoVehiculo("moto")}</TableHead>
                  <TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {tarifas.map((t) => (
                  <TableRow key={t.zonaId}>
                    <TableCell>
                      {t.zonaCodigo} — {t.zonaNombre}
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        min={0}
                        className="w-24"
                        placeholder="—"
                        value={valorKmDesde(t)}
                        onChange={(e) => setKmDesdes((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        min={0}
                        className="w-24"
                        placeholder="sin límite"
                        value={valorKmHasta(t)}
                        onChange={(e) => setKmHastas((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        step="0.01"
                        className="w-32"
                        placeholder="sin tarifa"
                        value={valorPrecioCamioneta(t)}
                        onChange={(e) => setPreciosCamioneta((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        step="0.01"
                        className="w-32"
                        placeholder="sin tarifa"
                        value={valorPrecioMoto(t)}
                        onChange={(e) => setPreciosMoto((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Button size="sm" disabled={guardandoZona === t.zonaId} onClick={() => guardar(t)}>
                        Guardar
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground mt-4">
        Un pedido ya confirmado no se ve afectado por este cambio: el precio se congela al
        confirmar (P1) y solo aplica a pedidos nuevos.
      </p>

      <ParametrosLiquidacion />
    </div>
  );
}
