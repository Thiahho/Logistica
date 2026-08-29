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
import { ESTADOS_PEDIDO, type PedidoResumen } from "@/lib/dominio/tipos";

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
  const [fecha, setFecha] = useState("");
  const [estado, setEstado] = useState<string>("");

  const cargar = useCallback(() => {
    const params = new URLSearchParams();
    if (fecha) params.set("fecha", fecha);
    if (estado) params.set("estado", estado);
    const query = params.toString();
    fetchConSesion(`/api/pedidos${query ? `?${query}` : ""}`)
      .then((r) => r.json())
      .then(setPedidos);
  }, [fetchConSesion, fecha, estado]);

  useEffect(() => {
    cargar();
  }, [cargar]);

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Pedidos del día" />

      <div className="flex items-end justify-between gap-4 mb-4">
        <div className="flex items-end gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-fecha">Fecha de entrega</Label>
            <Input
              id="filtro-fecha"
              type="date"
              value={fecha}
              onChange={(e) => setFecha(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Estado</Label>
            <Select
              items={[{ value: "todos", label: "Todos" }, ...ESTADOS_PEDIDO.map((e) => ({ value: e, label: e }))]}
              value={estado || "todos"}
              onValueChange={(v) => setEstado(!v || v === "todos" ? "" : v)}
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
          {(fecha || estado) && (
            <Button
              variant="outline"
              onClick={() => {
                setFecha("");
                setEstado("");
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

      {!pedidos ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : pedidos.length === 0 ? (
        <p className="text-muted-foreground">No hay pedidos cargados.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ID</TableHead>
              <TableHead>Destinatario</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Total</TableHead>
              <TableHead>Entrega</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {pedidos.map((p) => (
              <TableRow key={p.id} className={p.direccionDudosa ? "bg-destructive/10" : undefined}>
                <TableCell>
                  <Link href={`/pedidos/${p.id}`} className="hover:underline">
                    {p.id}
                  </Link>
                </TableCell>
                <TableCell>
                  <Link href={`/pedidos/${p.id}`} className="hover:underline">
                    {p.destinatarioNombre}
                  </Link>
                  {p.direccionDudosa && (
                    <span className="ml-2 text-xs font-medium text-destructive">
                      dirección dudosa
                    </span>
                  )}
                </TableCell>
                <TableCell>{p.estado}</TableCell>
                <TableCell>${p.total.toLocaleString("es-AR")}</TableCell>
                <TableCell>{p.fechaEntrega}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
