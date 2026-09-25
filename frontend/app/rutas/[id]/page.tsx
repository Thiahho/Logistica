"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { Navigation, Phone } from "lucide-react";
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
import { Input } from "@/components/ui/input";
import { TarjetaNovedades } from "@/components/PanelNovedades";
import { RutaEnGoogleMaps } from "@/components/RutaEnGoogleMaps";
import { enlaceParadaGoogleMaps } from "@/lib/dominio/googleMaps";
import type {
  JornadaRuta,
  NovedadResumen,
  ParadaDelDia,
  ResultadoRuta,
  RutaDetalle,
  UsuarioSeleccion,
  VentanaUrgencias,
} from "@/lib/dominio/tipos";
import { DialogoUrgencia } from "./DialogoUrgencia";

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
  cancelada: "cancelada",
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

  // Lo que informó el repartidor desde la calle. Pollea junto con la ruta: una incidencia o un
  // problema de carga es justo lo que no puede esperar a que alguien recargue.
  const { datos: novedades, recargar: recargarNovedades } = useSondeo<NovedadResumen[]>(
    `/api/rutas/${id}/novedades`,
    { intervaloMs: 20_000, repetir: ruta?.estado === "en_curso" },
  );

  const [dialogoRepartidor, setDialogoRepartidor] = useState(false);
  const [dialogoOrden, setDialogoOrden] = useState(false);
  const [dialogoInterrumpir, setDialogoInterrumpir] = useState(false);
  // RF-45: urgencias en una ruta en curso, solo dentro de la ventana de reagrupamiento.
  const [dialogoUrgencia, setDialogoUrgencia] = useState(false);
  const [avisoUrgencia, setAvisoUrgencia] = useState<string | null>(null);
  const { datos: ventana } = useSondeo<VentanaUrgencias>("/api/rutas/urgencias/ventana", {
    intervaloMs: 60_000,
    repetir: ruta?.estado === "en_curso",
  });
  // Con una lista larga en el teléfono el mapa se puede plegar para no obligar a scrollear hasta el final.
  const [mapaVisible, setMapaVisible] = useState(true);

  if (!ruta) {
    return (
      <div className="p-4 md:p-8">
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

  const pendientes = paradas.filter((p) => p.estado === "pendiente").length;
  const totalPedidos = paradas.reduce((n, p) => n + p.pedidos.length, 0);
  const totalBultos = paradas.reduce((n, p) => n + p.pedidos.reduce((m, ped) => m + ped.bultos, 0), 0);

  return (
    <div className="flex max-w-5xl flex-col gap-4 p-4 md:gap-6 md:p-8">
      <CabeceraSesion titulo={`Ruta #${ruta.id} — ${ruta.fecha}`} />
      <div className="flex items-center justify-between gap-3">
        <Button variant="outline" className="h-11 md:h-8" render={<Link href="/rutas" />} nativeButton={false}>
          ← Rutas
        </Button>
        <EstadoRutaBadge estado={ruta.estado} size="md" />
      </div>

      <RutaEnGoogleMaps origen={bundle?.origen ?? ruta.origen} paradas={paradas} estado={ruta.estado} />

      <div className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
        {ruta.estado === "planificada" && (
          <Button className="col-span-2 h-11 sm:col-span-1 md:h-8" render={<Link href={`/rutas/${ruta.id}/armar`} />} nativeButton={false}>
            Armar ruta
          </Button>
        )}
        {ruta.estado !== "planificada" && usuario?.rol === "administracion" && (
          <Button
            variant="outline"
            className="col-span-2 h-11 sm:col-span-1 md:h-8"
            render={<Link href={`/rutas/${ruta.id}/cierre`} />}
            nativeButton={false}
          >
            {ruta.estado === "cerrada" ? "Ver cierre" : "Cerrar ruta"}
          </Button>
        )}
        {puedeActuar && (
          <>
            <Button variant="outline" className="h-11 md:h-8" onClick={() => setDialogoRepartidor(true)}>
              Reasignar repartidor
            </Button>
            <Button
              variant="outline"
              className="h-11 md:h-8"
              disabled={pendientes < 2}
              onClick={() => setDialogoOrden(true)}
            >
              Reordenar pendientes
            </Button>
            {ruta.estado === "en_curso" && (
              <Button
                variant="outline"
                className="col-span-2 h-11 sm:col-span-1 md:h-8"
                disabled={!ventana?.abierta}
                title={ventana && !ventana.abierta ? `Las urgencias entran de ${ventana.desde.slice(0, 5)} a ${ventana.hasta.slice(0, 5)}` : undefined}
                onClick={() => setDialogoUrgencia(true)}
              >
                {ventana && !ventana.abierta
                  ? `Urgencias: ${ventana.desde.slice(0, 5)} a ${ventana.hasta.slice(0, 5)}`
                  : "Agregar urgencia"}
              </Button>
            )}
            {ruta.estado === "en_curso" && (
              <Button variant="destructive" className="col-span-2 h-11 sm:col-span-1 md:h-8" onClick={() => setDialogoInterrumpir(true)}>
                Interrumpir ruta
              </Button>
            )}
          </>
        )}
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos de la ruta</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-3">
          <Fila etiqueta="Repartidor" valor={ruta.repartidorNombre ?? "Sin asignar"} />
          <Fila etiqueta="Vehículo" valor={ruta.vehiculoPatente ?? "Sin asignar"} />
          <div className="col-span-2 sm:col-span-1">
            <Fila
              etiqueta="Punto de salida"
              valor={
                ruta.origen
                  ? (ruta.origen.esDeposito ? (ruta.origen.nombreDeposito ?? "Depósito") : ruta.origen.calleNumero)
                  : "Sin elegir"
              }
            />
          </div>
        </CardContent>
        <CardContent className="grid grid-cols-3 gap-2 border-t pt-4">
          <TarjetaMetrica valor={`${ruta.cantidadParadas} / ${ruta.capacidadParadas}`} etiqueta="Paradas" />
          <TarjetaMetrica valor={totalPedidos} etiqueta="Pedidos" />
          <TarjetaMetrica valor={totalBultos} etiqueta="Bultos" />
        </CardContent>
      </Card>

      {novedades && novedades.length > 0 && (
        <TarjetaNovedades
          novedades={novedades}
          onCambio={() => {
            recargarNovedades();
            recargarJornada();
          }}
        />
      )}

      {resultado && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Resultado económico</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-2 sm:grid-cols-3 sm:gap-4">
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
              <ProgresoParadas
                total={bundle.total}
                completadas={bundle.completadas}
                fallidas={bundle.fallidas}
                canceladas={bundle.canceladas}
              />
              <p className="text-sm text-muted-foreground">
                {bundle.completadas + bundle.fallidas + bundle.canceladas} de {bundle.total} paradas resueltas
              </p>
            </div>
          )}

          {/* En el teléfono va primero la lista (lo que se necesita); lado a lado desde md, con el mapa fijo. */}
          <div className="flex flex-col gap-4 md:grid md:grid-cols-2 md:items-start md:gap-6">
            <div className="flex flex-col gap-2">
              <p className="text-sm font-medium text-muted-foreground">Paradas</p>
              {paradas.length === 0 ? (
                <p className="text-muted-foreground">Esta ruta todavía no tiene paradas armadas.</p>
              ) : (
                <ul className="flex flex-col gap-3">
                  {paradas.map((p) => (
                    <ParadaFila key={p.paradaId} parada={p} anclada={ancladas.has(p.paradaId)} />
                  ))}
                </ul>
              )}
            </div>

            <div className="flex flex-col gap-2 md:sticky md:top-4">
              {paradas.length > 6 && (
                <Button variant="outline" className="h-11 md:hidden" onClick={() => setMapaVisible((v) => !v)}>
                  {mapaVisible ? "Ocultar mapa" : "Ver mapa"}
                </Button>
              )}
              <div className={mapaVisible ? "" : "max-md:hidden"}>
                <MapaDinamico marcadores={marcadores} recorrido={bundle.recorrido?.linea ?? null} alto="h-56 md:h-72" />
              </div>
            </div>
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

      {avisoUrgencia && (
        <p className="rounded-lg border border-amber-500 bg-amber-50/60 p-3 text-sm">{avisoUrgencia}</p>
      )}

      {dialogoUrgencia && ventana && (
        <DialogoUrgencia
          rutaId={ruta.id}
          fecha={ruta.fecha}
          ventana={ventana}
          onCerrar={() => setDialogoUrgencia(false)}
          onListo={(r) => {
            setDialogoUrgencia(false);
            setAvisoUrgencia(
              (r.consolidada ? `Urgencia sumada a la parada ${r.orden}.` : `Urgencia agregada como parada ${r.orden}.`) +
                (r.desplazadas > 0 ? ` Desplazó ${r.desplazadas} parada(s).` : "") +
                (r.total !== null ? ` Precio congelado con recargo: $${r.total.toLocaleString("es-AR")}.` : "") +
                " El repartidor recibe el aviso en su jornada.",
            );
            recargarJornada();
            recargarNovedades();
          }}
        />
      )}

      {dialogoInterrumpir && (
        <DialogoInterrumpir
          rutaId={ruta.id}
          pendientes={paradas.filter((p) => p.estado === "pendiente").length}
          onCerrar={() => setDialogoInterrumpir(false)}
          onListo={() => {
            setDialogoInterrumpir(false);
            cargarRuta();
            recargarJornada();
            recargarNovedades();
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
  const bultos = p.pedidos.reduce((n, ped) => n + ped.bultos, 0);
  // Un solo destinatario = un solo teléfono al que llamar; con varios no hay cuál elegir por el usuario.
  const telefono = p.pedidos.length === 1 ? p.pedidos[0].destinatarioTelefono : null;
  const hora = (iso: string) => new Date(iso).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });

  return (
    <li className={`flex flex-col gap-2 rounded-xl border bg-card p-3 ${resuelta ? "opacity-70" : ""}`}>
      <div className="flex items-start gap-3">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-neutral-200 text-sm font-bold text-neutral-700">
          {p.orden}
        </span>
        <div className="min-w-0 flex-1">
          <p className="break-words font-medium leading-snug">
            {p.pedidos.map((ped) => ped.destinatarioNombre).join(" · ")}
            {anclada && <span className="ml-2 text-xs font-normal text-muted-foreground">(anclada)</span>}
          </p>
          <p className="break-words text-sm text-muted-foreground">
            {p.calleNumero}
            {p.localidad ? `, ${p.localidad}` : ""}
          </p>
        </div>
        <EstadoParadaBadge estado={p.estado} />
      </div>
      <p className="pl-11 text-xs text-muted-foreground">
        {p.pedidos.length} {p.pedidos.length === 1 ? "pedido" : "pedidos"} · {bultos} {bultos === 1 ? "bulto" : "bultos"}
        {p.llegadaEn && ` · llegada ${hora(p.llegadaEn)}`}
        {p.salidaEn && ` · salida ${hora(p.salidaEn)}`}
      </p>
      <div className="flex flex-wrap gap-2 pl-11">
        <Button
          variant="outline"
          size="sm"
          className="h-10 gap-1 md:h-8"
          nativeButton={false}
          render={<a href={enlaceParadaGoogleMaps(p)} target="_blank" rel="noopener noreferrer" />}
        >
          <Navigation className="size-4" />
          Cómo llegar
        </Button>
        {telefono && (
          <Button
            variant="outline"
            size="sm"
            className="h-10 gap-1 md:h-8"
            nativeButton={false}
            render={<a href={`tel:${telefono}`} />}
          >
            <Phone className="size-4" />
            Llamar
          </Button>
        )}
      </div>
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
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="outline" className="h-11 sm:h-8" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button className="h-11 sm:h-8" onClick={confirmar} disabled={!elegido || enviando}>
              {enviando ? "Guardando…" : "Guardar"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** La ruta no sigue y no se reasigna. Las pendientes pasan a fallidas con motivo y sus pedidos entran al
 * circuito de reprogramación de siempre (D13) — sin esto una ruta rota no se puede cerrar limpia. Si lo
 * que hace falta es que OTRO repartidor la continúe, es "Reasignar repartidor", no esto. */
function DialogoInterrumpir({
  rutaId,
  pendientes,
  onCerrar,
  onListo,
}: {
  rutaId: number;
  pendientes: number;
  onCerrar: () => void;
  onListo: () => void;
}) {
  const { fetchConSesion } = useAuth();
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirmar() {
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion(`/api/rutas/${rutaId}/interrumpir`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ motivo }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onListo();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo interrumpir la ruta.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Interrumpir ruta</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-3 pt-2">
          <p className="text-sm text-muted-foreground">
            {pendientes === 0
              ? "No quedan paradas pendientes: no hay nada para declarar fallido."
              : `Las ${pendientes} parada(s) pendientes pasan a fallidas y sus pedidos entran al circuito de reprogramación. Lo ya resuelto no se toca. Si otro repartidor tiene que continuarla, usá "Reasignar repartidor".`}
          </p>
          <Input placeholder="Motivo (obligatorio)" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="outline" className="h-11 sm:h-8" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button variant="destructive" className="h-11 sm:h-8" onClick={confirmar} disabled={enviando || !motivo.trim()}>
              {enviando ? "Interrumpiendo…" : "Interrumpir ruta"}
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
                <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-neutral-200 text-xs font-bold text-neutral-700">
                  {p.orden}
                </span>
                <span className="min-w-0 flex-1 text-sm">
                  {p.calleNumero}
                  {p.localidad ? `, ${p.localidad}` : ""}
                </span>
                <Button
                  size="icon"
                  variant="outline"
                  className="size-11 sm:size-8"
                  aria-label={`Subir la parada ${p.orden}`}
                  disabled={i === 0}
                  onClick={() => mover(i, -1)}
                >
                  ↑
                </Button>
                <Button
                  size="icon"
                  variant="outline"
                  className="size-11 sm:size-8"
                  aria-label={`Bajar la parada ${p.orden}`}
                  disabled={i === orden.length - 1}
                  onClick={() => mover(i, 1)}
                >
                  ↓
                </Button>
              </li>
            ))}
          </ul>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="outline" className="h-11 sm:h-8" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button className="h-11 sm:h-8" onClick={confirmar} disabled={enviando}>
              {enviando ? "Guardando…" : "Guardar orden"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
