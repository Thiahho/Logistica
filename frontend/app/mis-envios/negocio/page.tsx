"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ChevronLeft, ChevronRight, Paperclip } from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerError, leerJson } from "@/lib/api/errores";
import { comprimirFoto } from "@/lib/captura/foto";
import {
  MEDIOS_PAGO,
  etiquetaEstadoFactura,
  etiquetaEstadoPagoInformado,
  etiquetaMedioPago,
  type DiaNegocio,
  type EstadoDeCuentaPropio,
  type FacturaPropia,
  type FacturaPropiaDetalle,
  type PagosPropios,
  type PeriodoNegocio,
  type ResumenNegocio,
} from "@/lib/dominio/tipos";

type Seccion = "panel" | "cuenta" | "pagos";

const SECCIONES: { valor: Seccion; label: string }[] = [
  { valor: "panel", label: "Panel" },
  { valor: "cuenta", label: "Estado de cuenta" },
  { valor: "pagos", label: "Pagos" },
];

const PERIODOS: { valor: PeriodoNegocio; label: string }[] = [
  { valor: "dia", label: "Hoy" },
  { valor: "semana", label: "Semana" },
  { valor: "mes", label: "Mes" },
];

const pesos = (n: number) => `$${n.toLocaleString("es-AR", { maximumFractionDigits: 2 })}`;

/** YYYY-MM-DD en hora local del navegador. */
const iso = (d: Date) => d.toLocaleDateString("en-CA");
const hoyISO = () => iso(new Date());

/** "2026-09-25" → Date local a mediodía (evita que el huso horario corra el día). */
const aFecha = (s: string) => new Date(`${s}T12:00:00`);
const fechaCorta = (s: string) => aFecha(s).toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit" });

/** Mueve `fecha` un período hacia atrás (-1) o adelante (+1). */
function moverPeriodo(fecha: string, periodo: PeriodoNegocio, sentido: -1 | 1): string {
  const d = aFecha(fecha);
  if (periodo === "dia") d.setDate(d.getDate() + sentido);
  else if (periodo === "semana") d.setDate(d.getDate() + 7 * sentido);
  else d.setMonth(d.getMonth() + sentido, 1);
  return iso(d);
}

function tituloPeriodo(r: ResumenNegocio): string {
  if (r.periodo === "dia") return aFecha(r.actual.desde).toLocaleDateString("es-AR", { weekday: "long", day: "numeric", month: "long" });
  if (r.periodo === "mes") return aFecha(r.actual.desde).toLocaleDateString("es-AR", { month: "long", year: "numeric" });
  return `Semana del ${fechaCorta(r.actual.desde)} al ${fechaCorta(r.actual.hasta)}`;
}

/** "Mi negocio": solo el dueño. Control del día, la semana y el mes (envíos y gasto), el estado de
 * cuenta como un resumen bancario y los pagos, con la opción de informar uno. */
export default function NegocioPage() {
  return (
    <RequireRole roles={["cliente"]} soloDueno>
      <Negocio />
    </RequireRole>
  );
}

function Negocio() {
  const [seccion, setSeccion] = useState<Seccion>("panel");

  return (
    <div className="flex flex-col gap-6 p-4 md:p-8">
      <CabeceraSesion titulo="Mi negocio" />

      <div className="flex gap-2" role="tablist" aria-label="Secciones de Mi negocio">
        {SECCIONES.map((s) => (
          <Button
            key={s.valor}
            role="tab"
            aria-selected={seccion === s.valor}
            variant={seccion === s.valor ? "default" : "outline"}
            size="sm"
            onClick={() => setSeccion(s.valor)}
          >
            {s.label}
          </Button>
        ))}
      </div>

      {seccion === "panel" && <Panel />}
      {seccion === "cuenta" && <EstadoDeCuenta />}
      {seccion === "pagos" && <Pagos />}
    </div>
  );
}

// ---- Panel ----

/** Diferencia contra el período anterior, en texto neutro (subir no siempre es bueno: gasto). */
function Variacion({ actual, anterior, formato = String }: {
  actual: number; anterior: number; formato?: (n: number) => string;
}) {
  const dif = actual - anterior;
  if (dif === 0) return <>igual que el período anterior</>;
  return <>{dif > 0 ? "▲" : "▼"} {formato(Math.abs(dif))} vs. anterior ({formato(anterior)})</>;
}

function Panel() {
  const { fetchConSesion } = useAuth();
  const [periodo, setPeriodo] = useState<PeriodoNegocio>("semana");
  const [fecha, setFecha] = useState(hoyISO());
  const [datos, setDatos] = useState<{ clave: string; resumen: ResumenNegocio } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const clave = `${periodo}|${fecha}`;

  useEffect(() => {
    fetchConSesion(`/api/mi-cuenta/negocio/resumen?periodo=${periodo}&fecha=${fecha}`)
      .then((r) => leerJson<ResumenNegocio>(r))
      .then((resumen) => {
        setError(null);
        setDatos({ clave: `${periodo}|${fecha}`, resumen });
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el resumen."));
  }, [fetchConSesion, periodo, fecha]);

  const r = datos?.clave === clave ? datos.resumen : null;
  const esFuturo = r ? r.actual.hasta >= hoyISO() : true;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex gap-1 rounded-lg border p-1">
          {PERIODOS.map((p) => (
            <Button
              key={p.valor}
              size="sm"
              variant={periodo === p.valor ? "default" : "ghost"}
              onClick={() => {
                setPeriodo(p.valor);
                setFecha(hoyISO());
              }}
            >
              {p.label}
            </Button>
          ))}
        </div>
        <div className="flex items-center gap-1">
          <Button size="icon" variant="outline" aria-label="Período anterior" onClick={() => setFecha(moverPeriodo(fecha, periodo, -1))}>
            <ChevronLeft className="size-4" />
          </Button>
          <Button
            size="icon"
            variant="outline"
            aria-label="Período siguiente"
            disabled={esFuturo}
            onClick={() => setFecha(moverPeriodo(fecha, periodo, 1))}
          >
            <ChevronRight className="size-4" />
          </Button>
        </div>
        {r && <p className="text-sm font-medium first-letter:uppercase">{tituloPeriodo(r)}</p>}
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !r ? (
        <div className="h-64 animate-pulse rounded-xl border bg-muted" />
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <TarjetaConVariacion valor={r.actual.envios.total} etiqueta="Envíos" actual={r.actual.envios.total} anterior={r.anterior.envios.total} />
            <TarjetaConVariacion valor={r.actual.envios.entregados} etiqueta="Entregados" actual={r.actual.envios.entregados} anterior={r.anterior.envios.entregados} />
            <TarjetaConVariacion
              valor={r.actual.envios.fallidos}
              etiqueta="Fallidos o devueltos"
              actual={r.actual.envios.fallidos}
              anterior={r.anterior.envios.fallidos}
              alerta={r.actual.envios.fallidos > 0}
            />
            <TarjetaConVariacion valor={r.actual.envios.enCurso} etiqueta="En curso" actual={r.actual.envios.enCurso} anterior={r.anterior.envios.enCurso} />
          </div>

          {/* Gasto: son montos de envío. Con Portal:MostrarPrecios apagado (acta 4.30, suscriptores)
           * la API los manda en null y estas tarjetas no aparecen. */}
          {r.actual.facturable !== null && r.actual.comprometido !== null && (
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <div className="rounded-xl border bg-card p-4">
                <TarjetaMetrica valor={pesos(r.actual.facturable)} etiqueta="Gasto ya generado en el período" />
                <p className="mt-1 text-xs text-muted-foreground">
                  <Variacion actual={r.actual.facturable} anterior={r.anterior.facturable ?? 0} formato={pesos} />
                </p>
              </div>
              <div className="rounded-xl border bg-card p-4">
                <TarjetaMetrica valor={pesos(r.actual.comprometido)} etiqueta="Comprometido por envíos en curso" />
                {r.actual.sinPrecio > 0 && (
                  <p className="mt-1 text-xs text-amber-700">
                    {r.actual.sinPrecio} {r.actual.sinPrecio === 1 ? "envío todavía sin precio" : "envíos todavía sin precio"} (pendiente de cotización)
                  </p>
                )}
              </div>
            </div>
          )}

          {r.dias.length > 1 && <GraficoDias dias={r.dias} />}

          <Button
            variant="outline"
            className="self-start"
            render={<Link href={`/mis-envios?desde=${r.actual.desde}&hasta=${r.actual.hasta}`} />}
            nativeButton={false}
          >
            Ver los envíos del período
          </Button>
        </>
      )}
    </div>
  );
}

function TarjetaConVariacion({ valor, etiqueta, actual, anterior, alerta = false }: {
  valor: number; etiqueta: string; actual: number; anterior: number; alerta?: boolean;
}) {
  return (
    <div className="rounded-xl border bg-card p-4">
      <TarjetaMetrica valor={valor} etiqueta={etiqueta} tono={alerta ? "alerta" : "normal"} />
      <p className="mt-1 text-xs text-muted-foreground">
        <Variacion actual={actual} anterior={anterior} />
      </p>
    </div>
  );
}

/** Envíos por día del período: una sola serie (sin leyenda: el título la nombra), barras finas con
 * la punta redondeada apoyadas en la base, 2px de separación y tooltip por barra. La lista de abajo
 * es la vista en tabla para lectores de pantalla. */
function GraficoDias({ dias }: { dias: DiaNegocio[] }) {
  const [activo, setActivo] = useState<number | null>(null);
  const maximo = Math.max(1, ...dias.map((d) => d.envios));
  // Cuántas etiquetas del eje x entran sin pisarse: todas en una semana, una cada ~5 días en un mes.
  const cadaCuantas = dias.length > 10 ? 5 : 1;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Envíos por día</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="relative" onMouseLeave={() => setActivo(null)}>
          <div className="flex h-40 items-end gap-[2px] border-b border-border" aria-hidden>
            {dias.map((d, i) => (
              <div
                key={d.fecha}
                className="relative flex h-full flex-1 items-end"
                onMouseEnter={() => setActivo(i)}
                onFocus={() => setActivo(i)}
                onClick={() => setActivo(i)}
              >
                <div
                  className={`w-full rounded-t-[4px] transition-opacity ${activo === null || activo === i ? "opacity-100" : "opacity-50"}`}
                  style={{ height: `${(d.envios / maximo) * 100}%`, minHeight: d.envios > 0 ? 2 : 0, background: "var(--color-bf-azul, #0057d9)" }}
                />
              </div>
            ))}
          </div>
          <div className="mt-1 flex gap-[2px] text-[10px] text-muted-foreground" aria-hidden>
            {dias.map((d, i) => (
              <span key={d.fecha} className="flex-1 text-center tabular-nums">
                {i % cadaCuantas === 0 ? aFecha(d.fecha).getDate() : ""}
              </span>
            ))}
          </div>
          {activo !== null && (
            <div
              className="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-md border bg-popover px-2 py-1 text-xs shadow-md"
              style={{ left: `${((activo + 0.5) / dias.length) * 100}%` }}
            >
              <p className="font-medium">{fechaCorta(dias[activo].fecha)}</p>
              <p>{dias[activo].envios} envíos · {dias[activo].entregados} entregados</p>
              {dias[activo].facturable !== null && (
                <p className="text-muted-foreground">Gasto generado: {pesos(dias[activo].facturable)}</p>
              )}
            </div>
          )}
        </div>
        <ul className="sr-only">
          {dias.map((d) => (
            <li key={d.fecha}>
              {fechaCorta(d.fecha)}: {d.envios} envíos, {d.entregados} entregados
              {d.facturable !== null && `, gasto generado ${pesos(d.facturable)}`}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

// ---- Estado de cuenta ----

function EstadoDeCuenta() {
  const { fetchConSesion } = useAuth();
  const [desde, setDesde] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 89);
    return iso(d);
  });
  const [hasta, setHasta] = useState(hoyISO());
  const [cuenta, setCuenta] = useState<EstadoDeCuentaPropio | null>(null);
  const [facturas, setFacturas] = useState<FacturaPropia[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!desde || !hasta) return;
    fetchConSesion(`/api/mi-cuenta/estado-de-cuenta?desde=${desde}&hasta=${hasta}`)
      .then((r) => leerJson<EstadoDeCuentaPropio>(r))
      .then((c) => {
        setError(null);
        setCuenta(c);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el estado de cuenta."));
  }, [fetchConSesion, desde, hasta]);

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta/facturas")
      .then((r) => leerJson<FacturaPropia[]>(r))
      .then(setFacturas)
      .catch(() => setFacturas([]));
  }, [fetchConSesion]);

  return (
    <div className="flex flex-col gap-4">
      {cuenta && (
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <TarjetaMetrica valor={pesos(cuenta.saldoActual)} etiqueta="Saldo actual" />
          <TarjetaMetrica
            valor={pesos(cuenta.deudaVencida)}
            etiqueta="Deuda vencida"
            tono={cuenta.deudaVencida > 0 ? "alerta" : "normal"}
          />
          <TarjetaMetrica valor={pesos(cuenta.pendienteDeFacturar)} etiqueta="Pendiente de facturar" />
          <TarjetaMetrica
            valor={cuenta.servicioCortado ? "Servicio cortado" : "Al día"}
            etiqueta="Servicio"
            tono={cuenta.servicioCortado ? "alerta" : "normal"}
            chico
          />
        </div>
      )}

      <Card>
        <CardHeader className="flex flex-wrap items-end justify-between gap-3">
          <CardTitle className="text-base">Movimientos</CardTitle>
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1">
              <Label htmlFor="cuenta-desde" className="text-xs">Desde</Label>
              <Input id="cuenta-desde" type="date" value={desde} max={hasta} onChange={(e) => setDesde(e.target.value)} />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="cuenta-hasta" className="text-xs">Hasta</Label>
              <Input id="cuenta-hasta" type="date" value={hasta} min={desde} onChange={(e) => setHasta(e.target.value)} />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {error ? (
            <p className="text-sm text-destructive">{error}</p>
          ) : !cuenta ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : (
            <div className="flex flex-col text-sm">
              <FilaMovimiento fecha={cuenta.desde} descripcion="Saldo al inicio" saldo={cuenta.saldoInicial} tenue />
              {cuenta.movimientos.length === 0 && (
                <p className="py-3 text-muted-foreground">Sin facturas ni pagos en estas fechas.</p>
              )}
              {cuenta.movimientos.map((m) => (
                <FilaMovimiento
                  key={`${m.tipo}-${m.referencia}`}
                  fecha={m.fecha}
                  descripcion={m.descripcion}
                  debe={m.debe}
                  haber={m.haber}
                  saldo={m.saldo}
                />
              ))}
              <FilaMovimiento fecha={cuenta.hasta} descripcion="Saldo al cierre" saldo={cuenta.saldoFinal} tenue />
              <p className="pt-2 text-xs text-muted-foreground">
                Positivo = saldo a pagar. Los pagos informados aparecen acá cuando Administración los confirma.
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Facturas</CardTitle>
        </CardHeader>
        <CardContent>
          {!facturas ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : facturas.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no tenés facturas.</p>
          ) : (
            <ul className="flex flex-col divide-y">
              {facturas.map((f) => (
                <FacturaFila key={f.id} factura={f} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function FilaMovimiento({ fecha, descripcion, debe, haber, saldo, tenue = false }: {
  fecha: string; descripcion: string; debe?: number; haber?: number; saldo: number; tenue?: boolean;
}) {
  return (
    <div className={`grid grid-cols-[3.5rem_1fr_auto] items-baseline gap-x-3 border-b py-2 md:grid-cols-[4rem_1fr_7rem_7rem_7rem] ${tenue ? "text-muted-foreground" : ""}`}>
      <span className="tabular-nums">{fechaCorta(fecha)}</span>
      <span className="min-w-0 truncate">{descripcion}</span>
      <span className="hidden text-right tabular-nums md:block">{debe ? pesos(debe) : ""}</span>
      <span className="hidden text-right tabular-nums md:block">{haber ? pesos(haber) : ""}</span>
      <span className="text-right font-medium tabular-nums">
        {pesos(saldo)}
        {/* En el teléfono no hay columnas de debe/haber: el importe va debajo del saldo. */}
        {(debe || haber) ? (
          <span className="block text-xs font-normal text-muted-foreground md:hidden">
            {debe ? `+${pesos(debe)}` : `−${pesos(haber!)}`}
          </span>
        ) : null}
      </span>
    </div>
  );
}

function FacturaFila({ factura }: { factura: FacturaPropia }) {
  const { fetchConSesion } = useAuth();
  const [detalle, setDetalle] = useState<FacturaPropiaDetalle | null>(null);
  const [abierta, setAbierta] = useState(false);

  function alternar() {
    setAbierta((a) => !a);
    if (!detalle) {
      fetchConSesion(`/api/mi-cuenta/facturas/${factura.id}`)
        .then((r) => leerJson<FacturaPropiaDetalle>(r))
        .then(setDetalle)
        .catch(() => setDetalle(null));
    }
  }

  return (
    <li className="py-3">
      <button type="button" onClick={alternar} aria-expanded={abierta} className="flex w-full items-baseline justify-between gap-3 text-left text-sm">
        <span>
          <span className="font-medium">Factura #{factura.id}</span>
          <span className="text-muted-foreground"> · {fechaCorta(factura.periodoDesde)} al {fechaCorta(factura.periodoHasta)} · vence {fechaCorta(factura.fechaVencimiento)}</span>
        </span>
        <span className="shrink-0 text-right">
          <span className="font-medium tabular-nums">{pesos(factura.total)}</span>
          <span className={`block text-xs ${factura.estado === "vencida" ? "text-destructive" : "text-muted-foreground"}`}>
            {etiquetaEstadoFactura(factura.estado)}
            {factura.saldo > 0 && factura.saldo < factura.total && ` · resta ${pesos(factura.saldo)}`}
          </span>
        </span>
      </button>
      {abierta && (
        <div className="mt-2 rounded-lg bg-muted/40 p-3 text-sm">
          {!detalle ? (
            <p className="text-muted-foreground">Cargando…</p>
          ) : (
            <ul className="flex flex-col gap-1">
              {detalle.items.map((i, idx) => (
                <li key={idx} className="flex justify-between gap-3">
                  <span className="min-w-0">{i.descripcion}</span>
                  <span className="shrink-0 tabular-nums">{i.monto === null ? "—" : pesos(i.monto)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </li>
  );
}

// ---- Pagos ----

function Pagos() {
  const { fetchConSesion } = useAuth();
  const [pagos, setPagos] = useState<PagosPropios | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion("/api/mi-cuenta/pagos")
      .then((r) => leerJson<PagosPropios>(r))
      .then(setPagos)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los pagos."));
  }, [fetchConSesion]);

  useEffect(cargar, [cargar]);

  async function verComprobante(id: number) {
    try {
      const resp = await fetchConSesion(`/api/mi-cuenta/pagos-informados/${id}/comprobante`);
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      window.open(URL.createObjectURL(await resp.blob()), "_blank");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo abrir el comprobante.");
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <InformarPago onInformado={cargar} />

      {error && <p className="text-sm text-destructive">{error}</p>}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Pagos informados</CardTitle>
        </CardHeader>
        <CardContent>
          {!pagos ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : pagos.informados.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no informaste pagos desde acá.</p>
          ) : (
            <ul className="flex flex-col divide-y text-sm">
              {pagos.informados.map((p) => (
                <li key={p.id} className="flex flex-col gap-1 py-3">
                  <div className="flex items-baseline justify-between gap-3">
                    <span>
                      <span className="font-medium tabular-nums">{pesos(p.monto)}</span>
                      <span className="text-muted-foreground"> · {etiquetaMedioPago(p.medio)} · {fechaCorta(p.fechaPago)} · informó {p.informadoPor}</span>
                    </span>
                    <span
                      className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ${
                        p.estado === "confirmado"
                          ? "bg-green-100 text-green-800"
                          : p.estado === "rechazado"
                            ? "bg-red-100 text-red-800"
                            : "bg-amber-100 text-amber-800"
                      }`}
                    >
                      {etiquetaEstadoPagoInformado(p.estado)}
                    </span>
                  </div>
                  {p.nota && <p className="text-muted-foreground">{p.nota}</p>}
                  {p.estado === "rechazado" && p.motivoRechazo && (
                    <p className="text-destructive">Motivo: {p.motivoRechazo}</p>
                  )}
                  {p.tieneComprobante && (
                    <Button variant="link" size="sm" className="h-auto self-start p-0" onClick={() => verComprobante(p.id)}>
                      <Paperclip className="size-3" /> Ver comprobante
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Pagos imputados</CardTitle>
        </CardHeader>
        <CardContent>
          {!pagos ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : pagos.imputados.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no hay pagos imputados.</p>
          ) : (
            <ul className="flex flex-col divide-y text-sm">
              {pagos.imputados.map((p) => (
                <li key={p.id} className="flex items-baseline justify-between gap-3 py-2">
                  <span className="text-muted-foreground">
                    {fechaCorta(p.fechaPago)} · {etiquetaMedioPago(p.medio)}
                  </span>
                  <span className={`font-medium tabular-nums ${p.monto < 0 ? "text-destructive" : ""}`}>{pesos(p.monto)}</span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function InformarPago({ onInformado }: { onInformado: () => void }) {
  const { fetchConSesion } = useAuth();
  const [monto, setMonto] = useState("");
  const [fechaPago, setFechaPago] = useState(hoyISO());
  const [medio, setMedio] = useState<string>("transferencia");
  const [nota, setNota] = useState("");
  const [comprobante, setComprobante] = useState<File | null>(null);
  const [archivoKey, setArchivoKey] = useState(0);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setOk(false);
    if (!(Number(monto) > 0)) {
      setError("El monto debe ser mayor a cero.");
      return;
    }
    setEnviando(true);
    try {
      const form = new FormData();
      form.append("monto", String(Number(monto)));
      form.append("fechaPago", fechaPago);
      form.append("medio", medio);
      if (nota.trim()) form.append("nota", nota.trim());
      // El backend guarda solo JPEG: la foto se comprime y convierte acá, igual que en la PWA.
      if (comprobante) form.append("comprobante", await comprimirFoto(comprobante), "comprobante.jpg");

      const resp = await fetchConSesion("/api/mi-cuenta/pagos-informados", { method: "POST", body: form });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setMonto("");
      setNota("");
      setComprobante(null);
      setArchivoKey((k) => k + 1);
      setOk(true);
      onInformado();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo informar el pago.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Informar un pago</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={enviar} className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            Avisanos un pago que ya hiciste. Administración lo revisa y, al confirmarlo, se descuenta de tu saldo.
          </p>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div className="flex flex-col gap-2">
              <Label htmlFor="pago-monto">Monto</Label>
              <Input
                id="pago-monto"
                type="number"
                inputMode="decimal"
                min={0.01}
                step={0.01}
                required
                value={monto}
                onChange={(e) => setMonto(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="pago-fecha">Fecha del pago</Label>
              <Input id="pago-fecha" type="date" required max={hoyISO()} value={fechaPago} onChange={(e) => setFechaPago(e.target.value)} />
            </div>
          </div>
          <div className="flex flex-col gap-2">
            <Label>Medio</Label>
            <div className="flex flex-wrap gap-2">
              {MEDIOS_PAGO.map((m) => (
                <Button key={m.value} type="button" size="sm" variant={medio === m.value ? "default" : "outline"} onClick={() => setMedio(m.value)}>
                  {m.label}
                </Button>
              ))}
            </div>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="pago-nota">Nota (opcional)</Label>
            <Input id="pago-nota" placeholder="Ej.: número de operación" value={nota} onChange={(e) => setNota(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="pago-comprobante">Foto del comprobante (opcional)</Label>
            <Input
              key={archivoKey}
              id="pago-comprobante"
              type="file"
              accept="image/*"
              onChange={(e) => setComprobante(e.target.files?.[0] ?? null)}
            />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          {ok && <p className="text-sm text-green-700">Listo: el pago quedó informado y en revisión.</p>}
          <Button type="submit" disabled={enviando} className="self-start">
            {enviando ? "Enviando…" : "Informar pago"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
