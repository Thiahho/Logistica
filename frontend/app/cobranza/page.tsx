"use client";

import { useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { PuntoColor } from "@/components/PuntoColor";
import { AvisoCobranzaDialog } from "@/components/AvisoCobranzaDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
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
import { PagosInformadosRevision } from "@/components/PagosInformadosRevision";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { etiquetaCategoriaRiesgo, type ClienteEnRiesgo } from "@/lib/dominio/tipos";

const CATEGORIAS = ["vencido", "por_vencer"] as const;

/** Panel de riesgo de cuenta corriente — NO es el tablero de indicadores operativos (Anexo I
 * B2/E3, que depende de una etapa todavía no arrancada). Esto se apoya en cuenta corriente
 * (B1, ya cerrado en E1): clientes con deuda vencida o con una factura por vencer dentro de la
 * ventana de preaviso del backend (15 días, CuentaCorrienteService.DiasPreavisoDefault). */
export default function CobranzaPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <PanelCobranza />
    </RequireRole>
  );
}

function PanelCobranza() {
  const [q, setQ] = useState("");
  const [categoria, setCategoria] = useState("");
  // Set de ids, no de filas: sobrevive a refetch/repaginado (mismo criterio que
  // rutas/[id]/armar). No se limpia al cambiar de página a propósito — una selección cruzada
  // entre páginas nunca debe ser invisible antes de mandar avisos.
  const [seleccionados, setSeleccionados] = useState<Set<number>>(new Set());
  const [idsParaAviso, setIdsParaAviso] = useState<number[] | null>(null);

  const {
    items: clientes, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina,
    totalPaginas, conReinicioDePagina, recargar,
  } = useListadoPaginado<ClienteEnRiesgo>({
    ruta: "/api/clientes/riesgo",
    filtros: { q: q.trim(), categoria },
    // Sin orden explícito: se mantiene el orden de prioridad de cobranza que ya trae el backend
    // (deuda vencida desc, próximo vencimiento asc) — no es alfabético a propósito.
    ordenInicial: "",
  });

  const seleccionables = (clientes ?? []).filter((c) => c.email || c.telefono);
  const todosEnPagina = seleccionables.length > 0 && seleccionables.every((c) => seleccionados.has(c.clienteId));
  const algunoEnPagina = seleccionables.some((c) => seleccionados.has(c.clienteId));

  function alternarFila(id: number) {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function alternarTodosEnPagina() {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (todosEnPagina) for (const c of seleccionables) next.delete(c.clienteId);
      else for (const c of seleccionables) next.add(c.clienteId);
      return next;
    });
  }

  const vencidos = (clientes ?? []).filter((c) => c.categoria === "vencido");
  const porVencer = (clientes ?? []).filter((c) => c.categoria === "por_vencer");
  const totalVencido = vencidos.reduce((acc, c) => acc + c.deudaVencida, 0);

  return (
    <>
      <div className="p-4 md:p-8">
        <CabeceraSesion titulo="Cobranza" />

        {/* Arriba de todo: un pago informado sin revisar es plata que el cliente ya dio y todavía
         * figura como deuda (y puede estar sosteniendo un corte de servicio). */}
        <div className="mb-6">
          <PagosInformadosRevision ruta="/api/pagos-informados" mostrarCliente onCambio={recargar} ocultarSiVacio />
        </div>

        {clientes && clientes.length > 0 && (
          <div className="grid grid-cols-3 gap-4 mb-6">
            <TarjetaMetrica valor={vencidos.length} etiqueta="Con deuda vencida" tono={vencidos.length > 0 ? "alerta" : "normal"} />
            <TarjetaMetrica
              valor={`$${totalVencido.toLocaleString("es-AR")}`}
              etiqueta="Total vencido"
              tono={totalVencido > 0 ? "alerta" : "normal"}
            />
            <TarjetaMetrica valor={porVencer.length} etiqueta="Vencen pronto" />
          </div>
        )}

        <div className="flex flex-wrap items-end gap-4 mb-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-q">Buscar</Label>
            <Input
              id="filtro-q"
              placeholder="Cliente"
              value={q}
              onChange={(e) => conReinicioDePagina(setQ)(e.target.value)}
              className="w-48"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Situación</Label>
            <Select
              items={[{ value: "todas", label: "Todas" }, ...CATEGORIAS.map((c) => ({ value: c, label: etiquetaCategoriaRiesgo(c) }))]}
              value={categoria || "todas"}
              onValueChange={(v) => conReinicioDePagina(setCategoria)(!v || v === "todas" ? "" : v)}
            >
              <SelectTrigger className="w-40">
                <SelectValue placeholder="Todas" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="todas">Todas</SelectItem>
                {CATEGORIAS.map((c) => (
                  <SelectItem key={c} value={c}>
                    {etiquetaCategoriaRiesgo(c)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        {error ? (
          <p className="text-sm text-destructive">{error}</p>
        ) : !clientes ? (
          <p className="text-muted-foreground">Cargando…</p>
        ) : clientes.length === 0 ? (
          <p className="text-muted-foreground">Ningún cliente con deuda vencida ni vencimientos próximos.</p>
        ) : (
          <>
            {seleccionados.size > 0 && (
              <div className="flex items-center justify-between gap-4 mb-3 rounded-lg border bg-muted/30 p-3">
                <span className="text-sm">
                  {seleccionados.size} seleccionado{seleccionados.size === 1 ? "" : "s"}
                </span>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => setSeleccionados(new Set())}>
                    Limpiar selección
                  </Button>
                  <Button size="sm" onClick={() => setIdsParaAviso(Array.from(seleccionados))}>
                    Enviar avisos
                  </Button>
                </div>
              </div>
            )}

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <Checkbox
                      checked={todosEnPagina}
                      indeterminate={algunoEnPagina && !todosEnPagina}
                      onCheckedChange={alternarTodosEnPagina}
                      disabled={seleccionables.length === 0}
                      aria-label={`Seleccionar los ${seleccionables.length} de esta página`}
                    />
                  </TableHead>
                  <TableHead>Cliente</TableHead>
                  <TableHead>Situación</TableHead>
                  <TableHead>Deuda vencida</TableHead>
                  <TableHead>Próximo vencimiento</TableHead>
                  <TableHead>Saldo</TableHead>
                  <TableHead>Semáforos</TableHead>
                  <TableHead>Contacto</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {clientes.map((c) => {
                  const sinContacto = !c.email && !c.telefono;
                  return (
                    <TableRow key={c.clienteId} className={c.categoria === "vencido" ? "bg-destructive/10" : undefined}>
                      <TableCell>
                        <Checkbox
                          checked={seleccionados.has(c.clienteId)}
                          onCheckedChange={() => alternarFila(c.clienteId)}
                          disabled={sinContacto}
                          aria-label={`Seleccionar ${c.razonSocial}`}
                        />
                      </TableCell>
                      <TableCell>
                        <Link href={`/clientes/${c.clienteId}`} className="hover:underline">
                          {c.razonSocial}
                        </Link>
                      </TableCell>
                      <TableCell>
                        <span className={c.categoria === "vencido" ? "text-destructive font-medium" : undefined}>
                          {etiquetaCategoriaRiesgo(c.categoria)}
                        </span>
                      </TableCell>
                      <TableCell>${c.deudaVencida.toLocaleString("es-AR")}</TableCell>
                      <TableCell>
                        {c.proximoVencimiento ? (
                          <>
                            {c.proximoVencimiento}
                            {c.diasHastaVencimiento !== null && (
                              <span className="ml-1 text-xs text-amber-600">({c.diasHastaVencimiento}d)</span>
                            )}
                          </>
                        ) : (
                          "—"
                        )}
                      </TableCell>
                      <TableCell>${c.saldo.toLocaleString("es-AR")}</TableCell>
                      <TableCell>
                        <div className="flex gap-1">
                          <PuntoColor color={c.colorPago} />
                          <PuntoColor color={c.colorTrato} />
                          <PuntoColor color={c.colorOper} />
                        </div>
                      </TableCell>
                      <TableCell>
                        {sinContacto ? (
                          <span className="text-xs text-amber-600">Sin email ni teléfono</span>
                        ) : (
                          <span className="text-xs text-muted-foreground">{c.email ?? c.telefono}</span>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
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

      <AvisoCobranzaDialog
        clienteIds={idsParaAviso}
        onOpenChange={(open) => !open && setIdsParaAviso(null)}
        onEnviado={() => {
          setSeleccionados(new Set());
          recargar();
        }}
      />
    </>
  );
}
