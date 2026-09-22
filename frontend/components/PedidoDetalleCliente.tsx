"use client";

import { useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { EstadoPedidoBadge } from "@/components/EstadoBadge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { PedidoDetalle } from "@/lib/dominio/tipos";

/**
 * Detalle de un envío para el rol 'cliente' — de solo lectura a propósito. No reusa
 * PedidoDetalleContenido.tsx: ese componente tiene la card "Cambiar estado" y las acciones de
 * ajuste sin chequeo de rol (se muestran a cualquiera que lo monte), así que no es seguro exponerlo
 * tal cual al portal. GET /api/pedidos/{id} ya viene con los nombres de actor interno cambiados a
 * "Empresa" para un caller 'cliente' (PedidosController.Detalle) — este componente solo pinta lo
 * que llega, no filtra nada él mismo.
 */
export function PedidoDetalleCliente({ pedidoId }: { pedidoId: number }) {
  const { fetchConSesion } = useAuth();
  const [pedido, setPedido] = useState<PedidoDetalle | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion(`/api/pedidos/${pedidoId}`)
      .then(async (r) => {
        if (!r.ok) throw new Error((await leerError(r)).mensaje);
        return leerJson<PedidoDetalle>(r);
      })
      .then(setPedido)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el envío."));
  }, [fetchConSesion, pedidoId]);

  if (error) return <p className="text-sm text-destructive">{error}</p>;
  if (!pedido) return <p className="text-muted-foreground">Cargando…</p>;

  // B5 (portal): el precio vinculante vive en precioManual.precio — pedidos.total sigue null
  // hasta CerrarPlanificacion (changelog 3.11), que un pedido de portal en Borrador nunca corrió
  // todavía. Un pedido interno confirmado, en cambio, ya tiene `total` y no `precioManual`.
  const precioFinal = pedido.precioManual?.precio ?? pedido.total;

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader className="flex items-center justify-between">
          <CardTitle className="text-base">{pedido.destinatarioNombre}</CardTitle>
          <EstadoPedidoBadge estado={pedido.estado} size="md" />
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <p>
            <span className="text-muted-foreground">Dirección: </span>
            {pedido.destinoCalleNumero}
            {pedido.destinoLocalidad ? `, ${pedido.destinoLocalidad}` : ""}
          </p>
          <p>
            <span className="text-muted-foreground">Teléfono: </span>
            {pedido.destinatarioTelefono}
          </p>
          <p>
            <span className="text-muted-foreground">Bultos: </span>
            {pedido.bultos}
          </p>
          <p>
            <span className="text-muted-foreground">Fecha de entrega: </span>
            {pedido.fechaEntrega}
            {pedido.urgente && <span className="ml-1 text-amber-600">(urgente)</span>}
          </p>
          {pedido.observaciones && (
            <p>
              <span className="text-muted-foreground">Observaciones: </span>
              {pedido.observaciones}
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Precio</CardTitle>
        </CardHeader>
        <CardContent>
          {pedido.requiereCotizacion ? (
            <p className="text-amber-600">Pendiente de cotización — te contactamos para confirmarlo.</p>
          ) : precioFinal !== null ? (
            <p className="text-lg font-semibold">${precioFinal.toLocaleString("es-AR")}</p>
          ) : (
            <p className="text-muted-foreground">Todavía sin definir.</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Historial</CardTitle>
        </CardHeader>
        <CardContent>
          {pedido.historial.length === 0 ? (
            <p className="text-sm text-muted-foreground">Sin movimientos todavía.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {pedido.historial.map((e) => (
                <li key={e.id} className="text-sm border-b pb-2 flex justify-between gap-4">
                  <span>{e.estadoNuevo}</span>
                  <span className="text-muted-foreground shrink-0">
                    {new Date(e.ocurridoEn).toLocaleString("es-AR")}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
