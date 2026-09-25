"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowDown, ArrowUp, ChevronRight, Plus } from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { EstadoPedidoBadge } from "@/components/EstadoBadge";
import { LineaTiempoEstados } from "@/components/LineaTiempoEstados";
import { DetalleEnvioSheet } from "@/components/DetalleEnvioSheet";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerJson } from "@/lib/api/errores";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { esClienteDueno, vePrecios } from "@/lib/auth/types";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { useSondeo } from "@/lib/hooks/useSondeo";
import {
  etiquetaEstadoFactura,
  type CuentaPropia,
  type PedidoDelDia,
  type PedidoResumen,
  type UsuarioEquipo,
  type ListaPaginada,
  type ViajeResumen,
  etiquetaEstadoViaje,
} from "@/lib/dominio/tipos";

export default function MisEnviosPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <MiPlan />
    </RequireRole>
  );
}

/** E1: resumen de cuenta corriente del cliente logueado — solo lectura, sin acciones (el cobro
 * lo gestiona la Empresa; Anexo I §7 excluye pasarelas de pago online). Compacta a propósito: en
 * el teléfono lo importante es saldo y deuda; el detalle de facturas queda plegado. */
function MiCuenta() {
  const { fetchConSesion, usuario } = useAuth();
  const [cuenta, setCuenta] = useState<CuentaPropia | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta")
      .then((r) => leerJson<CuentaPropia>(r))
      .then(setCuenta)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar tu cuenta."));
  }, [fetchConSesion]);

  if (error) return null; // no bloquea el resto de la pantalla si esto falla
  if (!cuenta) return <p className="text-sm text-muted-foreground">Cargando tu cuenta…</p>;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Mi cuenta</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <TarjetaMetrica valor={`$${cuenta.saldo.toLocaleString("es-AR")}`} etiqueta="Saldo" />
          <TarjetaMetrica
            valor={`$${cuenta.deudaVencida.toLocaleString("es-AR")}`}
            etiqueta="Deuda vencida"
            tono={cuenta.deudaVencida > 0 ? "alerta" : "normal"}
          />
        </div>
        <p className="flex flex-wrap items-baseline justify-between gap-2 text-sm text-muted-foreground">
          <span>
            Próximo vencimiento: <span className="text-foreground">{cuenta.proximoVencimiento ?? "—"}</span>
          </span>
          <Link href="/mis-envios/negocio" className="font-medium text-bf-azul underline-offset-2 hover:underline">
            Estado de cuenta y pagos
          </Link>
        </p>
        {/* B3 (acta RF-42): el rango y su descuento, sin los números con que se calculó (RF-33). */}
        {cuenta.rangoNombre && cuenta.rangoNombre !== "Sin rango" && (
          <p className="text-sm text-muted-foreground">
            Tu rango: <span className="font-medium text-foreground">{cuenta.rangoNombre}</span>
            {/* El descuento es sobre la tarifa por envío: sin precios visibles (acta 4.30) no aplica mostrarlo. */}
            {vePrecios(usuario) && cuenta.descuentoPct > 0 && ` · ${cuenta.descuentoPct}% de descuento sobre la tarifa general`}
          </p>
        )}

        {cuenta.servicioCortado && (
          <p className="text-sm text-destructive">
            Tu servicio está interrumpido por deuda vencida. Los envíos ya confirmados o en ruta
            siguen su curso; no se pueden cargar envíos nuevos hasta regularizar.
          </p>
        )}

        {cuenta.facturas.length > 0 && (
          <details className="border-t pt-3">
            <summary className="cursor-pointer text-sm font-medium">
              Facturas pendientes ({cuenta.facturas.length})
            </summary>
            <ul className="mt-2 flex flex-col gap-2">
              {cuenta.facturas.map((f) => (
                <li key={f.id} className="flex flex-col gap-0.5 border-b pb-2 text-sm sm:flex-row sm:justify-between sm:gap-4">
                  <span>
                    {f.periodoDesde} al {f.periodoHasta}
                    <span className="text-muted-foreground"> · vence {f.fechaVencimiento}</span>
                  </span>
                  <span className="shrink-0">
                    <span className={f.estado === "vencida" ? "font-medium text-destructive" : "font-medium"}>
                      ${f.saldo.toLocaleString("es-AR")}
                    </span>
                    <span className="text-muted-foreground"> · {etiquetaEstadoFactura(f.estado)}</span>
                  </span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </CardContent>
    </Card>
  );
}

/** Los envíos de hoy con su línea de tiempo de estados. Se actualiza solo cada 30 s (con la
 * pestaña visible): lo que más le importa al cliente es saber si ya salió o ya llegó. En camino
 * primero; el resto por número. */
function EnviosDeHoy({ onAbrir }: { onAbrir: (id: number) => void }) {
  const { datos, error } = useSondeo<PedidoDelDia[]>("/api/mi-cuenta/plan-del-dia", { intervaloMs: 30_000 });

  const ordenados = datos
    ? [...datos].sort((a, b) => Number(b.estado === "EnRuta") - Number(a.estado === "EnRuta") || a.id - b.id)
    : null;

  return (
    <section aria-labelledby="titulo-hoy" className="flex flex-col gap-3">
      <div className="flex items-baseline justify-between gap-2">
        <h2 id="titulo-hoy" className="text-base font-semibold">
          Hoy
        </h2>
        <span className="text-sm text-muted-foreground">
          {new Date().toLocaleDateString("es-AR", { weekday: "long", day: "numeric", month: "long" })}
        </span>
      </div>

      {!ordenados ? (
        error ? (
          <p className="text-sm text-destructive">No pudimos cargar tus envíos de hoy.</p>
        ) : (
          <div className="h-28 animate-pulse rounded-xl border bg-muted" />
        )
      ) : ordenados.length === 0 ? (
        <div className="flex flex-col items-start gap-3 rounded-xl border border-dashed p-4">
          <p className="text-sm text-muted-foreground">No tenés envíos para hoy.</p>
          <Button size="sm" className="gap-1" render={<Link href="/mis-envios/nuevo" />} nativeButton={false}>
            <Plus className="size-4" />
            Cargar un envío
          </Button>
        </div>
      ) : (
        <ul className="flex flex-col gap-3">
          {ordenados.map((p) => (
            <li key={p.id}>
              <button
                type="button"
                onClick={() => onAbrir(p.id)}
                className="flex w-full flex-col gap-3 rounded-xl border bg-card p-4 text-left shadow-sm transition-colors active:bg-muted/60 md:hover:bg-muted/30"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate font-medium">{p.destinatarioNombre}</p>
                    <p className="truncate text-sm text-muted-foreground">
                      {p.destinoCalleNumero}
                      {p.destinoLocalidad ? `, ${p.destinoLocalidad}` : ""}
                    </p>
                  </div>
                  <EstadoPedidoBadge estado={p.estado} />
                </div>
                <LineaTiempoEstados
                  variante="compacta"
                  estadoActual={p.estado}
                  eventos={p.eventos.map((e) => ({ estado: e.estado, ocurridoEn: e.ocurridoEn, motivo: e.motivo }))}
                />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// Antes pedía GET /api/pedidos sin ningún query param — el rol 'cliente' se bajaba su historial
// completo en cada visita. useListadoPaginado (extraído de /pedidos y /rutas, ver ese hook) le
// da paginación real sin escribir lógica nueva.
function MiPlan() {
  const { usuario, fetchConSesion } = useAuth();
  const esDueno = esClienteDueno(usuario);
  const [envioAbierto, setEnvioAbierto] = useState<number | null>(null);
  // Solo el dueño: filtrar la lista por quién cargó cada envío.
  const [equipo, setEquipo] = useState<UsuarioEquipo[]>([]);
  const [cargadoPor, setCargadoPor] = useState<string | null>(null);
  // "Ver los envíos del período" (Mi negocio) llega con ?desde=&hasta=. MiPlan monta solo en el
  // navegador (RequireRole espera la sesión), así que leer window acá no genera un desfasaje de hidratación.
  const [rango, setRango] = useState(() => {
    const q = new URLSearchParams(window.location.search);
    const desde = q.get("desde");
    const hasta = q.get("hasta");
    return desde && hasta ? { desde, hasta } : null;
  });
  const {
    items: pedidos, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina, totalPaginas,
    orden, alternarOrden, conReinicioDePagina,
  } = useListadoPaginado<PedidoResumen>({
    ruta: "/api/pedidos",
    filtros: { cargadoPor: cargadoPor ?? "", fechaDesde: rango?.desde ?? "", fechaHasta: rango?.hasta ?? "" },
    ordenInicial: "-fecha",
  });

  useEffect(() => {
    if (!esDueno) return;
    fetchConSesion("/api/mi-cuenta/usuarios")
      .then((r) => leerJson<UsuarioEquipo[]>(r))
      .then(setEquipo)
      .catch(() => setEquipo([])); // sin la lista, el filtro no aparece; el resto sigue igual
  }, [esDueno, fetchConSesion]);

  const nuevosPrimero = orden === "-fecha";

  return (
    <div className="flex flex-col gap-6 p-4 md:p-8">
      <CabeceraSesion titulo="Mi plan" />

      <EnviosDeHoy onAbrir={setEnvioAbierto} />
      <MisViajes />
      {esDueno && <MiCuenta />}

      <section aria-labelledby="titulo-todos" className="flex flex-col gap-3">
        <div className="flex items-center justify-between gap-2">
          <h2 id="titulo-todos" className="text-base font-semibold">
            Todos mis envíos
          </h2>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="h-10 gap-1 md:h-8"
              onClick={() => alternarOrden("fecha")}
              aria-label={
                nuevosPrimero
                  ? "Ordenados del más nuevo al más viejo. Tocar para ver los más viejos primero"
                  : "Ordenados del más viejo al más nuevo. Tocar para ver los más nuevos primero"
              }
            >
              {nuevosPrimero ? <ArrowDown className="size-4" /> : <ArrowUp className="size-4" />}
              {nuevosPrimero ? "Más nuevos primero" : "Más viejos primero"}
            </Button>
            <Button size="sm" className="hidden gap-1 md:inline-flex" render={<Link href="/mis-envios/nuevo" />} nativeButton={false}>
              <Plus className="size-4" />
              Cargar envío
            </Button>
          </div>
        </div>
        {rango && (
          <div className="flex items-center gap-2 text-sm">
            <span className="rounded-full bg-accent px-3 py-1 text-bf-azul">
              Entrega del {rango.desde.split("-").reverse().slice(0, 2).join("/")} al{" "}
              {rango.hasta.split("-").reverse().slice(0, 2).join("/")}
            </span>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setRango(null);
                setPagina(1);
                window.history.replaceState(null, "", "/mis-envios");
              }}
            >
              Quitar filtro
            </Button>
          </div>
        )}
        {esDueno && equipo.length > 1 && (
          <div className="md:max-w-xs">
            <ComboboxBusqueda
              items={equipo.map((u) => ({ value: u.id, label: u.nombre }))}
              value={cargadoPor}
              onValueChange={conReinicioDePagina(setCargadoPor)}
              placeholder="Cargados por cualquiera"
            />
          </div>
        )}
        {error ? (
          <p className="text-sm text-destructive">{error}</p>
        ) : !pedidos ? (
          <p className="text-muted-foreground">Cargando…</p>
        ) : pedidos.length === 0 ? (
          <p className="text-muted-foreground">Todavía no tenés envíos.</p>
        ) : (
          <>
            <ul className="flex flex-col gap-2">
              {pedidos.map((p) => (
                <li key={p.id}>
                  <button
                    type="button"
                    onClick={() => setEnvioAbierto(p.id)}
                    className="flex min-h-14 w-full items-center gap-3 rounded-xl border bg-card p-3 text-left transition-colors active:bg-muted/60 md:hover:bg-muted/30"
                  >
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-medium">{p.destinatarioNombre}</p>
                      <p className="text-sm text-muted-foreground">
                        Entrega: {p.fechaEntrega}
                        {p.viajeId && ` · Viaje #${p.viajeId}, parada ${p.ordenEnViaje}`}
                        {esDueno && p.cargadoPorNombre && ` · Cargó ${p.cargadoPorNombre}`}
                      </p>
                    </div>
                    <EstadoPedidoBadge estado={p.estado} />
                    <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                  </button>
                </li>
              ))}
            </ul>
            <ControlesPaginacion
              pagina={pagina}
              setPagina={setPagina}
              totalPaginas={totalPaginas}
              totalRegistros={totalRegistros}
              tamanioPagina={tamanioPagina}
              setTamanioPagina={setTamanioPagina}
            />
          </>
        )}
      </section>

      <DetalleEnvioSheet pedidoId={envioAbierto} onCerrar={() => setEnvioAbierto(null)} />
    </div>
  );
}

/** Los últimos viajes (envíos de varias paradas) con su avance; cada uno abre su detalle con el mapa.
 * No se muestra si el cliente todavía no cargó ninguno. */
function MisViajes() {
  const { fetchConSesion } = useAuth();
  const [viajes, setViajes] = useState<ViajeResumen[] | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta/viajes?tamanioPagina=5")
      .then((r) => leerJson<ListaPaginada<ViajeResumen>>(r))
      .then((r) => setViajes(r.items))
      .catch(() => setViajes([]));
  }, [fetchConSesion]);

  if (!viajes || viajes.length === 0) return null;

  return (
    <section aria-labelledby="titulo-viajes" className="flex flex-col gap-3">
      <h2 id="titulo-viajes" className="text-base font-semibold">
        Mis viajes
      </h2>
      <ul className="flex flex-col gap-2">
        {viajes.map((v) => (
          <li key={v.id}>
            <Link
              href={`/mis-envios/viajes/${v.id}`}
              className="flex min-h-14 items-center gap-3 rounded-xl border bg-card p-3 text-sm transition-colors active:bg-muted/60 md:hover:bg-muted/30"
            >
              <div className="min-w-0 flex-1">
                <p className="font-medium">Viaje #{v.id} · {v.paradas} paradas</p>
                <p className="text-muted-foreground">
                  Entrega {v.fechaEntrega.split("-").reverse().join("/")} · {etiquetaEstadoViaje(v.estado)}
                </p>
              </div>
              <span className="shrink-0 tabular-nums text-muted-foreground" aria-label={`${v.entregadas} de ${v.paradas} entregadas`}>
                {v.entregadas}/{v.paradas}
              </span>
              <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
