"use client";

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
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { ESTADOS_RUTA, etiquetaEstadoRuta, type RutaResumen } from "@/lib/dominio/tipos";
import { useState } from "react";

interface Columna {
  campo: "id" | "fecha" | "estado" | "paradas";
  etiqueta: string;
}

const COLUMNAS: Columna[] = [
  { campo: "id", etiqueta: "ID" },
  { campo: "fecha", etiqueta: "Fecha" },
  { campo: "paradas", etiqueta: "Paradas" },
  { campo: "estado", etiqueta: "Estado" },
];

export default function RutasPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ListaRutas />
    </RequireRole>
  );
}

function ListaRutas() {
  const { usuario } = useAuth();
  const [q, setQ] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [estado, setEstado] = useState("");

  const {
    items: rutas, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina,
    totalPaginas, alternarOrden, indicadorOrden, conReinicioDePagina,
  } = useListadoPaginado<RutaResumen>({
    ruta: "/api/rutas",
    filtros: { q: q.trim(), fechaDesde, fechaHasta, estado },
    ordenInicial: "-fecha",
  });

  const hayFiltros = q || fechaDesde || fechaHasta || estado;

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Rutas" />

      <div className="flex flex-wrap items-end justify-between gap-4 mb-4">
        <div className="flex flex-wrap items-end gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-q">Buscar</Label>
            <Input
              id="filtro-q"
              placeholder="ID, patente o repartidor"
              value={q}
              onChange={(e) => conReinicioDePagina(setQ)(e.target.value)}
              className="w-44"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-desde">Desde</Label>
            <Input
              id="filtro-desde"
              type="date"
              value={fechaDesde}
              onChange={(e) => conReinicioDePagina(setFechaDesde)(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-hasta">Hasta</Label>
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
              items={[{ value: "todos", label: "Todos" }, ...ESTADOS_RUTA.map((e) => ({ value: e, label: etiquetaEstadoRuta(e) }))]}
              value={estado || "todos"}
              onValueChange={(v) => conReinicioDePagina(setEstado)(!v || v === "todos" ? "" : v)}
            >
              <SelectTrigger className="w-40">
                <SelectValue placeholder="Todos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="todos">Todos</SelectItem>
                {ESTADOS_RUTA.map((e) => (
                  <SelectItem key={e} value={e}>
                    {etiquetaEstadoRuta(e)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {hayFiltros && (
            <Button
              variant="outline"
              onClick={() => {
                setQ("");
                setFechaDesde("");
                setFechaHasta("");
                setEstado("");
                setPagina(1);
              }}
            >
              Limpiar filtros
            </Button>
          )}
        </div>
        <Button render={<Link href="/rutas/nueva" />} nativeButton={false}>
          Nueva ruta
        </Button>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !rutas ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : rutas.length === 0 ? (
        <p className="text-muted-foreground">No hay rutas que coincidan con estos filtros.</p>
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
                <TableHead>Vehículo</TableHead>
                <TableHead>Repartidor</TableHead>
                <TableHead></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rutas.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>{r.id}</TableCell>
                  <TableCell>{r.fecha}</TableCell>
                  <TableCell>{r.cantidadParadas}</TableCell>
                  <TableCell>{etiquetaEstadoRuta(r.estado)}</TableCell>
                  <TableCell>{r.vehiculoPatente ?? "—"}</TableCell>
                  <TableCell>{r.repartidorNombre ?? "—"}</TableCell>
                  <TableCell className="flex gap-2">
                    <Button
                      size="sm"
                      variant="outline"
                      render={<Link href={`/rutas/${r.id}`} />}
                      nativeButton={false}
                    >
                      Ver
                    </Button>
                    {r.estado === "planificada" && (
                      <Button
                        size="sm"
                        variant="outline"
                        render={<Link href={`/rutas/${r.id}/armar`} />}
                        nativeButton={false}
                      >
                        Armar
                      </Button>
                    )}
                    {r.estado !== "planificada" && usuario?.rol === "administracion" && (
                      <Button
                        size="sm"
                        variant="outline"
                        render={<Link href={`/rutas/${r.id}/cierre`} />}
                        nativeButton={false}
                      >
                        {r.estado === "cerrada" ? "Ver cierre" : "Cerrar"}
                      </Button>
                    )}
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
  );
}
