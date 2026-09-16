"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
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
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PedidoDetalleContenido } from "@/components/PedidoDetalleContenido";
import { leerJson } from "@/lib/api/errores";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { ESTADOS_PEDIDO, type ClienteSeleccion, type DeliveryResumen } from "@/lib/dominio/tipos";

interface Columna {
  campo: "id" | "total" | "fecha";
  etiqueta: string;
}

const COLUMNAS: Columna[] = [
  { campo: "id", etiqueta: "ID" },
  { campo: "total", etiqueta: "Total" },
  { campo: "fecha", etiqueta: "Entrega" },
];

export default function DeliverysPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ListaDeliverys />
    </RequireRole>
  );
}

function ListaDeliverys() {
  const { fetchConSesion } = useAuth();
  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [q, setQ] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [estado, setEstado] = useState("");
  const [clienteId, setClienteId] = useState<string | null>(null);
  const [deliveryAbierto, setDeliveryAbierto] = useState<number | null>(null);

  const {
    items: deliverys, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina,
    totalPaginas, alternarOrden, indicadorOrden, conReinicioDePagina, recargar,
  } = useListadoPaginado<DeliveryResumen>({
    ruta: "/api/deliverys",
    filtros: { q: q.trim(), fechaDesde, fechaHasta, estado, clienteId: clienteId ?? "" },
    ordenInicial: "-fecha",
  });

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch(() => setClientes([]));
  }, [fetchConSesion]);

  const hayFiltros = q || fechaDesde || fechaHasta || estado || clienteId;

  return (
    <>
    <div className="p-8">
      <CabeceraSesion titulo="Deliverys y urgencias" />
      <p className="text-sm text-muted-foreground mb-4 max-w-2xl">
        Servicio punto a punto ad-hoc (Anexo I, D14): sin retiro programado, con precio congelado
        en el momento del alta según la zona de destino, el % de recargo por urgencia y el % de
        recargo según el km recorrido (Anexo I §10.2-N).
      </p>

      <div className="flex flex-wrap items-end justify-between gap-4 mb-4">
        <div className="flex flex-wrap items-end gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-q">Buscar</Label>
            <Input
              id="filtro-q"
              placeholder="ID o destinatario"
              value={q}
              onChange={(e) => conReinicioDePagina(setQ)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-desde">Entrega desde</Label>
            <Input
              id="filtro-desde"
              type="date"
              value={fechaDesde}
              onChange={(e) => conReinicioDePagina(setFechaDesde)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-hasta">Entrega hasta</Label>
            <Input
              id="filtro-hasta"
              type="date"
              value={fechaHasta}
              onChange={(e) => conReinicioDePagina(setFechaHasta)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Estado</Label>
            <Select
              items={[{ value: "todos", label: "Todos" }, ...ESTADOS_PEDIDO.map((e) => ({ value: e, label: e }))]}
              value={estado || "todos"}
              onValueChange={(v) => conReinicioDePagina(setEstado)(!v || v === "todos" ? "" : v)}
            >
              <SelectTrigger className="w-40">
                <SelectValue placeholder="Todos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="todos">Todos</SelectItem>
                {ESTADOS_PEDIDO.map((e) => (
                  <SelectItem key={e} value={e}>
                    {e}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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
          {hayFiltros && (
            <Button
              variant="outline"
              onClick={() => {
                setQ("");
                setFechaDesde("");
                setFechaHasta("");
                setEstado("");
                setClienteId(null);
                setPagina(1);
              }}
            >
              Limpiar filtros
            </Button>
          )}
        </div>
        <div className="flex gap-2">
          <Button render={<Link href="/deliverys/nuevo" />} nativeButton={false}>
            Nuevo delivery
          </Button>
        </div>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !deliverys ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : deliverys.length === 0 ? (
        <p className="text-muted-foreground">No hay deliverys que coincidan con estos filtros.</p>
      ) : (
        <>
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
                <TableHead>Cliente</TableHead>
                <TableHead>Destinatario</TableHead>
                <TableHead>Km</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {deliverys.map((d) => (
                <TableRow
                  key={d.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => setDeliveryAbierto(d.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setDeliveryAbierto(d.id);
                    }
                  }}
                  className="cursor-pointer hover:bg-muted/50"
                >
                  <TableCell>{d.id}</TableCell>
                  <TableCell>
                    {d.total !== null ? `$${d.total.toLocaleString("es-AR")}` : "—"}
                  </TableCell>
                  <TableCell>{d.fechaEntrega}</TableCell>
                  <TableCell>{d.clienteRazonSocial}</TableCell>
                  <TableCell>
                    {d.destinatarioNombre}
                    {d.urgente && (
                      <span className="ml-2 text-xs font-medium text-amber-600">urgente</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {d.kmCobrados !== null ? `${d.kmCobrados.toLocaleString("es-AR")} km` : "—"}
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

    <Dialog open={deliveryAbierto !== null} onOpenChange={(open) => !open && setDeliveryAbierto(null)}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Delivery #{deliveryAbierto}</DialogTitle>
        </DialogHeader>
        {deliveryAbierto !== null && (
          <PedidoDetalleContenido key={deliveryAbierto} pedidoId={deliveryAbierto} onCambio={recargar} />
        )}
      </DialogContent>
    </Dialog>
    </>
  );
}
