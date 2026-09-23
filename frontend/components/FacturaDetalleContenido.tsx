"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { leerJson } from "@/lib/api/errores";
import { etiquetaEstadoFactura, type FacturaDetalle } from "@/lib/dominio/tipos";

interface FacturaDetalleContenidoProps {
  facturaId: number;
}

/** Detalle de una factura — cabecera + ítems. Sin acciones: una factura emitida es inmutable
 * (trg_facturas_inmutable), no hay nada que editar acá. Mismo patrón de Card + <Fila> que
 * PedidoDetalleContenido.tsx. */
export function FacturaDetalleContenido({ facturaId }: FacturaDetalleContenidoProps) {
  const { fetchConSesion } = useAuth();
  const [factura, setFactura] = useState<FacturaDetalle | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  // Los ítems llegan completos en el detalle: buscar y paginar se resuelve acá, sin otra llamada.
  const [busqueda, setBusqueda] = useState("");
  const [pagina, setPagina] = useState(1);
  const [tamanioPagina, setTamanioPagina] = useState(10);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/facturas/${facturaId}`)
      .then((r) => leerJson<FacturaDetalle>(r))
      .then(setFactura)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la factura."));
  }, [fetchConSesion, facturaId]);

  useEffect(cargar, [cargar]);

  const itemsFiltrados = useMemo(() => {
    const termino = busqueda.trim().toLowerCase();
    const items = factura?.items ?? [];
    if (!termino) return items;
    return items.filter(
      (item) =>
        item.descripcion.toLowerCase().includes(termino) ||
        (item.pedidoId !== null && String(item.pedidoId).includes(termino)),
    );
  }, [factura, busqueda]);

  if (!factura) {
    return <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>{errorCarga ?? "Cargando…"}</p>;
  }

  const totalPaginas = Math.max(1, Math.ceil(itemsFiltrados.length / tamanioPagina));
  const paginaActual = Math.min(pagina, totalPaginas);
  const itemsPagina = itemsFiltrados.slice((paginaActual - 1) * tamanioPagina, paginaActual * tamanioPagina);

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <Fila etiqueta="Cliente" valor={factura.clienteRazonSocial} />
          <Fila etiqueta="Ciclo" valor={factura.ciclo === "quincenal" ? "Quincenal" : "Mensual"} />
          <Fila etiqueta="Período" valor={`${factura.periodoDesde} al ${factura.periodoHasta}`} />
          <Fila etiqueta="Emisión" valor={factura.fechaEmision} />
          <Fila etiqueta="Vencimiento" valor={factura.fechaVencimiento} />
          <Fila etiqueta="Estado" valor={<span className="font-medium">{etiquetaEstadoFactura(factura.estado)}</span>} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Importe</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <div className="flex justify-between border-t pt-2 font-semibold">
            <span>Total</span>
            <span>${factura.total.toLocaleString("es-AR")}</span>
          </div>
          <Fila etiqueta="Pagado" valor={`$${factura.pagado.toLocaleString("es-AR")}`} />
          <Fila etiqueta="Saldo" valor={`$${factura.saldo.toLocaleString("es-AR")}`} />
          <p className="text-xs text-muted-foreground">
            Emitida el {factura.fechaEmision}. Una factura no se edita — cualquier corrección entra
            como ítem nuevo en la siguiente.
          </p>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Ítems ({factura.items.length})</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {factura.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">Sin ítems.</p>
          ) : (
            <>
              <Input
                type="search"
                aria-label="Buscar ítems"
                placeholder="Buscar por descripción o N.º de pedido"
                value={busqueda}
                onChange={(e) => {
                  setBusqueda(e.target.value);
                  setPagina(1);
                }}
              />
              {itemsFiltrados.length === 0 ? (
                <p className="text-sm text-muted-foreground">Ningún ítem coincide con la búsqueda.</p>
              ) : (
                // Scroll propio de la lista: los datos e importes de arriba no se pierden de vista.
                <ul className="flex max-h-72 flex-col gap-2 overflow-y-auto pr-1">
                  {itemsPagina.map((item) => (
                    <li key={item.id} className="text-sm border-b pb-2 flex justify-between gap-4">
                      <span>
                        {item.descripcion}
                        {item.pedidoId && <span className="text-muted-foreground"> (pedido #{item.pedidoId})</span>}
                      </span>
                      <span className="shrink-0 font-medium">
                        {item.monto !== null ? `$${item.monto.toLocaleString("es-AR")}` : "—"}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <ControlesPaginacion
                pagina={paginaActual}
                setPagina={setPagina}
                totalPaginas={totalPaginas}
                totalRegistros={itemsFiltrados.length}
                tamanioPagina={tamanioPagina}
                setTamanioPagina={setTamanioPagina}
              />
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function Fila({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <span className="text-muted-foreground shrink-0">{etiqueta}</span>
      <span className="text-right">{valor}</span>
    </div>
  );
}
