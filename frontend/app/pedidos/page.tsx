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
import { OrdenMovil } from "@/components/OrdenMovil";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PedidoDetalleContenido } from "@/components/PedidoDetalleContenido";
import { leerJson } from "@/lib/api/errores";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { ESTADOS_PEDIDO, type ClienteSeleccion, type PedidoResumen } from "@/lib/dominio/tipos";

interface Columna {
  campo: "id" | "estado" | "total" | "fecha";
  etiqueta: string;
}

const COLUMNAS: Columna[] = [
  { campo: "id", etiqueta: "ID" },
  { campo: "estado", etiqueta: "Estado" },
  { campo: "total", etiqueta: "Total" },
  { campo: "fecha", etiqueta: "Entrega" },
];

export default function PedidosPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ListaPedidos />
    </RequireRole>
  );
}

function ListaPedidos() {
  const { fetchConSesion } = useAuth();
  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [q, setQ] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [estado, setEstado] = useState("");
  const [clienteId, setClienteId] = useState<string | null>(null);
  const [pedidoAbierto, setPedidoAbierto] = useState<number | null>(null);

  const {
    items: pedidos, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina,
    orden, totalPaginas, alternarOrden, indicadorOrden, conReinicioDePagina, recargar,
  } = useListadoPaginado<PedidoResumen>({
    ruta: "/api/pedidos",
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
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Pedidos del día" />

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
          <Button render={<Link href="/pedidos/nuevo" />} nativeButton={false}>
            Nuevo pedido
          </Button>
        </div>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !pedidos ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : pedidos.length === 0 ? (
        <p className="text-muted-foreground">No hay pedidos que coincidan con estos filtros.</p>
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
                <TableHead>Cliente</TableHead>
                <TableHead>Destinatario</TableHead>
                <TableHead>Bultos</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {pedidos.map((p) => (
                <TableRow
                  key={p.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => setPedidoAbierto(p.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setPedidoAbierto(p.id);
                    }
                  }}
                  className={`cursor-pointer hover:bg-muted/50 ${p.direccionDudosa ? "bg-destructive/10" : ""}`}
                >
                  <TableCell>{p.id}</TableCell>
                  <TableCell>{p.estado}</TableCell>
                  <TableCell>
                    {p.total !== null ? `$${p.total.toLocaleString("es-AR")}` : (
                      <span className="text-muted-foreground">Pendiente de armado</span>
                    )}
                  </TableCell>
                  <TableCell>{p.fechaEntrega}</TableCell>
                  <TableCell>{p.clienteRazonSocial}</TableCell>
                  <TableCell>
                    {p.destinatarioNombre}
                    {p.direccionDudosa && (
                      <span className="ml-2 text-xs font-medium text-destructive">
                        dirección dudosa
                      </span>
                    )}
                    {p.requiereCotizacion && (
                      <span className="ml-2 text-xs font-medium text-amber-600">
                        requiere cotización
                      </span>
                    )}
                  </TableCell>
                  <TableCell>{p.bultos}</TableCell>
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

    <Dialog open={pedidoAbierto !== null} onOpenChange={(open) => !open && setPedidoAbierto(null)}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Pedido #{pedidoAbierto}</DialogTitle>
        </DialogHeader>
        {pedidoAbierto !== null && (
          <PedidoDetalleContenido
            key={pedidoAbierto}
            pedidoId={pedidoAbierto}
            onCambio={recargar}
            onAbrirPedidoOrigen={setPedidoAbierto}
          />
        )}
      </DialogContent>
    </Dialog>
    </>
  );
}
