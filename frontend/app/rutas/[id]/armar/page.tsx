"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { leerError } from "@/lib/api/errores";
import { sugerirOrden, type ParadaParaOrden } from "@/lib/dominio/ruteo";
import type { Punto } from "@/lib/dominio/geo";
import type {
  CandidatoRuta,
  ParadaArmada,
  RutaDetalle,
  UsuarioSeleccion,
  VehiculoSeleccion,
} from "@/lib/dominio/tipos";

interface ParadaConsolidada {
  calleNumero: string;
  localidad: string | null;
  lat: number | null;
  lng: number | null;
  pedidoIds: number[];
}

export default function ArmarRutaPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ArmarRuta />
    </RequireRole>
  );
}

function ArmarRuta() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [ruta, setRuta] = useState<RutaDetalle | null>(null);
  const [candidatos, setCandidatos] = useState<CandidatoRuta[] | null>(null);
  const [repartidores, setRepartidores] = useState<UsuarioSeleccion[]>([]);
  const [vehiculos, setVehiculos] = useState<VehiculoSeleccion[]>([]);
  const [deposito, setDeposito] = useState<Punto | null>(null);

  const [seleccionados, setSeleccionados] = useState<Set<number>>(new Set());
  const [ordenUbicaciones, setOrdenUbicaciones] = useState<number[]>([]);
  const [anclajes, setAnclajes] = useState<Set<number>>(new Set());

  const [vehiculoId, setVehiculoId] = useState<number | null>(null);
  const [repartidorId, setRepartidorId] = useState<string | null>(null);
  const [capacidadParadas, setCapacidadParadas] = useState(24);

  const [guardando, setGuardando] = useState(false);
  const [cerrando, setCerrando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const cargarRuta = useCallback(() => {
    fetchConSesion(`/api/rutas/${id}`)
      .then((r) => r.json())
      .then((r: RutaDetalle) => {
        setRuta(r);
        setVehiculoId(r.vehiculoId);
        setRepartidorId(r.repartidorId);
        setCapacidadParadas(r.capacidadParadas);
      });
  }, [fetchConSesion, id]);

  useEffect(cargarRuta, [cargarRuta]);

  useEffect(() => {
    if (!ruta) return;
    fetchConSesion(`/api/pedidos/candidatos-ruta?fecha=${ruta.fecha}&rutaId=${id}`)
      .then((r) => r.json())
      .then(setCandidatos);
  }, [fetchConSesion, ruta, id]);

  useEffect(() => {
    fetchConSesion(`/api/rutas/${id}/paradas`)
      .then((r) => r.json())
      .then((paradas: ParadaArmada[]) => {
        setOrdenUbicaciones(paradas.map((p) => p.ubicacionId));
        setAnclajes(new Set(paradas.filter((p) => p.anclada).map((p) => p.ubicacionId)));
        setSeleccionados(new Set(paradas.flatMap((p) => p.pedidoIds)));
      });
  }, [fetchConSesion, id]);

  useEffect(() => {
    fetchConSesion("/api/usuarios/seleccion?rol=repartidor").then((r) => r.json()).then(setRepartidores);
    fetchConSesion("/api/vehiculos/seleccion").then((r) => r.json()).then(setVehiculos);
    fetchConSesion("/api/ubicaciones/deposito").then((r) => r.json()).then(setDeposito);
  }, [fetchConSesion]);

  // Al elegir un vehículo del catálogo, prellena la capacidad de paradas de la ruta con la suya
  // (P7/RF-16); el campo sigue editable a mano después.
  function onElegirVehiculo(valor: string | null) {
    const nuevoId = valor ? Number(valor) : null;
    setVehiculoId(nuevoId);
    const vehiculo = vehiculos.find((v) => v.id === nuevoId);
    if (vehiculo) setCapacidadParadas(vehiculo.capacidadParadas);
  }

  // Consolidación por destino (RF-14): N pedidos al mismo destino son una sola parada.
  const paradasConsolidadas = useMemo(() => {
    const mapa = new Map<number, ParadaConsolidada>();
    for (const c of candidatos ?? []) {
      if (!seleccionados.has(c.pedidoId)) continue;
      const existente = mapa.get(c.destinoUbicacionId);
      if (existente) existente.pedidoIds.push(c.pedidoId);
      else
        mapa.set(c.destinoUbicacionId, {
          calleNumero: c.destinoCalleNumero,
          localidad: c.destinoLocalidad,
          lat: c.lat,
          lng: c.lng,
          pedidoIds: [c.pedidoId],
        });
    }
    return mapa;
  }, [candidatos, seleccionados]);

  // Orden efectivo: reconcilia ordenUbicaciones (estado, lo tocan mover()/sugerir()) contra lo
  // que sigue seleccionado — mantiene la posición de lo vigente, agrega al final lo nuevo, saca
  // lo deseleccionado. Derivado en cada render, sin escribir de vuelta a ordenUbicaciones: evitar
  // sincronizar estado con setState dentro de un efecto (react-hooks/set-state-in-effect).
  const ordenEfectivo = useMemo(() => {
    const vigentes = ordenUbicaciones.filter((uid) => paradasConsolidadas.has(uid));
    const nuevas = [...paradasConsolidadas.keys()].filter((uid) => !vigentes.includes(uid));
    return [...vigentes, ...nuevas];
  }, [ordenUbicaciones, paradasConsolidadas]);

  const paradas = ordenEfectivo
    .map((uid) => {
      const info = paradasConsolidadas.get(uid);
      return info ? { ubicacionId: uid, ...info, anclada: anclajes.has(uid) } : null;
    })
    .filter((p): p is NonNullable<typeof p> => p !== null);

  const candidatosPorZona = useMemo(() => {
    const mapa = new Map<string, CandidatoRuta[]>();
    for (const c of candidatos ?? []) {
      const clave = c.zonaCodigo ?? "Sin zona";
      if (!mapa.has(clave)) mapa.set(clave, []);
      mapa.get(clave)!.push(c);
    }
    return [...mapa.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [candidatos]);

  function alternarSeleccion(pedidoId: number) {
    setSeleccionados((actual) => {
      const nuevo = new Set(actual);
      if (nuevo.has(pedidoId)) nuevo.delete(pedidoId);
      else nuevo.add(pedidoId);
      return nuevo;
    });
  }

  function alternarAnclada(ubicacionId: number) {
    setAnclajes((actual) => {
      const nuevo = new Set(actual);
      if (nuevo.has(ubicacionId)) nuevo.delete(ubicacionId);
      else nuevo.add(ubicacionId);
      return nuevo;
    });
  }

  function mover(index: number, delta: number) {
    const destino = index + delta;
    if (destino < 0 || destino >= ordenEfectivo.length) return;
    const nuevo = [...ordenEfectivo];
    [nuevo[index], nuevo[destino]] = [nuevo[destino], nuevo[index]];
    setOrdenUbicaciones(nuevo);
  }

  function sugerir() {
    if (!deposito) return;
    const paraOrden: ParadaParaOrden[] = paradas.map((p) => ({
      ubicacionId: p.ubicacionId,
      lat: p.lat ?? deposito.lat,
      lng: p.lng ?? deposito.lng,
      anclada: p.anclada,
    }));
    setOrdenUbicaciones(sugerirOrden(deposito, paraOrden).map((p) => p.ubicacionId));
  }

  async function guardarAsignacion() {
    const resp = await fetchConSesion(`/api/rutas/${id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ vehiculoId, repartidorId, capacidadParadas }),
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
  }

  async function guardarParadas() {
    const resp = await fetchConSesion(`/api/rutas/${id}/paradas`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        paradas: paradas.map((p) => ({
          ubicacionId: p.ubicacionId,
          anclada: p.anclada,
          pedidoIds: p.pedidoIds,
        })),
      }),
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
  }

  async function onGuardar() {
    setError(null);
    setAviso(null);
    setGuardando(true);
    try {
      await guardarAsignacion();
      await guardarParadas();
      setAviso("Armado guardado.");
      cargarRuta();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el armado.");
    } finally {
      setGuardando(false);
    }
  }

  async function onCerrarPlanificacion() {
    setError(null);
    setCerrando(true);
    try {
      await guardarAsignacion();
      await guardarParadas();
      const resp = await fetchConSesion(`/api/rutas/${id}/cerrar-planificacion`, { method: "POST" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      router.push("/rutas");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cerrar la planificación.");
    } finally {
      setCerrando(false);
    }
  }

  if (!ruta) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Armar ruta" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }

  if (ruta.estado !== "planificada") {
    return (
      <div className="p-8">
        <CabeceraSesion titulo={`Ruta #${ruta.id}`} />
        <p className="text-muted-foreground">
          Esta ruta ya cerró su planificación (estado: {ruta.estado}). No se puede seguir editando.
        </p>
        <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false} className="mt-4">
          ← Rutas
        </Button>
      </div>
    );
  }

  const excedeCapacidad = paradas.length > capacidadParadas;

  return (
    <div className="p-8 max-w-4xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Armar ruta #${ruta.id} — ${ruta.fecha}`} />
      <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false} className="self-start">
        ← Rutas
      </Button>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Vehículo y repartidor</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-4">
          <div className="flex flex-col gap-2">
            <Label>Vehículo</Label>
            <Select
              items={vehiculos.map((v) => ({
                value: String(v.id),
                label: v.descripcion ? `${v.patente} — ${v.descripcion}` : v.patente,
              }))}
              value={vehiculoId !== null ? String(vehiculoId) : null}
              onValueChange={onElegirVehiculo}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Elegir vehículo" />
              </SelectTrigger>
              <SelectContent>
                {vehiculos.map((v) => (
                  <SelectItem key={v.id} value={String(v.id)}>
                    {v.descripcion ? `${v.patente} — ${v.descripcion}` : v.patente}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label>Repartidor</Label>
            <Select
              items={repartidores.map((r) => ({ value: r.id, label: r.nombre }))}
              value={repartidorId}
              onValueChange={setRepartidorId}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Elegir repartidor" />
              </SelectTrigger>
              <SelectContent>
                {repartidores.map((r) => (
                  <SelectItem key={r.id} value={r.id}>
                    {r.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="capacidad">Capacidad de paradas</Label>
            <Input
              id="capacidad"
              type="number"
              min={1}
              value={capacidadParadas}
              onChange={(e) => setCapacidadParadas(Number(e.target.value))}
              className="w-32"
            />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Pedidos confirmados del {ruta.fecha}, por zona</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {!candidatos ? (
            <p className="text-muted-foreground">Cargando…</p>
          ) : candidatosPorZona.length === 0 ? (
            <p className="text-muted-foreground">No hay pedidos confirmados sin ruta para esta fecha.</p>
          ) : (
            candidatosPorZona.map(([zona, items]) => (
              <div key={zona} className="flex flex-col gap-2">
                <p className="text-sm font-medium text-muted-foreground">Zona {zona}</p>
                <ul className="flex flex-col gap-1">
                  {items.map((c) => (
                    <li key={c.pedidoId} className="flex items-center gap-2 text-sm">
                      <Checkbox
                        id={`pedido-${c.pedidoId}`}
                        checked={seleccionados.has(c.pedidoId)}
                        disabled={!c.direccionApta}
                        onCheckedChange={() => alternarSeleccion(c.pedidoId)}
                      />
                      <Label htmlFor={`pedido-${c.pedidoId}`} className="font-normal">
                        #{c.pedidoId} · {c.clienteRazonSocial} · {c.destinatarioNombre} · {c.bultos} bulto(s)
                        {c.urgente && " · urgente"}
                        {" — "}
                        {c.destinoCalleNumero}
                        {c.destinoLocalidad ? `, ${c.destinoLocalidad}` : ""}
                      </Label>
                      {!c.direccionApta && (
                        <span className="text-xs font-medium text-destructive">dirección dudosa, no se puede rutear</span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base flex items-center justify-between">
            <span>
              Paradas ({paradas.length} / {capacidadParadas})
            </span>
            <Button size="sm" variant="outline" disabled={!deposito || paradas.length < 2} onClick={sugerir}>
              Sugerir orden
            </Button>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2">
          {excedeCapacidad && (
            <p className="text-sm text-destructive">La ruta supera la capacidad de paradas configurada.</p>
          )}
          {paradas.length === 0 ? (
            <p className="text-sm text-muted-foreground">Seleccioná pedidos arriba para armar las paradas.</p>
          ) : (
            <ol className="flex flex-col gap-2">
              {paradas.map((p, i) => (
                <li key={p.ubicacionId} className="flex items-center gap-3 rounded-lg border p-3 text-sm">
                  <span className="w-6 shrink-0 text-center font-medium text-muted-foreground">{i + 1}</span>
                  <div className="flex-1">
                    <p>
                      {p.calleNumero}
                      {p.localidad ? `, ${p.localidad}` : ""}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {p.pedidoIds.length} pedido(s): {p.pedidoIds.map((pid) => `#${pid}`).join(", ")}
                    </p>
                  </div>
                  <Button size="sm" variant="outline" disabled={p.anclada || i === 0} onClick={() => mover(i, -1)}>
                    ↑
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={p.anclada || i === paradas.length - 1}
                    onClick={() => mover(i, 1)}
                  >
                    ↓
                  </Button>
                  <Button
                    size="sm"
                    variant={p.anclada ? "default" : "outline"}
                    onClick={() => alternarAnclada(p.ubicacionId)}
                  >
                    {p.anclada ? "Anclada" : "Anclar"}
                  </Button>
                </li>
              ))}
            </ol>
          )}
        </CardContent>
      </Card>

      {error && <p className="text-sm text-destructive">{error}</p>}
      {aviso && <p className="text-sm text-muted-foreground">{aviso}</p>}

      <div className="flex gap-2">
        <Button variant="outline" disabled={guardando || cerrando} onClick={onGuardar}>
          {guardando ? "Guardando…" : "Guardar armado"}
        </Button>
        <Button disabled={guardando || cerrando || paradas.length === 0} onClick={onCerrarPlanificacion}>
          {cerrando ? "Cerrando…" : "Cerrar planificación"}
        </Button>
      </div>
    </div>
  );
}
