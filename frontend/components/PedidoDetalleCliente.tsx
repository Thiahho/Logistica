"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { EstadoPedidoBadge } from "@/components/EstadoBadge";
import { LineaTiempoEstados } from "@/components/LineaTiempoEstados";
import { UbicacionEnvio } from "@/components/UbicacionEnvio";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerError, leerJson } from "@/lib/api/errores";
import type { PedidoDetalle } from "@/lib/dominio/tipos";
import { esClienteDueno, vePrecios } from "@/lib/auth/types";

/**
 * Detalle de un envío para el rol 'cliente' — de solo lectura salvo las acciones del dueño en Borrador (AccionesBorrador). No reusa
 * PedidoDetalleContenido.tsx: ese componente tiene la card "Cambiar estado" y las acciones de
 * ajuste sin chequeo de rol (se muestran a cualquiera que lo monte), así que no es seguro exponerlo
 * tal cual al portal. GET /api/pedidos/{id} ya viene con los nombres de actor interno cambiados a
 * "Empresa" para un caller 'cliente' (PedidosController.Detalle) — este componente solo pinta lo
 * que llega, no filtra nada él mismo.
 */
export function PedidoDetalleCliente({ pedidoId }: { pedidoId: number }) {
  const { fetchConSesion, usuario } = useAuth();
  // El precio lo ve solo quien tiene verPrecios (acta 4.30; el backend ya lo manda vacío); el dueño ve quién cargó.
  const esDueno = esClienteDueno(usuario);
  const [pedido, setPedido] = useState<PedidoDetalle | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/pedidos/${pedidoId}`)
      .then(async (r) => {
        if (!r.ok) throw new Error((await leerError(r)).mensaje);
        return leerJson<PedidoDetalle>(r);
      })
      .then(setPedido)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el envío."));
  }, [fetchConSesion, pedidoId]);

  useEffect(cargar, [cargar]);

  if (error) return <p className="text-sm text-destructive">{error}</p>;
  if (!pedido) return <p className="text-muted-foreground">Cargando…</p>;

  // B5 (portal): el precio vinculante vive en precioManual.precio — pedidos.total sigue null
  // hasta CerrarPlanificacion (changelog 3.11), que un pedido de portal en Borrador nunca corrió
  // todavía. Un pedido interno confirmado, en cambio, ya tiene `total` y no `precioManual`.
  const precioFinal = pedido.precioManual?.precio ?? pedido.total;

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader className="flex items-center justify-between gap-3">
          <CardTitle className="text-base">{pedido.destinatarioNombre}</CardTitle>
          <EstadoPedidoBadge estado={pedido.estado} size="md" />
        </CardHeader>
        <CardContent>
          <LineaTiempoEstados
            eventos={pedido.historial.map((e) => ({ estado: e.estadoNuevo, ocurridoEn: e.ocurridoEn, motivo: e.motivo }))}
            estadoActual={pedido.estado}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Ubicación</CardTitle>
        </CardHeader>
        <CardContent>
          <UbicacionEnvio
            calleNumero={pedido.destinoCalleNumero}
            localidad={pedido.destinoLocalidad}
            lat={pedido.destinoLat}
            lng={pedido.destinoLng}
            dudosa={pedido.direccionDudosa}
            entregado={pedido.estado === "Entregado"}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos del envío</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <p>
            <span className="text-muted-foreground">Teléfono: </span>
            <a href={`tel:${pedido.destinatarioTelefono}`} className="underline-offset-2 hover:underline">
              {pedido.destinatarioTelefono}
            </a>
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
          {esDueno && (
            <p>
              <span className="text-muted-foreground">Cargado por: </span>
              {pedido.cargadoPorNombre ?? "Empresa"}
            </p>
          )}
        </CardContent>
      </Card>

      {vePrecios(usuario) && (
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
      )}

      {esDueno && pedido.estado === "Borrador" && <AccionesBorrador pedido={pedido} onCambio={cargar} />}
    </div>
  );
}

/** Solo el dueño y solo en Borrador: corregir el contacto o cancelar sin costo. Una vez confirmado,
 * los cambios los hace la Empresa. Si el envío ya entró en el armado de una ruta, el backend
 * rechaza la cancelación con el mensaje para contactar a la Empresa. */
function AccionesBorrador({ pedido, onCambio }: { pedido: PedidoDetalle; onCambio: () => void }) {
  const { fetchConSesion } = useAuth();
  const [modo, setModo] = useState<"nada" | "editar" | "cancelar">("nada");
  const [nombre, setNombre] = useState(pedido.destinatarioNombre);
  const [telefono, setTelefono] = useState(pedido.destinatarioTelefono);
  const [observaciones, setObservaciones] = useState(pedido.observaciones ?? "");
  const [motivo, setMotivo] = useState("");
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function enviar(ruta: string, init: RequestInit) {
    setError(null);
    setGuardando(true);
    try {
      const resp = await fetchConSesion(ruta, { ...init, headers: { "Content-Type": "application/json" } });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setModo("nada");
      onCambio();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Cambios antes de la salida</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {modo === "nada" && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => setModo("editar")}>
              Editar contacto
            </Button>
            <Button variant="outline" className="text-destructive" onClick={() => setModo("cancelar")}>
              Cancelar envío
            </Button>
          </div>
        )}

        {modo === "editar" && (
          <form
            className="flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              void enviar(`/api/mi-cuenta/pedidos/${pedido.id}`, {
                method: "PUT",
                body: JSON.stringify({ destinatarioNombre: nombre, destinatarioTelefono: telefono, observaciones }),
              });
            }}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="editar-nombre">Destinatario</Label>
              <Input id="editar-nombre" required value={nombre} onChange={(e) => setNombre(e.target.value)} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="editar-telefono">Teléfono</Label>
              <Input id="editar-telefono" required value={telefono} onChange={(e) => setTelefono(e.target.value)} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="editar-obs">Observaciones</Label>
              <Input id="editar-obs" value={observaciones} onChange={(e) => setObservaciones(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <Button type="submit" disabled={guardando}>
                {guardando ? "Guardando…" : "Guardar"}
              </Button>
              <Button type="button" variant="ghost" onClick={() => setModo("nada")}>
                Volver
              </Button>
            </div>
          </form>
        )}

        {modo === "cancelar" && (
          <div className="flex flex-col gap-3">
            <p className="text-sm">
              ¿Cancelar el envío a <span className="font-medium">{pedido.destinatarioNombre}</span>? Como todavía no salió,
              no tiene costo. No se puede deshacer.
            </p>
            <div className="flex flex-col gap-2">
              <Label htmlFor="cancelar-motivo">Motivo (opcional)</Label>
              <Input id="cancelar-motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <Button
                variant="destructive"
                disabled={guardando}
                onClick={() =>
                  void enviar(`/api/mi-cuenta/pedidos/${pedido.id}/cancelar`, {
                    method: "POST",
                    body: JSON.stringify({ motivo: motivo.trim() || null }),
                  })
                }
              >
                {guardando ? "Cancelando…" : "Sí, cancelar envío"}
              </Button>
              <Button variant="ghost" onClick={() => setModo("nada")}>
                No, volver
              </Button>
            </div>
          </div>
        )}

        {error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  );
}
