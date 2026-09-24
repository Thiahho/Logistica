"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { BarrasHorizontales, GraficoEntregas, GraficoMargen, pesos } from "@/components/tablero/Graficos";
import { leerJson } from "@/lib/api/errores";
import type { Tablero } from "@/lib/dominio/tipos";

/** toISOString() sobre un Date local devuelve el día anterior al oeste de Greenwich: se corrige el offset. */
function fechaLocal(d: Date): string {
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}
const haceDias = (n: number) => fechaLocal(new Date(Date.now() - n * 86_400_000));

const PRESETS = [
  { dias: 7, etiqueta: "7 días" },
  { dias: 30, etiqueta: "30 días" },
  { dias: 90, etiqueta: "90 días" },
];

interface Opcion {
  valor: string;
  etiqueta: string;
}

export default function TableroPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <TableroIndicadores />
    </RequireRole>
  );
}

/**
 * B2 — tablero de indicadores, etapa E3 (acta RF-44; definiciones en docs/diseño_b2_tablero.md). Los
 * filtros van en una fila arriba y gobiernan todo lo de abajo. Al volver a consultar, lo anterior queda
 * a la vista atenuado en vez de un salto a vacío.
 */
function TableroIndicadores() {
  const { fetchConSesion } = useAuth();
  const [desde, setDesde] = useState(() => haceDias(29));
  const [hasta, setHasta] = useState(() => haceDias(0));
  const [cliente, setCliente] = useState("");
  const [zona, setZona] = useState("");
  const [vehiculo, setVehiculo] = useState("");
  const [repartidor, setRepartidor] = useState("");
  const [opciones, setOpciones] = useState<Record<"clientes" | "zonas" | "vehiculos" | "repartidores", Opcion[]>>({
    clientes: [],
    zonas: [],
    vehiculos: [],
    repartidores: [],
  });
  // La respuesta se guarda con la consulta que la pidió: "cargando" es que la vigente todavía no llegó.
  const [respuesta, setRespuesta] = useState<{ consulta: string; tablero: Tablero } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const cargarLista = <T,>(url: string, mapear: (x: T) => Opcion) =>
      fetchConSesion(url)
        .then((r) => leerJson<T[]>(r))
        .then((l) => l.map(mapear))
        .catch(() => [] as Opcion[]);
    Promise.all([
      cargarLista<{ id: number; razonSocial: string }>("/api/clientes/seleccion", (c) => ({ valor: String(c.id), etiqueta: c.razonSocial })),
      cargarLista<{ id: number; codigo: string; nombre: string }>("/api/zonas", (z) => ({ valor: String(z.id), etiqueta: `${z.codigo} — ${z.nombre}` })),
      cargarLista<{ id: number; patente: string }>("/api/vehiculos/seleccion", (v) => ({ valor: String(v.id), etiqueta: v.patente })),
      cargarLista<{ id: string; nombre: string }>("/api/usuarios/seleccion?rol=repartidor", (u) => ({ valor: u.id, etiqueta: u.nombre })),
    ]).then(([clientes, zonas, vehiculos, repartidores]) => setOpciones({ clientes, zonas, vehiculos, repartidores }));
  }, [fetchConSesion]);

  const params = new URLSearchParams({ desde, hasta });
  if (cliente) params.set("clienteId", cliente);
  if (zona) params.set("zonaId", zona);
  if (vehiculo) params.set("vehiculoId", vehiculo);
  if (repartidor) params.set("repartidorId", repartidor);
  const consulta = params.toString();

  useEffect(() => {
    let vigente = true;
    fetchConSesion(`/api/tablero?${consulta}`)
      .then((r) => leerJson<Tablero>(r))
      .then((tablero) => {
        if (!vigente) return;
        setRespuesta({ consulta, tablero });
        setError(null);
      })
      .catch((err) => vigente && setError(err instanceof Error ? err.message : "No se pudo calcular el tablero."));
    return () => {
      vigente = false;
    };
  }, [fetchConSesion, consulta]);

  const datos = respuesta?.tablero ?? null;
  const cargando = respuesta?.consulta !== consulta && !error;

  const nd = "sin datos";
  const num = (v: number | null, sufijo = "") => (v === null ? nd : `${v.toLocaleString("es-AR")}${sufijo}`);
  const plata = (v: number | null) => (v === null ? nd : pesos(v));
  const ruta = (v: string) => (datos?.indicadoresDeRuta ? v : "no aplica");

  return (
    <div className="p-4 md:p-8 flex flex-col gap-6 max-w-6xl">
      <CabeceraSesion titulo="Tablero" />

      {/* Filtros: una fila, arriba de todo lo que gobiernan. Período primero. */}
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex gap-1">
          {PRESETS.map((p) => (
            <Button
              key={p.dias}
              size="sm"
              variant={desde === haceDias(p.dias - 1) && hasta === haceDias(0) ? "default" : "outline"}
              onClick={() => {
                setDesde(haceDias(p.dias - 1));
                setHasta(haceDias(0));
              }}
            >
              {p.etiqueta}
            </Button>
          ))}
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="desde" className="text-xs">Desde</Label>
          <Input id="desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} className="w-40" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="hasta" className="text-xs">Hasta</Label>
          <Input id="hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} className="w-40" />
        </div>
        <Filtro etiqueta="Cliente" valor={cliente} onCambio={setCliente} opciones={opciones.clientes} />
        <Filtro etiqueta="Zona" valor={zona} onCambio={setZona} opciones={opciones.zonas} />
        <Filtro etiqueta="Vehículo" valor={vehiculo} onCambio={setVehiculo} opciones={opciones.vehiculos} />
        <Filtro etiqueta="Repartidor" valor={repartidor} onCambio={setRepartidor} opciones={opciones.repartidores} />
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}

      {!datos ? (
        !error && <p className="text-sm text-muted-foreground">Calculando…</p>
      ) : (
        <div className={`flex flex-col gap-6 transition-opacity ${cargando ? "opacity-60" : ""}`}>
          {!datos.indicadoresDeRuta && (
            <p className="rounded-lg border bg-muted/50 p-3 text-sm text-muted-foreground">
              Con un filtro de cliente o de zona no se muestran km, tiempo, costo, margen ni ocupación: una ruta lleva
              pedidos de varios clientes y zonas, y su costo no se reparte entre ellos.
            </p>
          )}

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
            <TarjetaMetrica valor={num(datos.entregasPorDia)} etiqueta={`Entregas por día (${datos.entregas} en total)`} />
            <TarjetaMetrica valor={ruta(num(datos.kmPorEntrega, " km"))} etiqueta="Km por entrega" />
            <TarjetaMetrica valor={ruta(num(datos.minutosPorEntrega, " min"))} etiqueta="Tiempo por entrega" />
            <TarjetaMetrica valor={pesos(datos.facturacion)} etiqueta="Facturación" />
            <TarjetaMetrica
              valor={num(datos.pctCancelaciones, "%")}
              etiqueta={`Cancelaciones (${datos.cancelados})`}
            />
            <TarjetaMetrica valor={ruta(plata(datos.costoPorEntrega))} etiqueta="Costo por entrega" />
            <TarjetaMetrica valor={ruta(plata(datos.costoPorRuta))} etiqueta={`Costo por ruta (${datos.rutasCerradas} cerradas)`} />
            <TarjetaMetrica
              valor={ruta(plata(datos.margenPorRuta))}
              etiqueta="Margen por ruta"
              tono={datos.margenPorRuta !== null && datos.margenPorRuta < 0 ? "alerta" : "normal"}
            />
            <TarjetaMetrica valor={ruta(num(datos.pctOcupacion, "%"))} etiqueta="Ocupación de flota" />
            <TarjetaMetrica valor="sin datos" etiqueta="NPS (sin encuesta, Anexo I §7)" />
          </div>

          <div className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Entregas por día</CardTitle>
              </CardHeader>
              <CardContent>
                <GraficoEntregas datos={datos.porDia} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">
                  Margen por día
                  {datos.margenTotal !== null && (
                    <span className="ml-2 text-sm font-normal text-muted-foreground">{pesos(datos.margenTotal)} en el período</span>
                  )}
                </CardTitle>
              </CardHeader>
              <CardContent>
                {datos.indicadoresDeRuta ? (
                  <GraficoMargen datos={datos.porDia} />
                ) : (
                  <p className="text-sm text-muted-foreground">No aplica con filtro de cliente o zona.</p>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Facturación por cliente</CardTitle>
              </CardHeader>
              <CardContent>
                {datos.facturacionPorCliente.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Sin facturación en el período.</p>
                ) : (
                  <BarrasHorizontales datos={datos.facturacionPorCliente} columna="Cliente" />
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Facturación por rango</CardTitle>
              </CardHeader>
              <CardContent>
                <BarrasHorizontales datos={datos.facturacionPorRango} columna="Rango" />
                <p className="mt-2 text-xs text-muted-foreground">Por el rango de cada cliente hoy.</p>
              </CardContent>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}

function Filtro({ etiqueta, valor, onCambio, opciones }: { etiqueta: string; valor: string; onCambio: (v: string) => void; opciones: Opcion[] }) {
  return (
    <div className="flex flex-col gap-1">
      <Label className="text-xs">{etiqueta}</Label>
      <Select
        items={[{ value: "todos", label: "Todos" }, ...opciones.map((o) => ({ value: o.valor, label: o.etiqueta }))]}
        value={valor || "todos"}
        onValueChange={(v) => onCambio(!v || v === "todos" ? "" : v)}
      >
        <SelectTrigger className="w-40">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="todos">Todos</SelectItem>
          {opciones.map((o) => (
            <SelectItem key={o.valor} value={o.valor}>
              {o.etiqueta}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
