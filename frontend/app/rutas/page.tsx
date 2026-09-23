"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
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
import { OrdenMovil } from "@/components/OrdenMovil";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { useSondeo } from "@/lib/hooks/useSondeo";
import { leerError, leerJson } from "@/lib/api/errores";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  ESTADOS_RUTA,
  etiquetaEstadoRuta,
  type CandidatoRuta,
  type ListaPaginada,
  type RutaResumen,
} from "@/lib/dominio/tipos";
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

/** Mañana en fecha LOCAL (yyyy-mm-dd). No toISOString(): eso da la fecha en UTC y a la noche ya es otro día. */
function mananaISO(): string {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toLocaleDateString("sv-SE");
}

/**
 * Anticipa el día siguiente: cuántos pedidos (y bultos) ya tienen fecha de entrega ese día y todavía
 * no están en ninguna ruta — se refresca solo, así se ve cómo van entrando los pedidos de los clientes.
 * "Preparar ruta" abre la ruta planificada de esa fecha si ya existe, o la crea, y va al armado con
 * todo lo apto preseleccionado (?todos=1). La fecha es editable: sirve para saltar un fin de semana.
 */
function PrepararDiaSiguiente() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();
  const [fecha, setFecha] = useState(mananaISO);
  const [preparando, setPreparando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { datos: candidatos } = useSondeo<CandidatoRuta[]>(
    `/api/pedidos/candidatos-ruta?fecha=${fecha}`,
    { intervaloMs: 30_000, habilitado: fecha !== "" },
  );
  const { datos: existentes } = useSondeo<ListaPaginada<RutaResumen>>(
    `/api/rutas?fecha=${fecha}&estado=planificada&orden=id&tamanioPagina=1`,
    { intervaloMs: 30_000, habilitado: fecha !== "" },
  );

  const bultos = (candidatos ?? []).reduce((n, c) => n + c.bultos, 0);
  const rutaExistente = existentes?.items[0] ?? null;
  const aptos = (candidatos ?? []).filter((c) => c.direccionApta).length;

  async function preparar() {
    setError(null);
    setPreparando(true);
    try {
      if (rutaExistente) {
        // Con paradas ya armadas no se pisa nada: ?todos=1 solo actúa sobre una ruta vacía.
        router.push(`/rutas/${rutaExistente.id}/armar?todos=1`);
        return;
      }
      const resp = await fetchConSesion("/api/rutas", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fecha }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const { id } = await leerJson<{ id: number }>(resp);
      router.push(`/rutas/${id}/armar?todos=1`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo preparar la ruta.");
      setPreparando(false);
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Preparar la ruta del día siguiente</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="fecha-preparar">Fecha de entrega</Label>
            <Input
              id="fecha-preparar"
              type="date"
              value={fecha}
              onChange={(e) => setFecha(e.target.value)}
              className="w-44"
            />
          </div>
          <Button
            className="h-10"
            disabled={preparando || !fecha || !candidatos || (candidatos.length === 0 && !rutaExistente)}
            onClick={preparar}
          >
            {preparando ? "Preparando…" : rutaExistente ? `Abrir la ruta #${rutaExistente.id}` : "Preparar ruta"}
          </Button>
        </div>
        {!candidatos ? (
          <p className="text-sm text-muted-foreground">Buscando pedidos…</p>
        ) : candidatos.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Todavía no hay pedidos sin ruta para esa fecha.
            {rutaExistente && ` Ya hay una ruta planificada (#${rutaExistente.id}, ${rutaExistente.cantidadBultos} bultos).`}
          </p>
        ) : (
          <p className="text-sm">
            <span className="font-medium">
              {candidatos.length} {candidatos.length === 1 ? "pedido" : "pedidos"} sin ruta · {bultos} {bultos === 1 ? "bulto" : "bultos"}
            </span>
            {aptos < candidatos.length && (
              <span className="ml-2 text-amber-600">
                {candidatos.length - aptos} con dirección dudosa (no se pueden rutear todavía)
              </span>
            )}
            {rutaExistente && (
              <span className="ml-2 text-muted-foreground">
                · ya hay una ruta planificada (#{rutaExistente.id}, {rutaExistente.cantidadBultos} bultos)
              </span>
            )}
          </p>
        )}
        {error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  );
}

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
    orden, totalPaginas, alternarOrden, indicadorOrden, conReinicioDePagina,
  } = useListadoPaginado<RutaResumen>({
    ruta: "/api/rutas",
    filtros: { q: q.trim(), fechaDesde, fechaHasta, estado },
    ordenInicial: "-fecha",
  });

  const hayFiltros = q || fechaDesde || fechaHasta || estado;

  return (
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Rutas" />

      <PrepararDiaSiguiente />

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
                <TableHead>Bultos</TableHead>
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
                  <TableCell>{r.cantidadBultos}</TableCell>
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
