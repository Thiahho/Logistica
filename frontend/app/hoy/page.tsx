"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import {
  AlertTriangle,
  ChevronRight,
  Maximize2,
  MapPin,
  Navigation,
  Package,
  Play,
  X,
} from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraRepartidor } from "@/components/CabeceraRepartidor";
import { Button } from "@/components/ui/button";
import { useSondeo } from "@/lib/hooks/useSondeo";
import { leerError } from "@/lib/api/errores";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa, VarianteMarcador } from "@/components/mapa/Mapa";
import { EstadoParadaBadge } from "@/components/EstadoBadge";
import { ProgresoParadas } from "@/components/ProgresoParadas";
import { RutaEnGoogleMaps } from "@/components/RutaEnGoogleMaps";
import { AvisoParadaHecha } from "@/components/AvisoParadaHecha";
import {
  ETIQUETA_TIPO_NOVEDAD,
  etiquetaCategoria,
  type JornadaDelDia,
  type NovedadDelDia,
  type ParadaDelDia,
} from "@/lib/dominio/tipos";

export default function HoyPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <GuiaDeRuta />
    </RequireRole>
  );
}

const VARIANTE_POR_ESTADO: Record<string, VarianteMarcador> = {
  pendiente: "pendiente",
  completada: "completada",
  fallida: "fallida",
  cancelada: "cancelada",
};

function urlComoLlegar(p: { lat: number | null; lng: number | null }) {
  return `https://www.google.com/maps/dir/?api=1&destination=${p.lat},${p.lng}&travelmode=driving`;
}

function GuiaDeRuta() {
  // Sondeo y no una carga única: el back-office puede reordenar las pendientes, cancelar un pedido
  // o cambiar un teléfono con el repartidor ya en la calle, y tiene que enterarse sin recargar.
  // useSondeo pausa con la pestaña oculta y conserva lo último bueno si un ciclo falla.
  const { datos: jornada, error, recargar } = useSondeo<JornadaDelDia>("/api/mis-paradas/dia");
  const [mapaExpandido, setMapaExpandido] = useState(false);

  if (!jornada) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Hoy" />
        {error ? (
          <p className="text-sm text-destructive">{error}</p>
        ) : (
          <p className="text-muted-foreground">Cargando…</p>
        )}
      </div>
    );
  }

  if (jornada.rutaId === null) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Hoy" />
        <p className="text-muted-foreground">No tenés una ruta en curso.</p>
      </div>
    );
  }

  // Marcadores: origen de la ruta (depósito o donde quedó la camioneta, acta changelog 3.6) +
  // paradas con coordenada. Degradación consciente en ambos casos — nunca se inventa posición
  // para lo que no la tiene: el origen puede no estar geocodificado (dirección tipeada a mano
  // al armar) igual que una parada puede no estarlo.
  const marcadores: MarcadorMapa[] = [
    ...(jornada.origen && jornada.origen.lat !== null && jornada.origen.lng !== null
      ? [
          {
            id: "origen",
            punto: { lat: jornada.origen.lat, lng: jornada.origen.lng },
            etiqueta: jornada.origen.esDeposito ? "D" : "P",
            variante: "origen" as const,
            titulo: jornada.origen.esDeposito
              ? (jornada.origen.nombreDeposito ?? "Depósito")
              : `Partida: ${jornada.origen.calleNumero}${jornada.origen.localidad ? `, ${jornada.origen.localidad}` : ""}`,
          },
        ]
      : []),
    ...jornada.paradas
      .filter((p) => p.lat !== null && p.lng !== null)
      .map((p) => ({
        id: p.paradaId,
        punto: { lat: p.lat as number, lng: p.lng as number },
        etiqueta: String(p.orden),
        variante: VARIANTE_POR_ESTADO[p.estado] ?? "pendiente",
        titulo: `${p.calleNumero}${p.localidad ? `, ${p.localidad}` : ""}`,
      })),
  ];

  const resueltas = jornada.completadas + jornada.fallidas + jornada.canceladas;
  const proxima = jornada.paradas.find((p) => p.estado === "pendiente");
  const pendientes = jornada.paradas.filter((p) => p.estado === "pendiente").length;
  // RF-35 / acta §7: sin retiro firmado el servidor rechaza llegada y cierre (409), así que la UI
  // ni ofrece abrir una parada. El mapa se sigue viendo: saber a dónde vas antes de cargar sirve.
  const retiroPendiente = jornada.retiroConfirmadoEn === null;

  const avisos = jornada.novedades.filter((n) => n.sinVer);
  const esperandoRespuesta = jornada.novedades.filter((n) => n.origen === "repartidor" && n.estado === "abierta");

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <CabeceraRepartidor titulo="Hoy" />

      <Suspense fallback={null}>
        <AvisoParadaHecha pendientes={pendientes} />
      </Suspense>

      {avisos.length > 0 && (
        <div className="flex flex-col gap-2">
          {avisos.map((n) => (
            <Aviso key={n.id} novedad={n} jornada={jornada} onAcuse={recargar} />
          ))}
        </div>
      )}

      {/* Panel de progreso — "ventana" de estado, siempre visible arriba. */}
      <div className="rounded-2xl border bg-card shadow-sm p-4 flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <span className="text-2xl font-semibold">
            {resueltas} <span className="text-base font-normal text-muted-foreground">de {jornada.total}</span>
          </span>
          <span className="text-sm text-muted-foreground">paradas resueltas</span>
        </div>
        <ProgresoParadas
          total={jornada.total}
          completadas={jornada.completadas}
          fallidas={jornada.fallidas}
          canceladas={jornada.canceladas}
        />
        {jornada.origen && !jornada.origen.esDeposito && (
          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <MapPin className="size-3.5 shrink-0" />
            Salís desde: {jornada.origen.calleNumero}
            {jornada.origen.localidad ? `, ${jornada.origen.localidad}` : ""}
          </p>
        )}
        {!retiroPendiente && jornada.retiroConfirmadoEn && (
          <p className="text-sm font-medium text-green-700">
            Ruta en marcha · salió a las{" "}
            {new Date(jornada.retiroConfirmadoEn).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" })}
          </p>
        )}
        {error && <p className="text-xs text-amber-700">Sin conexión: mostrando lo último que se pudo cargar.</p>}
      </div>

      {retiroPendiente && (
        <div className="rounded-2xl border-2 border-amber-500 bg-amber-50/60 p-4 flex flex-col gap-3">
          <div>
            <span className="text-xs font-semibold uppercase tracking-wide text-amber-700">Todavía no saliste</span>
            <p className="font-medium">Empezá la ruta para poder registrar las entregas.</p>
          </div>
          <ol className="list-decimal space-y-0.5 pl-5 text-sm text-muted-foreground">
            <li>Contá los {jornada.bultosEsperados} bulto(s) que cargás.</li>
            <li>Anotá el km del tablero.</li>
            <li>Firmá y salí.</li>
          </ol>
          <Button className="h-12 w-full gap-2 text-base" render={<Link href="/hoy/retiro" />} nativeButton={false}>
            <Play className="size-4" />
            Empezar ruta
          </Button>
        </div>
      )}

      {/* Toda la ruta en Google Maps (salida + paradas en orden). Se ve desde antes de empezar: saber a
      dónde vas sirve antes de cargar. Con la ruta en marcha, solo lo que falta y desde donde estás. */}
      {jornada.paradas.length > 0 && !jornada.cierreRepartidorEn && (
        <RutaEnGoogleMaps
          origen={jornada.origen}
          paradas={jornada.paradas}
          estado="en_curso"
          mostrarCopiar={false}
          grande
        />
      )}

      {/* Próxima parada — la "ventana" funcional que importa primero: qué sigue, y cómo llegar. */}
      {!retiroPendiente && proxima && (
        <div className="rounded-2xl border-2 border-bf-azul p-4 flex flex-col gap-3 bg-bf-celeste/10">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-bf-azul">Próxima parada</span>
            <span className="flex size-7 items-center justify-center rounded-full bg-bf-azul text-sm font-bold text-white">
              {proxima.orden}
            </span>
          </div>
          <div>
            <p className="font-medium">
              {proxima.pedidos
                .filter((ped) => ped.estado !== "Cancelado")
                .map((ped) => ped.destinatarioNombre)
                .join(" · ")}
            </p>
            <p className="text-sm text-muted-foreground">
              {proxima.calleNumero}
              {proxima.localidad ? `, ${proxima.localidad}` : ""}
            </p>
          </div>
          <div className="flex gap-2">
            {proxima.lat !== null && proxima.lng !== null && (
              <Button
                className="h-12 flex-1 text-base"
                render={<a href={urlComoLlegar(proxima)} target="_blank" rel="noopener noreferrer" />}
                nativeButton={false}
              >
                <Navigation className="size-4" />
                Cómo llegar
              </Button>
            )}
            <Button
              variant="outline"
              className="h-12 flex-1 text-base"
              render={<Link href={`/hoy/parada/${proxima.paradaId}`} />}
              nativeButton={false}
            >
              Ver detalle
            </Button>
          </div>
        </div>
      )}

      {/* Terminar la ruta: siempre a la vista para saber cuánto falta; se habilita cuando no quedan pendientes. */}
      {!retiroPendiente && jornada.total > 0 && (
        <div
          className={`rounded-2xl border-2 p-4 flex flex-col gap-3 ${
            proxima || jornada.cierreRepartidorEn ? "border-border bg-card" : "border-green-600 bg-green-50/60"
          }`}
        >
          {jornada.cierreRepartidorEn ? (
            <>
              <span className="text-xs font-semibold uppercase tracking-wide text-green-700">Ruta terminada</span>
              <p className="font-medium">
                Cargaste el cierre a las {new Date(jornada.cierreRepartidorEn).toLocaleTimeString()}. Queda pendiente
                de revisión de administración.
              </p>
            </>
          ) : proxima ? (
            <>
              <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Terminar la ruta</span>
              <p className="text-sm text-muted-foreground">
                Te {pendientes === 1 ? "falta 1 parada" : `faltan ${pendientes} paradas`} para poder terminar.
              </p>
              <Button className="h-12 w-full text-base" disabled>
                Terminé la ruta
              </Button>
            </>
          ) : (
            <>
              <span className="text-xs font-semibold uppercase tracking-wide text-green-700">Todas resueltas</span>
              <p className="font-medium">No te quedan paradas pendientes. Terminá la ruta para cerrar el día.</p>
              <Button className="h-12 w-full text-base" render={<Link href="/hoy/cierre" />} nativeButton={false}>
                Terminé la ruta
              </Button>
            </>
          )}
        </div>
      )}

      {!jornada.cierreRepartidorEn && (
        <div className="flex flex-col gap-2">
          <Button
            variant="outline"
            className="h-12 w-full text-base"
            render={<Link href="/hoy/problema" />}
            nativeButton={false}
          >
            <AlertTriangle className="size-4" />
            Reportar un problema
          </Button>
          {esperandoRespuesta.map((n) => (
            <p key={n.id} className="rounded-md bg-muted p-2 text-xs text-muted-foreground">
              Informaste: {ETIQUETA_TIPO_NOVEDAD[n.tipo].toLowerCase()}
              {n.categoria ? ` (${etiquetaCategoria(n.categoria).toLowerCase()})` : ""} — esperando respuesta de
              operación.
            </p>
          ))}
        </div>
      )}

      <div className="relative overflow-hidden rounded-2xl border shadow-sm">
        {/* El mapa chico se saca del árbol mientras está expandido (no solo se tapa): los
        controles propios de Leaflet (zoom, atribución) usan z-index >1000 en su propia hoja de
        estilos, por encima de cualquier overlay razonable de la página — dos mapas vivos a la
        vez terminaban con los controles del chico asomando sobre el modal. */}
        {!mapaExpandido && (
          <>
            <MapaDinamico marcadores={marcadores} recorrido={jornada.recorrido?.linea ?? null} alto="h-64" />
            {marcadores.length > 0 && (
              <Button
                variant="outline"
                size="icon"
                className="absolute right-2 top-2 z-[400] size-9 rounded-full bg-card shadow"
                onClick={() => setMapaExpandido(true)}
              >
                <Maximize2 className="size-4" />
              </Button>
            )}
          </>
        )}
      </div>

      {mapaExpandido && (
        <div className="fixed inset-0 z-50 flex flex-col bg-background">
          <div className="flex items-center justify-between border-b p-3">
            <span className="font-medium">Mapa de la jornada</span>
            <Button variant="outline" size="icon" className="size-9" onClick={() => setMapaExpandido(false)}>
              <X className="size-4" />
            </Button>
          </div>
          <div className="flex-1">
            <MapaDinamico marcadores={marcadores} recorrido={jornada.recorrido?.linea ?? null} alto="h-full" />
          </div>
        </div>
      )}

      {jornada.paradas.length === 0 ? (
        <p className="text-muted-foreground">No tenés paradas asignadas hoy.</p>
      ) : (
        <div className="flex flex-col gap-2">
          <p className="text-sm font-medium text-muted-foreground">Todas las paradas</p>
          <ul className="flex flex-col gap-2">
            {jornada.paradas.map((p) => (
              <ParadaCard key={p.paradaId} parada={p} bloqueada={retiroPendiente} />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

/** Aviso de operación (cambio de un dato, cancelación) o respuesta a algo que informó el repartidor.
 * No desaparece solo: "Entendido" es el acuse de recibo (POST .../visto), para que operación pueda
 * saber que el mensaje llegó y no quedó en una pantalla que nadie miró. */
function Aviso({
  novedad: n,
  jornada,
  onAcuse,
}: {
  novedad: NovedadDelDia;
  jornada: JornadaDelDia;
  onAcuse: () => void;
}) {
  const { fetchConSesion } = useAuth();
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const parada = jornada.paradas.find((p) => p.paradaId === n.paradaId);
  const respuesta = n.origen === "repartidor";
  const titulo = respuesta
    ? n.estado === "rechazada"
      ? "Operación rechazó lo que informaste"
      : "Operación respondió lo que informaste"
    : ETIQUETA_TIPO_NOVEDAD[n.tipo];

  async function acusar() {
    setEnviando(true);
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/mi-jornada/novedades/${n.id}/visto`, { method: "POST" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onAcuse();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo confirmar.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div
      className={`rounded-2xl border-2 p-3 flex flex-col gap-2 ${
        n.tipo === "cancelacion"
          ? "border-red-500 bg-red-50/60"
          : n.tipo === "urgencia"
            ? "border-amber-500 bg-amber-50/70"
            : "border-bf-azul bg-bf-celeste/10"
      }`}
    >
      <span className="text-xs font-semibold uppercase tracking-wide">{titulo}</span>
      <p className="text-sm">{n.descripcion}</p>
      {respuesta && n.resolucion && <p className="text-sm font-medium">{n.resolucion}</p>}
      {parada && (
        <p className="text-xs text-muted-foreground">
          Parada {parada.orden} · {parada.calleNumero}
        </p>
      )}
      {error && <p className="text-xs text-destructive">{error}</p>}
      <Button variant="outline" className="h-11 w-full text-base" disabled={enviando} onClick={acusar}>
        {enviando ? "Enviando…" : "Entendido"}
      </Button>
    </div>
  );
}

function ParadaCard({ parada: p, bloqueada }: { parada: ParadaDelDia; bloqueada: boolean }) {
  const resuelta = p.estado !== "pendiente";
  const activos = p.pedidos.filter((ped) => ped.estado !== "Cancelado");
  return (
    <li>
      <Link
        href={`/hoy/parada/${p.paradaId}`}
        aria-disabled={bloqueada}
        tabIndex={bloqueada ? -1 : undefined}
        className={`flex min-h-16 items-center gap-3 rounded-2xl border bg-card shadow-sm p-3 ${resuelta ? "opacity-60" : ""} ${
          bloqueada ? "pointer-events-none opacity-50" : ""
        }`}
      >
        <span
          className={`flex size-8 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
            p.estado === "completada"
              ? "bg-green-600 text-white"
              : p.estado === "fallida"
                ? "bg-red-600 text-white"
                : p.estado === "cancelada"
                  ? "bg-neutral-400 text-white"
                  : "bg-bf-gris text-bf-profundo"
          }`}
        >
          {p.orden}
        </span>
        <div className="min-w-0 flex-1">
          <p className={`truncate font-medium ${p.estado === "cancelada" ? "line-through" : ""}`}>
            {(activos.length > 0 ? activos : p.pedidos).map((ped) => ped.destinatarioNombre).join(" · ")}
          </p>
          <p className="truncate text-sm text-muted-foreground">
            {p.calleNumero}
            {p.localidad ? `, ${p.localidad}` : ""}
            {(p.lat === null || p.lng === null) && " · sin coordenadas"}
          </p>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1">
          <EstadoParadaBadge estado={p.estado} />
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <Package className="size-3" />
            {activos.reduce((n, ped) => n + ped.bultos, 0)}
          </span>
        </div>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  );
}
