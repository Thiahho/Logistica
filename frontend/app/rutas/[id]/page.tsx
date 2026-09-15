"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { EstadoRutaBadge, EstadoParadaBadge } from "@/components/EstadoBadge";
import { ProgresoParadas } from "@/components/ProgresoParadas";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa, VarianteMarcador } from "@/components/mapa/Mapa";
import { leerError, leerJson } from "@/lib/api/errores";
import { useSondeo } from "@/lib/hooks/useSondeo";
import type { JornadaRuta, ParadaDelDia, ResultadoRuta, RutaDetalle, UsuarioSeleccion } from "@/lib/dominio/tipos";

export default function RutaDetallePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <RutaDetalleContenido />
    </RequireRole>
  );
}

const VARIANTE_POR_ESTADO: Record<string, VarianteMarcador> = {
  pendiente: "pendiente",
  completada: "completada",
  fallida: "fallida",
};

/** El agujero principal que tapa esta pantalla: hasta ahora `/rutas/{id}` no existía —
 * `armar/page.tsx` se auto-bloqueaba apenas la ruta dejaba `planificada`, así que una ruta
 * `en_curso` o `cerrada` no tenía ninguna vista de sus paradas desde el back-office. Funciona en
 * los tres estados; solo `en_curso` pollea (jornada §9.3, useSondeo a 20s) — en `planificada` y
 * `cerrada` nada se mueve solo. */
function RutaDetalleContenido() {
  const { id } = useParams<{ id: string }>();
  const { usuario, fetchConSesion } = useAuth();

  const [ruta, setRuta] = useState<RutaDetalle | null>(null);
  const [resultado, setResultado] = useState<ResultadoRuta | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  // ParadaDelDia (v_paradas_repartidor) no trae `anclada` — se completa desde GET .../paradas,
  // que sí lo tiene (RutaParada.Anclada). Una sola carga, no polleada: RF-13 no cambia sola.
  const [ancladas, setAncladas] = useState<Set<number>>(new Set());

  const cargarRuta = useCallback(() => {
    fetchConSesion(`/api/rutas/${id}`)
      .then((r) => leerJson<RutaDetalle>(r))
      .then(setRuta)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la ruta."));
  }, [fetchConSesion, id]);

  useEffect(cargarRuta, [cargarRuta]);

  useEffect(() => {
    fetchConSesion(`/api/rutas/${id}/paradas`)
      .then((r) => leerJson<{ id: number; anclada: boolean }[]>(r))
      .then((paradas) => setAncladas(new Set(paradas.filter((p) => p.anclada).map((p) => p.id))))
      .catch(() => {}); // decorativo: si falla, simplemente no se muestra la marca de anclada
  }, [fetchConSesion, id]);

  useEffect(() => {
    if (ruta?.estado === "cerrada" && usuario?.rol === "administracion") {
      fetchConSesion(`/api/rutas/${id}/resultado`)
        .then((r) => leerJson<ResultadoRuta>(r))
        .then(setResultado)
        .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el resultado."));
    }
  }, [ruta?.estado, usuario?.rol, fetchConSesion, id]);

  const {
    datos: bundle,
    error: errorJornada,
    recargar: recargarJornada,
  } = useSondeo<JornadaRuta>(`/api/rutas/${id}/jornada`, {
    intervaloMs: 20_000,
    repetir: ruta?.estado === "en_curso",
  });

  const [dialogoRepartidor, setDialogoRepartidor] = useState(false);
  const [dialogoOrden, setDialogoOrden] = useState(false);

  if (!ruta) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Ruta" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  const paradas = bundle?.paradas ?? [];
  const puedeActuar = ruta.estado !== "cerrada";

  const marcadores: MarcadorMapa[] = [
    ...(bundle?.origen && bundle.origen.lat !== null && bundle.origen.lng !== null
      ? [
          {
            id: "origen",
            punto: { lat: bundle.origen.lat, lng: bundle.origen.lng },
            etiqueta: bundle.origen.esDeposito ? "D" : "P",
            variante: "origen" as const,
            titulo: bundle.origen.esDeposito
              ? (bundle.origen.nombreDeposito ?? "Depósito")
              : `Partida: ${bundle.origen.calleNumero}`,
          },
        ]
      : []),
    ...paradas
      .filter((p) => p.lat !== null && p.lng !== null)
      .map((p) => ({
        id: p.paradaId,
        punto: { lat: p.lat as number, lng: p.lng as number },
        etiqueta: String(p.orden),
        variante: VARIANTE_POR_ESTADO[p.estado] ?? "pendiente",
        titulo: `${p.calleNumero}${p.localidad ? `, ${p.localidad}` : ""}`,
      })),
  ];

  return (
    <div className="p-8 max-w-4xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Ruta #${ruta.id} — ${ruta.fecha}`} />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button variant="outline" render={<Link href="/rutas" />} nativeButton={false}>
          ← Rutas
        </Button>
        <div className="flex gap-2">
          {ruta.estado === "planificada" && (
            <Button render={<Link href={`/rutas/${ruta.id}/armar`} />} nativeButton={false}>
              Armar
            </Button>
          )}
          {ruta.estado !== "planificada" && usuario?.rol === "administracion" && (
            <Button variant="outline" render={<Link href={`/rutas/${ruta.id}/cierre`} />} nativeButton={false}>
              {ruta.estado === "cerrada" ? "Ver cierre" : "Cerrar ruta"}
            </Button>
          )}
        </div>
      </div>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="text-base">Datos de la ruta</CardTitle>
            <EstadoRutaBadge estado={ruta.estado} />
          </div>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-3">
          <Fila etiqueta="Repartidor" valor={ruta.repartidorNombre ?? "Sin asignar"} />
          <Fila etiqueta="Vehículo" valor={ruta.vehiculoPatente ?? "Sin asignar"} />
          <Fila etiqueta="Capacidad" valor={`${ruta.cantidadParadas} / ${ruta.capacidadParadas} paradas`} />
          <Fila
            etiqueta="Origen"
            valor={
              ruta.origen
                ? (ruta.origen.esDeposito ? (ruta.origen.nombreDeposito ?? "Depósito") : ruta.origen.calleNumero)
                : "Sin elegir"
            }
          />
        </CardContent>
        {puedeActuar && (
          <CardContent className="flex gap-2 border-t pt-4">
            <Button size="sm" variant="outline" onClick={() => setDialogoRepartidor(true)}>
              Reasignar repartidor
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={paradas.filter((p) => p.estado === "pendiente").length < 2}
              onClick={() => setDialogoOrden(true)}
            >
              Reordenar pendientes
            </Button>
          </CardContent>
        )}
      </Card>

      {resultado && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Resultado económico</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-3 gap-4">
            <TarjetaMetrica valor={`$${resultado.ingresos.toLocaleString("es-AR")}`} etiqueta="Ingresos" />
            <TarjetaMetrica valor={`$${resultado.costos.toLocaleString("es-AR")}`} etiqueta="Costos" />
            <TarjetaMetrica
              valor={`$${resultado.margen.toLocaleString("es-AR")}`}
              etiqueta="Margen"
              tono={resultado.margen < 0 ? "alerta" : "normal"}
            />
          </CardContent>
        </Card>
      )}

      {errorJornada && !bundle && <p className="text-sm text-destructive">{errorJornada}</p>}

      {bundle && (
        <>
          {paradas.length > 0 && (
            <div className="flex flex-col gap-2">
              <ProgresoParadas total={bundle.total} completadas={bundle.completadas} fallidas={bundle.fallidas} />
              <p className="text-sm text-muted-foreground">
                {bundle.completadas + bundle.fallidas} de {bundle.total} paradas resueltas
              </p>
            </div>
          )}

          <MapaDinamico marcadores={marcadores} recorrido={bundle.recorrido?.linea ?? null} alto="h-72" />

          <div className="flex flex-col gap-2">
            <p className="text-sm font-medium text-muted-foreground">Paradas</p>
            {paradas.length === 0 ? (
              <p className="text-muted-foreground">Esta ruta todavía no tiene paradas armadas.</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {paradas.map((p) => (
                  <ParadaFila key={p.paradaId} parada={p} anclada={ancladas.has(p.paradaId)} />
                ))}
              </ul>
            )}
          </div>
        </>
      )}

      {dialogoRepartidor && (
        <DialogoReasignarRepartidor
          rutaId={ruta.id}
          repartidorActualId={ruta.repartidorId}
          onCerrar={() => setDialogoRepartidor(false)}
          onListo={() => {
            setDialogoRepartidor(false);
            cargarRuta();
            recargarJornada();
          }}
        />
      )}

      {dialogoOrden && bundle && (
        <DialogoReordenar
          rutaId={ruta.id}
          paradas={paradas}
          onCerrar={() => setDialogoOrden(false)}
          onListo={() => {
            setDialogoOrden(false);
            recargarJornada();
          }}
        />
      )}
    </div>
  );
}

function Fila({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-muted-foreground">{etiqueta}</span>
      <span>{valor}</span>
    </div>
  );
}

function ParadaFila({ parada: p, anclada }: { parada: ParadaDelDia; anclada: boolean }) {
  const resuelta = p.estado !== "pendiente";
  return (
    <li className={`flex items-start gap-3 rounded-lg border p-3 ${resuelta ? "opacity-70" : ""}`}>
      <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-neutral-200 text-xs font-bold text-neutral-700">
        {p.orden}
      </span>
      <div className="min-w-0 flex-1">
        <p className="font-medium">
          {p.pedidos.map((ped) => ped.destinatarioNombre).join(" · ")}
          {anclada && <span className="ml-2 text-xs font-normal text-muted-foreground">(anclada)</span>}
        </p>
        <p className="text-sm text-muted-foreground">
          {p.calleNumero}
          {p.localidad ? `, ${p.localidad}` : ""}
        </p>
        <p className="text-xs text-muted-foreground">
          {p.pedidos.length} pedido(s) · {p.pedidos.reduce((n, ped) => n + ped.bultos, 0)} bultos
          {p.llegadaEn && ` · llegada ${new Date(p.llegadaEn).toLocaleTimeString("es-AR")}`}
          {p.salidaEn && ` · salida ${new Date(p.salidaEn).toLocaleTimeString("es-AR")}`}
        </p>
      </div>
      <EstadoParadaBadge estado={p.estado} />
    </li>
  );
}

function DialogoReasignarRepartidor({
  rutaId,
  repartidorActualId,
  onCerrar,
  onListo,
}: {
  rutaId: number;
  repartidorActualId: string | null;
  onCerrar: () => void;
  onListo: () => void;
}) {
  const { fetchConSesion } = useAuth();
  const [repartidores, setRepartidores] = useState<UsuarioSeleccion[]>([]);
  const [elegido, setElegido] = useState<string | null>(repartidorActualId);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/usuarios/seleccion?rol=repartidor")
      .then((r) => leerJson<UsuarioSeleccion[]>(r))
      .then(setRepartidores)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los repartidores."));
  }, [fetchConSesion]);

  const items = useMemo(() => repartidores.map((r) => ({ value: r.id, label: r.nombre })), [repartidores]);

  async function confirmar() {
    if (!elegido) return;
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion(`/api/rutas/${rutaId}/repartidor`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ repartidorId: elegido }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onListo();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo reasignar el repartidor.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reasignar repartidor</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-3 pt-2">
          <ComboboxBusqueda items={items} value={elegido} onValueChange={setElegido} placeholder="Elegir repartidor" />
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button onClick={confirmar} disabled={!elegido || enviando}>
              {enviando ? "Guardando…" : "Guardar"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function DialogoReordenar({
  rutaId,
  paradas,
  onCerrar,
  onListo,
}: {
  rutaId: number;
  paradas: ParadaDelDia[];
  onCerrar: () => void;
  onListo: () => void;
}) {
  const { fetchConSesion } = useAuth();
  // Solo las pendientes se reordenan (RF-12) — completadas/fallidas conservan su posición
  // (RutasController.ReordenarParadas). El orden inicial es el que ya traían.
  const [orden, setOrden] = useState<ParadaDelDia[]>(() => paradas.filter((p) => p.estado === "pendiente"));
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function mover(index: number, delta: number) {
    const destino = index + delta;
    if (destino < 0 || destino >= orden.length) return;
    const nuevo = [...orden];
    [nuevo[index], nuevo[destino]] = [nuevo[destino], nuevo[index]];
    setOrden(nuevo);
  }

  async function confirmar() {
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion(`/api/rutas/${rutaId}/paradas/orden`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ paradaIds: orden.map((p) => p.paradaId) }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onListo();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el nuevo orden.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reordenar paradas pendientes</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-3 pt-2">
          <ul className="flex flex-col gap-2">
            {orden.map((p, i) => (
              <li key={p.paradaId} className="flex items-center gap-2 rounded-lg border p-2">
                <span className="flex-1 truncate text-sm">
                  {p.calleNumero}
                  {p.localidad ? `, ${p.localidad}` : ""}
                </span>
                <Button size="sm" variant="outline" disabled={i === 0} onClick={() => mover(i, -1)}>
                  ↑
                </Button>
                <Button size="sm" variant="outline" disabled={i === orden.length - 1} onClick={() => mover(i, 1)}>
                  ↓
                </Button>
              </li>
            ))}
          </ul>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button onClick={confirmar} disabled={enviando}>
              {enviando ? "Guardando…" : "Guardar orden"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
