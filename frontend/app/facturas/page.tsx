"use client";

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { OrdenMovil } from "@/components/OrdenMovil";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FacturaDetalleContenido } from "@/components/FacturaDetalleContenido";
import { leerError, leerJson } from "@/lib/api/errores";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import {
  etiquetaEstadoFactura,
  type ClienteSeleccion,
  type FacturaResumen,
  type ResultadoCierreCliente,
} from "@/lib/dominio/tipos";

const ESTADOS_FACTURA = ["pendiente", "parcial", "pagada", "vencida"] as const;

interface Columna {
  campo: "id" | "vencimiento" | "total" | "cliente";
  etiqueta: string;
}

const COLUMNAS: Columna[] = [
  { campo: "id", etiqueta: "ID" },
  { campo: "cliente", etiqueta: "Cliente" },
  { campo: "vencimiento", etiqueta: "Vencimiento" },
  { campo: "total", etiqueta: "Total" },
];

export default function FacturasPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <Suspense fallback={null}>
        <ListaFacturas />
      </Suspense>
    </RequireRole>
  );
}

function ListaFacturas() {
  const { fetchConSesion } = useAuth();
  const searchParams = useSearchParams();
  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [q, setQ] = useState("");
  // Deep-link desde la Card de cuenta corriente de un cliente (/clientes/[id]) — solo se lee al
  // montar, después el filtro se maneja como cualquier otro (el usuario puede sacarlo).
  const [clienteId, setClienteId] = useState<string | null>(() => searchParams.get("clienteId"));
  const [estado, setEstado] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [facturaAbierta, setFacturaAbierta] = useState<number | null>(null);
  const [cierreAbierto, setCierreAbierto] = useState(false);

  const {
    items: facturas, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina,
    orden, totalPaginas, alternarOrden, indicadorOrden, conReinicioDePagina, recargar,
  } = useListadoPaginado<FacturaResumen>({
    ruta: "/api/facturas",
    filtros: { q: q.trim(), clienteId: clienteId ?? "", estado, fechaDesde, fechaHasta },
    ordenInicial: "-fecha",
  });

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch(() => setClientes([]));
  }, [fetchConSesion]);

  const hayFiltros = q || clienteId || estado || fechaDesde || fechaHasta;

  return (
    <>
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Facturas" />

      <div className="flex flex-wrap items-end justify-between gap-4 mb-4">
        <div className="flex flex-wrap items-end gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-q">Buscar</Label>
            <Input
              id="filtro-q"
              placeholder="ID o cliente"
              value={q}
              onChange={(e) => conReinicioDePagina(setQ)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Cliente</Label>
            <ComboboxBusqueda
              items={clientes.map((c) => ({ value: String(c.id), label: c.razonSocial }))}
              value={clienteId}
              onValueChange={conReinicioDePagina(setClienteId)}
              placeholder="Todos"
              className="w-48"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Estado</Label>
            <Select
              items={[{ value: "todos", label: "Todos" }, ...ESTADOS_FACTURA.map((e) => ({ value: e, label: etiquetaEstadoFactura(e) }))]}
              value={estado || "todos"}
              onValueChange={(v) => conReinicioDePagina(setEstado)(!v || v === "todos" ? "" : v)}
            >
              <SelectTrigger className="w-36">
                <SelectValue placeholder="Todos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="todos">Todos</SelectItem>
                {ESTADOS_FACTURA.map((e) => (
                  <SelectItem key={e} value={e}>
                    {etiquetaEstadoFactura(e)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-desde">Emisión desde</Label>
            <Input
              id="filtro-desde"
              type="date"
              value={fechaDesde}
              onChange={(e) => conReinicioDePagina(setFechaDesde)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-hasta">Emisión hasta</Label>
            <Input
              id="filtro-hasta"
              type="date"
              value={fechaHasta}
              onChange={(e) => conReinicioDePagina(setFechaHasta)(e.target.value)}
              className="w-40"
            />
          </div>
          {hayFiltros && (
            <Button
              variant="outline"
              onClick={() => {
                setQ("");
                setClienteId(null);
                setEstado("");
                setFechaDesde("");
                setFechaHasta("");
                setPagina(1);
              }}
            >
              Limpiar filtros
            </Button>
          )}
        </div>
        <Button onClick={() => setCierreAbierto(true)}>Cerrar ciclo</Button>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !facturas ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : facturas.length === 0 ? (
        <p className="text-muted-foreground">No hay facturas que coincidan con estos filtros.</p>
      ) : (
        <>
          <OrdenMovil columnas={COLUMNAS} orden={orden} alternarOrden={alternarOrden} />
          <Table>
            <TableHeader>
              <TableRow>
                {COLUMNAS.map((c) => (
                  <TableHead key={c.campo}>
                    <button
                      type="button"
                      onClick={() => alternarOrden(c.campo)}
                      className="flex items-center gap-1 hover:underline"
                    >
                      {c.etiqueta}
                      {indicadorOrden(c.campo) && <span className="text-xs">{indicadorOrden(c.campo)}</span>}
                    </button>
                  </TableHead>
                ))}
                <TableHead>Pagado</TableHead>
                <TableHead>Saldo</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {facturas.map((f) => (
                <TableRow
                  key={f.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => setFacturaAbierta(f.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setFacturaAbierta(f.id);
                    }
                  }}
                  className={`cursor-pointer hover:bg-muted/50 ${f.estado === "vencida" ? "bg-destructive/10" : ""}`}
                >
                  <TableCell>{f.id}</TableCell>
                  <TableCell>{f.clienteRazonSocial}</TableCell>
                  <TableCell>{f.fechaVencimiento}</TableCell>
                  <TableCell>${f.total.toLocaleString("es-AR")}</TableCell>
                  <TableCell>${f.pagado.toLocaleString("es-AR")}</TableCell>
                  <TableCell>${f.saldo.toLocaleString("es-AR")}</TableCell>
                  <TableCell>
                    <span className={f.estado === "vencida" ? "text-destructive font-medium" : undefined}>
                      {etiquetaEstadoFactura(f.estado)}
                    </span>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

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
    </div>

    <Dialog open={facturaAbierta !== null} onOpenChange={(open) => !open && setFacturaAbierta(null)}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Factura #{facturaAbierta}</DialogTitle>
        </DialogHeader>
        {facturaAbierta !== null && <FacturaDetalleContenido facturaId={facturaAbierta} />}
      </DialogContent>
    </Dialog>

    <CierreCicloDialog
      open={cierreAbierto}
      onOpenChange={setCierreAbierto}
      onEmitido={recargar}
    />
    </>
  );
}

function CierreCicloDialog({
  open,
  onOpenChange,
  onEmitido,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onEmitido: () => void;
}) {
  const { fetchConSesion } = useAuth();
  const [fecha, setFecha] = useState(() => new Date().toISOString().slice(0, 10));
  const [previsualizacion, setPrevisualizacion] = useState<ResultadoCierreCliente[] | null>(null);
  const [cargando, setCargando] = useState(false);
  const [emitiendo, setEmitiendo] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [emitido, setEmitido] = useState<ResultadoCierreCliente[] | null>(null);

  // Reset en el handler de cierre, no en un efecto que observe `open` — evita el cascading
  // render que react-hooks/set-state-in-effect señala (mismo criterio que el resto del repo,
  // ver construccion_v1.md changelog 1.13).
  function manejarCambioApertura(abierto: boolean) {
    if (!abierto) {
      setPrevisualizacion(null);
      setEmitido(null);
      setError(null);
    }
    onOpenChange(abierto);
  }

  async function previsualizar() {
    setCargando(true);
    setError(null);
    setEmitido(null);
    try {
      const resp = await fetchConSesion(`/api/facturas/cierre/previsualizacion?fecha=${fecha}`);
      setPrevisualizacion(await leerJson<ResultadoCierreCliente[]>(resp));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo previsualizar el cierre.");
    } finally {
      setCargando(false);
    }
  }

  async function confirmarEmision() {
    setEmitiendo(true);
    setError(null);
    try {
      const resp = await fetchConSesion("/api/facturas/cierre", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fecha }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const resultado = await leerJson<ResultadoCierreCliente[]>(resp);
      setEmitido(resultado);
      setPrevisualizacion(null);
      onEmitido();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo emitir el cierre.");
    } finally {
      setEmitiendo(false);
    }
  }

  const conFactura = (lista: ResultadoCierreCliente[]) => lista.filter((r) => r.facturaId !== null);
  const sinItems = (lista: ResultadoCierreCliente[]) => lista.filter((r) => r.facturaId === null);

  return (
    <Dialog open={open} onOpenChange={manejarCambioApertura}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Cerrar ciclo de facturación</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            Emite una factura por cada cliente cuyo ciclo (quincenal o mensual) cerró hasta la fecha
            elegida. Una factura emitida es inmutable — previsualizá antes de confirmar.
          </p>
          <div className="flex items-end gap-2">
            <div className="flex flex-col gap-2">
              <Label htmlFor="fecha-cierre">Fecha de cierre</Label>
              <Input
                id="fecha-cierre"
                type="date"
                value={fecha}
                onChange={(e) => {
                  setFecha(e.target.value);
                  setPrevisualizacion(null);
                  setEmitido(null);
                }}
                className="w-44"
              />
            </div>
            <Button variant="outline" onClick={previsualizar} disabled={cargando}>
              {cargando ? "Calculando…" : "Previsualizar"}
            </Button>
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          {previsualizacion && (
            <div className="flex flex-col gap-3 border-t pt-4">
              {conFactura(previsualizacion).length === 0 ? (
                <p className="text-sm text-muted-foreground">Ningún cliente tiene ítems facturables en este cierre.</p>
              ) : (
                <ul className="flex flex-col gap-2">
                  {conFactura(previsualizacion).map((r) => (
                    <li key={r.clienteId} className="text-sm border-b pb-2 flex justify-between gap-4">
                      <span>
                        {r.razonSocial} — {r.periodoDesde} al {r.periodoHasta} ({r.cantidadItems} ítem{r.cantidadItems === 1 ? "" : "s"})
                        {r.ajustesPendientes > 0 && (
                          <span className="ml-2 text-xs font-medium text-amber-600">
                            {r.ajustesPendientes} ajuste{r.ajustesPendientes === 1 ? "" : "s"} pendiente{r.ajustesPendientes === 1 ? "" : "s"} — no entra en esta factura
                          </span>
                        )}
                      </span>
                      <span className="shrink-0 font-medium">${r.total.toLocaleString("es-AR")}</span>
                    </li>
                  ))}
                </ul>
              )}
              {sinItems(previsualizacion).length > 0 && (
                <p className="text-xs text-muted-foreground">
                  Sin ítems facturables: {sinItems(previsualizacion).map((r) => r.razonSocial).join(", ")}.
                </p>
              )}
              {conFactura(previsualizacion).length > 0 && (
                <Button onClick={confirmarEmision} disabled={emitiendo}>
                  {emitiendo ? "Emitiendo…" : `Confirmar emisión de ${conFactura(previsualizacion).length} factura${conFactura(previsualizacion).length === 1 ? "" : "s"}`}
                </Button>
              )}
            </div>
          )}

          {emitido && (
            <div className="border-t pt-4">
              <p className="text-sm font-medium text-green-700">
                Se emitieron {conFactura(emitido).length} factura{conFactura(emitido).length === 1 ? "" : "s"}.
              </p>
              <Button variant="outline" size="sm" className="mt-2" onClick={() => manejarCambioApertura(false)}>
                Cerrar
              </Button>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
