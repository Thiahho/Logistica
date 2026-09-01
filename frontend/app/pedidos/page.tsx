"use client";

import { useCallback, useEffect, useState } from "react";
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
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PedidoDetalleContenido } from "@/components/PedidoDetalleContenido";
import { leerJson } from "@/lib/api/errores";
import { ESTADOS_PEDIDO, type ClienteSeleccion, type ListaPaginada, type PedidoResumen } from "@/lib/dominio/tipos";

const TAMANIOS_PAGINA = [10, 15, 20] as const;

type Orden = "fecha" | "-fecha" | "id" | "-id" | "total" | "-total" | "estado" | "-estado";

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
  const [pedidos, setPedidos] = useState<PedidoResumen[] | null>(null);
  const [totalRegistros, setTotalRegistros] = useState(0);
  const [error, setError] = useState<string | null>(null);

  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [q, setQ] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [estado, setEstado] = useState("");
  const [clienteId, setClienteId] = useState<string | null>(null);
  const [orden, setOrden] = useState<Orden>("-fecha");
  const [pagina, setPagina] = useState(1);
  const [tamanioPagina, setTamanioPagina] = useState<number>(15);
  const [pedidoAbierto, setPedidoAbierto] = useState<number | null>(null);

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch(() => setClientes([]));
  }, [fetchConSesion]);

  const cargar = useCallback(() => {
    const params = new URLSearchParams();
    if (q.trim()) params.set("q", q.trim());
    if (fechaDesde) params.set("fechaDesde", fechaDesde);
    if (fechaHasta) params.set("fechaHasta", fechaHasta);
    if (estado) params.set("estado", estado);
    if (clienteId) params.set("clienteId", clienteId);
    params.set("orden", orden);
    params.set("pagina", String(pagina));
    params.set("tamanioPagina", String(tamanioPagina));
    fetchConSesion(`/api/pedidos?${params.toString()}`)
      .then((r) => leerJson<ListaPaginada<PedidoResumen>>(r))
      .then((r) => {
        setPedidos(r.items);
        setTotalRegistros(r.total);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los pedidos."));
  }, [fetchConSesion, q, fechaDesde, fechaHasta, estado, clienteId, orden, pagina, tamanioPagina]);

  useEffect(() => {
    cargar();
  }, [cargar]);

  // Cualquier cambio de filtro/orden/tamaño vuelve a la página 1 — quedarse en una página que ya
  // no existe (ej. filtrar y tener menos resultados que antes) mostraría una lista vacía confusa.
  function conReinicioDePagina<T>(setter: (v: T) => void) {
    return (v: T) => {
      setter(v);
      setPagina(1);
    };
  }

  const totalPaginas = Math.max(1, Math.ceil(totalRegistros / tamanioPagina));

  function alternarOrden(campo: Columna["campo"]) {
    const asc = campo as Orden;
    const desc = `-${campo}` as Orden;
    setOrden((actual) => (actual === desc ? asc : desc));
    setPagina(1);
  }

  function indicadorOrden(campo: Columna["campo"]) {
    if (orden === campo) return "▲";
    if (orden === `-${campo}`) return "▼";
    return null;
  }

  const hayFiltros = q || fechaDesde || fechaHasta || estado || clienteId;

  return (
    <>
    <div className="p-8">
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
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          <div className="flex items-center justify-between gap-4 mt-4">
            <p className="text-sm text-muted-foreground">
              {totalRegistros === 0
                ? "Sin resultados"
                : `Mostrando ${(pagina - 1) * tamanioPagina + 1}–${Math.min(pagina * tamanioPagina, totalRegistros)} de ${totalRegistros}`}
            </p>
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <Label htmlFor="tamanio-pagina" className="text-sm text-muted-foreground">
                  Por página
                </Label>
                <Select
                  items={TAMANIOS_PAGINA.map((n) => ({ value: String(n), label: String(n) }))}
                  value={String(tamanioPagina)}
                  onValueChange={(v) => {
                    setTamanioPagina(Number(v));
                    setPagina(1);
                  }}
                >
                  <SelectTrigger id="tamanio-pagina" className="w-20">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {TAMANIOS_PAGINA.map((n) => (
                      <SelectItem key={n} value={String(n)}>
                        {n}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={pagina <= 1}
                  onClick={() => setPagina((p) => Math.max(1, p - 1))}
                >
                  ← Anterior
                </Button>
                <span className="text-sm text-muted-foreground">
                  Página {pagina} de {totalPaginas}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={pagina >= totalPaginas}
                  onClick={() => setPagina((p) => Math.min(totalPaginas, p + 1))}
                >
                  Siguiente →
                </Button>
              </div>
            </div>
          </div>
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
            onCambio={cargar}
            onAbrirPedidoOrigen={setPedidoAbierto}
          />
        )}
      </DialogContent>
    </Dialog>
    </>
  );
}
