"use client";

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { AvisoViajeDeRuta } from "@/components/viajes/AvisoViajeDeRuta";
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
import { leerError, leerJson } from "@/lib/api/errores";
import { trazarRecorrido } from "@/lib/api/recorrido";
import { sugerirOrden, type ParadaParaOrden } from "@/lib/dominio/ruteo";
import type { Punto } from "@/lib/dominio/geo";
import {
  etiquetaEstadoRuta,
  type CandidatoRuta,
  type Deposito,
  type ParadaArmada,
  type Recorrido,
  type RutaDetalle,
  type UsuarioSeleccion,
  type VehiculoSeleccion,
} from "@/lib/dominio/tipos";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa } from "@/components/mapa/Mapa";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";

/** Sentinel del combobox de origen para "tipear otra dirección" en vez de elegir del catálogo. */
const OTRA_DIRECCION = "otra";

interface ParadaConsolidada {
  calleNumero: string;
  localidad: string | null;
  lat: number | null;
  lng: number | null;
  pedidoIds: number[];
  bultos: number;
}

const plural = (n: number, uno: string, varios: string) => `${n} ${n === 1 ? uno : varios}`;

export default function ArmarRutaPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <Suspense fallback={null}>
        <ArmarRuta />
      </Suspense>
    </RequireRole>
  );
}

function ArmarRuta() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();
  const router = useRouter();
  // ?todos=1 (desde "Preparar ruta" en /rutas): al abrir una ruta todavía sin paradas se preselecciona
  // todo lo apto de la fecha, listo para ajustar. Necesita candidatos y paradas guardadas, que llegan
  // por separado: cada callback anota lo suyo y prueba aplicar (una sola vez).
  const quiereTodos = useSearchParams().get("todos") === "1";
  const candidatosCargados = useRef<CandidatoRuta[] | null>(null);
  const paradasGuardadas = useRef<number | null>(null);
  const preseleccionHecha = useRef(false);

  const [ruta, setRuta] = useState<RutaDetalle | null>(null);
  const [candidatos, setCandidatos] = useState<CandidatoRuta[] | null>(null);
  const [repartidores, setRepartidores] = useState<UsuarioSeleccion[]>([]);
  const [vehiculos, setVehiculos] = useState<VehiculoSeleccion[]>([]);
  // Catálogo de depósitos (acta changelog 3.8) + "otra dirección" para cuando la camioneta quedó
  // en un lugar que no está en el catálogo. Sin default implícito: el planificador siempre elige.
  const [depositos, setDepositos] = useState<Deposito[]>([]);
  const [origenSeleccion, setOrigenSeleccion] = useState<string | null>(null); // `deposito:{id}` | "otra" | null
  const [origenElegido, setOrigenElegido] = useState<DireccionResuelta | null>(null);
  const [origenInicial, setOrigenInicial] = useState<DireccionResuelta | null>(null);

  const [seleccionados, setSeleccionados] = useState<Set<number>>(new Set());
  const [ordenUbicaciones, setOrdenUbicaciones] = useState<number[]>([]);
  const [anclajes, setAnclajes] = useState<Set<number>>(new Set());
  const [seleccionadaMapa, setSeleccionadaMapa] = useState<number | null>(null);
  const [recorrido, setRecorrido] = useState<Recorrido | null>(null);

  const [vehiculoId, setVehiculoId] = useState<number | null>(null);
  const [repartidorId, setRepartidorId] = useState<string | null>(null);
  const [capacidadParadas, setCapacidadParadas] = useState(24);

  const [guardando, setGuardando] = useState(false);
  const [cerrando, setCerrando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const cargarRuta = useCallback(() => {
    fetchConSesion(`/api/rutas/${id}`)
      .then((r) => leerJson<RutaDetalle>(r))
      .then((r) => {
        setRuta(r);
        setVehiculoId(r.vehiculoId);
        setRepartidorId(r.repartidorId);
        setCapacidadParadas(r.capacidadParadas);
        // Reabrir un armado ya guardado: el combobox abre en la opción correcta. Si es "otra
        // dirección", se prellena sin volver a geocodificar (SelectorDireccion la toma como
        // `inicial`) y se setea también `origenElegido` directo (no solo `origenInicial`): si el
        // operador no toca el selector, SelectorDireccion nunca llama a onCambio, y sin esto
        // guardarAsignacion mandaría origenUbicacionId null pese a que la ruta ya tenía uno.
        if (r.origenUbicacionId === null || r.origen === null) {
          setOrigenSeleccion(null);
          setOrigenInicial(null);
          setOrigenElegido(null);
        } else if (r.origen.esDeposito) {
          setOrigenSeleccion(`deposito:${r.origen.ubicacionId}`);
          setOrigenInicial(null);
          setOrigenElegido(null);
        } else {
          setOrigenSeleccion(OTRA_DIRECCION);
          const origenReabierto =
            r.origen.localidadId !== null
              ? {
                  ubicacionId: r.origen.ubicacionId,
                  calleNumero: r.origen.calleNumero,
                  localidadId: r.origen.localidadId,
                  localidadNombre: r.origen.localidad,
                  lat: r.origen.lat,
                  lng: r.origen.lng,
                  geoConfianza: r.origen.geoConfianza,
                }
              : null;
          setOrigenInicial(origenReabierto);
          setOrigenElegido(origenReabierto);
        }
      })
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la ruta."));
  }, [fetchConSesion, id]);

  useEffect(cargarRuta, [cargarRuta]);

  const intentarPreseleccion = useCallback(() => {
    const cands = candidatosCargados.current;
    const nParadas = paradasGuardadas.current;
    if (!quiereTodos || preseleccionHecha.current || cands === null || nParadas === null) return;
    preseleccionHecha.current = true;
    if (nParadas === 0) setSeleccionados(new Set(cands.filter((c) => c.direccionApta).map((c) => c.pedidoId)));
  }, [quiereTodos]);

  useEffect(() => {
    if (!ruta) return;
    fetchConSesion(`/api/pedidos/candidatos-ruta?fecha=${ruta.fecha}&rutaId=${id}`)
      .then((r) => leerJson<CandidatoRuta[]>(r))
      .then((c) => {
        setCandidatos(c);
        candidatosCargados.current = c;
        intentarPreseleccion();
      })
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar los candidatos."));
  }, [fetchConSesion, ruta, id, intentarPreseleccion]);

  useEffect(() => {
    fetchConSesion(`/api/rutas/${id}/paradas`)
      .then((r) => leerJson<ParadaArmada[]>(r))
      .then((paradas) => {
        setOrdenUbicaciones(paradas.map((p) => p.ubicacionId));
        setAnclajes(new Set(paradas.filter((p) => p.anclada).map((p) => p.ubicacionId)));
        setSeleccionados(new Set(paradas.flatMap((p) => p.pedidoIds)));
        paradasGuardadas.current = paradas.length;
        intentarPreseleccion();
      })
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar las paradas."));
  }, [fetchConSesion, id, intentarPreseleccion]);

  useEffect(() => {
    fetchConSesion("/api/usuarios/seleccion?rol=repartidor")
      .then((r) => leerJson<UsuarioSeleccion[]>(r))
      .then(setRepartidores)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar los repartidores."));
    fetchConSesion("/api/vehiculos/seleccion")
      .then((r) => leerJson<VehiculoSeleccion[]>(r))
      .then(setVehiculos)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar los vehículos."));
    fetchConSesion("/api/ubicaciones/depositos")
      .then((r) => leerJson<Deposito[]>(r))
      .then(setDepositos)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el catálogo de depósitos."));
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
      if (existente) {
        existente.pedidoIds.push(c.pedidoId);
        existente.bultos += c.bultos;
      } else
        mapa.set(c.destinoUbicacionId, {
          calleNumero: c.destinoCalleNumero,
          localidad: c.destinoLocalidad,
          lat: c.lat,
          lng: c.lng,
          pedidoIds: [c.pedidoId],
          bultos: c.bultos,
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

  // Depósito elegido del catálogo, si la selección actual es una de esas opciones (no "otra").
  const depositoElegido = useMemo(
    () =>
      origenSeleccion && origenSeleccion !== OTRA_DIRECCION
        ? (depositos.find((d) => `deposito:${d.ubicacionId}` === origenSeleccion) ?? null)
        : null,
    [origenSeleccion, depositos],
  );

  // Punto desde el que arranca el recorrido: un depósito del catálogo u otra dirección elegida a
  // mano (acta changelog 3.6/3.8) — reemplaza los usos que antes asumían siempre el depósito.
  const puntoPartida = useMemo<Punto | null>(() => {
    const o = depositoElegido ?? (origenSeleccion === OTRA_DIRECCION ? origenElegido : null);
    return o && o.lat !== null && o.lng !== null ? { lat: o.lat, lng: o.lng } : null;
  }, [depositoElegido, origenSeleccion, origenElegido]);

  // Firma en string (no el array `paradas`, que es nuevo en cada render) para que el efecto de
  // abajo dispare solo cuando el conjunto u orden de coordenadas realmente cambia — el mismo
  // problema que ya resuelve `ordenEfectivo` con useMemo, pero para el POST de recorrido.
  const firmaRecorrido = useMemo(() => {
    if (!puntoPartida) return "";
    const conCoordenada = paradas.filter((p) => p.lat !== null && p.lng !== null);
    if (conCoordenada.length === 0) return "";
    return [puntoPartida, ...conCoordenada].map((p) => `${p.lat},${p.lng}`).join(";");
  }, [puntoPartida, paradas]);

  // Recorrido real por calles (OSRM vía backend). Debounce + AbortController: mismo patrón que
  // la cotización en vivo de pedidos/nuevo/page.tsx — sin esto, diez clics rápidos en ↑/↓
  // disparan diez requests y una respuesta lenta puede pisar a una más nueva. El caso
  // "firmaRecorrido vacía" NO llama setRecorrido acá (dispararía react-hooks/set-state-in-effect
  // al ser síncrono); en cambio se deriva en el render de abajo (recorridoVisible).
  useEffect(() => {
    if (!firmaRecorrido) return;
    const puntos: Punto[] = firmaRecorrido.split(";").map((par) => {
      const [lat, lng] = par.split(",").map(Number);
      return { lat, lng };
    });
    const abort = new AbortController();
    const timeout = setTimeout(() => {
      trazarRecorrido(fetchConSesion, puntos, abort.signal).then(setRecorrido);
    }, 500);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
  }, [firmaRecorrido, fetchConSesion]);

  const recorridoVisible = firmaRecorrido ? recorrido : null;

  const marcadoresMapa = useMemo<MarcadorMapa[]>(() => {
    const items: MarcadorMapa[] = [];
    if (puntoPartida) {
      items.push({
        id: "origen",
        punto: puntoPartida,
        etiqueta: depositoElegido ? "D" : "P",
        imagen: depositoElegido ? "/logo-solo.png" : undefined,
        variante: "origen",
        titulo: depositoElegido
          ? depositoElegido.nombre
          : `${origenElegido?.calleNumero ?? ""}${origenElegido?.localidadNombre ? `, ${origenElegido.localidadNombre}` : ""}`,
      });
    }
    paradas.forEach((p, i) => {
      if (p.lat === null || p.lng === null) return;
      items.push({
        id: p.ubicacionId,
        punto: { lat: p.lat, lng: p.lng },
        etiqueta: String(i + 1),
        variante: "pendiente",
        titulo: `${p.calleNumero}${p.localidad ? `, ${p.localidad}` : ""}`,
        seleccionado: seleccionadaMapa === p.ubicacionId,
      });
    });
    return items;
  }, [puntoPartida, depositoElegido, origenElegido, paradas, seleccionadaMapa]);

  const paradasSinCoordenadas = paradas.filter((p) => p.lat === null || p.lng === null).length;

  const candidatosPorZona = useMemo(() => {
    const mapa = new Map<string, CandidatoRuta[]>();
    for (const c of candidatos ?? []) {
      const clave = c.zonaCodigo ?? "Sin zona";
      if (!mapa.has(clave)) mapa.set(clave, []);
      mapa.get(clave)!.push(c);
    }
    return [...mapa.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [candidatos]);

  // Totales de lo elegido, para saber de un vistazo cuánta carga lleva la ruta.
  const totalBultosSeleccionados = (candidatos ?? [])
    .filter((c) => seleccionados.has(c.pedidoId))
    .reduce((suma, c) => suma + c.bultos, 0);

  const aptos = (candidatos ?? []).filter((c) => c.direccionApta);
  const todosSeleccionados = aptos.length > 0 && aptos.every((c) => seleccionados.has(c.pedidoId));

  /** Marca o desmarca de una vez un grupo de pedidos (una zona o todos). Solo los aptos: una
   * dirección dudosa no se puede rutear, sigue con su aviso y hay que corregirla antes. */
  function fijarSeleccion(grupo: CandidatoRuta[], marcar: boolean) {
    setSeleccionados((actual) => {
      const nuevo = new Set(actual);
      for (const c of grupo) {
        if (!c.direccionApta) continue;
        if (marcar) nuevo.add(c.pedidoId);
        else nuevo.delete(c.pedidoId);
      }
      return nuevo;
    });
  }

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
    if (!puntoPartida) return;
    // Sin coordenada ficticia para las paradas sin geocodificar: sugerirOrden las trata como
    // ancladas (mantienen su posición) en vez de recibir la del origen, que las empujaba
    // artificialmente al principio del recorrido.
    const paraOrden: ParadaParaOrden[] = paradas.map((p) => ({
      ubicacionId: p.ubicacionId,
      lat: p.lat,
      lng: p.lng,
      anclada: p.anclada,
    }));
    setOrdenUbicaciones(sugerirOrden(puntoPartida, paraOrden).map((p) => p.ubicacionId));
  }

  async function guardarAsignacion() {
    const resp = await fetchConSesion(`/api/rutas/${id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        vehiculoId,
        repartidorId,
        capacidadParadas,
        origenUbicacionId:
          depositoElegido?.ubicacionId ?? (origenSeleccion === OTRA_DIRECCION ? (origenElegido?.ubicacionId ?? null) : null),
      }),
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
      <div className="p-4 md:p-8">
        <CabeceraSesion titulo="Armar ruta" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  if (ruta.estado !== "planificada") {
    return (
      <div className="p-4 md:p-8">
        <CabeceraSesion titulo={`Ruta #${ruta.id}`} />
        <p className="text-muted-foreground">
          Esta ruta ya cerró su planificación (estado: {etiquetaEstadoRuta(ruta.estado)}). No se puede seguir editando.
        </p>
        <Button variant="outline" render={<Link href={`/rutas/${ruta.id}`} />} nativeButton={false} className="mt-4">
          Ver detalle de la ruta →
        </Button>
      </div>
    );
  }

  const excedeCapacidad = paradas.length > capacidadParadas;

  return (
    <div className="p-4 md:p-8 max-w-4xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Armar ruta #${ruta.id} — ${ruta.fecha}`} />
      <AvisoViajeDeRuta rutaId={ruta.id} />
      <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false} className="self-start">
        ← Rutas
      </Button>

      {errorCarga && <p className="text-sm text-destructive">{errorCarga}</p>}

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
          <CardTitle className="text-base">Punto de partida</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <ComboboxBusqueda
            items={[
              ...depositos.map((d) => ({
                value: `deposito:${d.ubicacionId}`,
                label: d.nombre,
                detalle: `${d.calleNumero}${d.localidad ? `, ${d.localidad}` : ""}`,
              })),
              { value: OTRA_DIRECCION, label: "Otra dirección…" },
            ]}
            value={origenSeleccion}
            onValueChange={setOrigenSeleccion}
            placeholder="Elegí el punto de partida…"
            mensajeVacio="Sin depósitos cargados."
          />
          {origenSeleccion === OTRA_DIRECCION && (
            <SelectorDireccion inicial={origenInicial} onCambio={setOrigenElegido} idPrefijo="origen" />
          )}
          {origenSeleccion === OTRA_DIRECCION && origenElegido && origenElegido.lat === null && (
            <p className="text-sm text-destructive">
              Esta dirección no se pudo geolocalizar: se guarda igual, pero el recorrido se traza desde la primera parada.
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Pedidos disponibles del {ruta.fecha}, por zona</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {candidatos && candidatos.length > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-muted/50 p-3 text-sm">
              <p className="font-medium">
                Seleccionados: {plural(seleccionados.size, "pedido", "pedidos")} · {plural(totalBultosSeleccionados, "bulto", "bultos")}
                <span className="font-normal text-muted-foreground">
                  {" "}
                  de {plural(candidatos.length, "disponible", "disponibles")} ({plural(candidatos.reduce((n, c) => n + c.bultos, 0), "bulto", "bultos")})
                </span>
              </p>
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="h-10 md:h-8"
                disabled={aptos.length === 0}
                onClick={() => fijarSeleccion(candidatos, !todosSeleccionados)}
              >
                {todosSeleccionados ? "Quitar todos" : "Seleccionar todos"}
              </Button>
            </div>
          )}
          {!candidatos ? (
            <p className="text-muted-foreground">Cargando…</p>
          ) : candidatosPorZona.length === 0 ? (
            <p className="text-muted-foreground">No hay pedidos confirmados sin ruta para esta fecha.</p>
          ) : (
            candidatosPorZona.map(([zona, items]) => {
              const aptosZona = items.filter((c) => c.direccionApta);
              const zonaCompleta = aptosZona.length > 0 && aptosZona.every((c) => seleccionados.has(c.pedidoId));
              return (
              <div key={zona} className="flex flex-col gap-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="text-sm font-medium text-muted-foreground">
                    Zona {zona} · {plural(items.length, "pedido", "pedidos")} · {plural(items.reduce((n, c) => n + c.bultos, 0), "bulto", "bultos")}
                  </p>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    className="h-10 md:h-8"
                    disabled={aptosZona.length === 0}
                    onClick={() => fijarSeleccion(items, !zonaCompleta)}
                  >
                    {zonaCompleta ? "Quitar la zona" : "Seleccionar toda la zona"}
                  </Button>
                </div>
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
                        #{c.pedidoId} · {c.clienteRazonSocial}
                        {/* B3 (acta 4.21): el rango ordena esta lista, nunca las paradas de la ruta. */}
                        {c.rangoNombre && c.rangoNombre !== "Sin rango" && ` (${c.rangoNombre})`} · {c.destinatarioNombre} ·{" "}
                        {c.bultos} bulto(s)
                        {c.urgente && " · urgente"}
                        {" — "}
                        {c.destinoCalleNumero}
                        {c.destinoLocalidad ? `, ${c.destinoLocalidad}` : ""}
                      </Label>
                      {!c.direccionApta && (
                        <span className="text-xs font-medium text-destructive">dirección dudosa, no se puede rutear</span>
                      )}
                      {c.direccionApta && c.requiereCotizacion && (
                        <span className="text-xs font-medium text-amber-600">
                          zona sin tarifa — cerrar planificación va a rechazar la ruta si no se fija un precio manual
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
              );
            })
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Recorrido</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2">
          {paradasSinCoordenadas > 0 && (
            <p className="text-sm text-destructive">
              {paradasSinCoordenadas} parada(s) sin coordenadas — no se dibujan en el mapa.
            </p>
          )}
          <MapaDinamico
            marcadores={marcadoresMapa}
            recorrido={recorridoVisible?.linea ?? null}
            onSeleccionar={(id) => setSeleccionadaMapa(typeof id === "number" ? id : null)}
          />
          {recorridoVisible && (
            <p className="text-xs text-muted-foreground">
              {(recorridoVisible.distanciaMetros / 1000).toFixed(1)} km ·{" "}
              {Math.round(recorridoVisible.duracionSegundos / 60)} min
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base flex items-center justify-between">
            <span>
              Paradas ({paradas.length} / {capacidadParadas})
              <span className="ml-2 text-sm font-normal text-muted-foreground">
                {plural(totalBultosSeleccionados, "bulto", "bultos")}
              </span>
            </span>
            <Button size="sm" variant="outline" disabled={!puntoPartida || paradas.length < 2} onClick={sugerir}>
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
                <li
                  key={p.ubicacionId}
                  className={`flex items-center gap-3 rounded-lg border p-3 text-sm ${
                    seleccionadaMapa === p.ubicacionId ? "ring-2 ring-primary" : ""
                  }`}
                  onMouseEnter={() => setSeleccionadaMapa(p.ubicacionId)}
                >
                  <span className="w-6 shrink-0 text-center font-medium text-muted-foreground">
                    {p.lat === null || p.lng === null ? (
                      <span className="text-destructive" title="Sin coordenadas">
                        {i + 1}
                      </span>
                    ) : (
                      i + 1
                    )}
                  </span>
                  <div className="flex-1">
                    <p>
                      {p.calleNumero}
                      {p.localidad ? `, ${p.localidad}` : ""}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {p.pedidoIds.length} pedido(s) · {plural(p.bultos, "bulto", "bultos")}: {p.pedidoIds.map((pid) => `#${pid}`).join(", ")}
                      {(p.lat === null || p.lng === null) && " · sin coordenadas"}
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
