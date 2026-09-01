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
import { leerJson } from "@/lib/api/errores";
import type { ListaPaginada, RutaResumen } from "@/lib/dominio/tipos";

const TAMANIOS_PAGINA = [10, 15, 20] as const;
const ESTADOS_RUTA = ["planificada", "en_curso", "cerrada"] as const;

type Orden = "fecha" | "-fecha" | "id" | "-id" | "estado" | "-estado" | "paradas" | "-paradas";

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
  const { usuario, fetchConSesion } = useAuth();
  const [rutas, setRutas] = useState<RutaResumen[] | null>(null);
  const [totalRegistros, setTotalRegistros] = useState(0);
  const [error, setError] = useState<string | null>(null);

  const [q, setQ] = useState("");
  const [fechaDesde, setFechaDesde] = useState("");
  const [fechaHasta, setFechaHasta] = useState("");
  const [estado, setEstado] = useState("");
  const [orden, setOrden] = useState<Orden>("-fecha");
  const [pagina, setPagina] = useState(1);
  const [tamanioPagina, setTamanioPagina] = useState<number>(15);

  const cargar = useCallback(() => {
    const params = new URLSearchParams();
    if (q.trim()) params.set("q", q.trim());
    if (fechaDesde) params.set("fechaDesde", fechaDesde);
    if (fechaHasta) params.set("fechaHasta", fechaHasta);
    if (estado) params.set("estado", estado);
    params.set("orden", orden);
    params.set("pagina", String(pagina));
    params.set("tamanioPagina", String(tamanioPagina));
    fetchConSesion(`/api/rutas?${params.toString()}`)
      .then((r) => leerJson<ListaPaginada<RutaResumen>>(r))
      .then((r) => {
        setRutas(r.items);
        setTotalRegistros(r.total);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar las rutas."));
  }, [fetchConSesion, q, fechaDesde, fechaHasta, estado, orden, pagina, tamanioPagina]);

  useEffect(() => {
    cargar();
  }, [cargar]);

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
              items={[{ value: "todos", label: "Todos" }, ...ESTADOS_RUTA.map((e) => ({ value: e, label: e }))]}
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
                    {e}
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
                  <TableCell>{r.estado}</TableCell>
                  <TableCell>{r.vehiculoPatente ?? "—"}</TableCell>
                  <TableCell>{r.repartidorNombre ?? "—"}</TableCell>
                  <TableCell>
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
  );
}
