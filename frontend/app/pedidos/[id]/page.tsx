"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { EstadoPedido, PedidoDetalle } from "@/lib/dominio/tipos";
import {
  ETIQUETA_TRANSICION,
  motivoObligatorio,
  transicionesDisponibles,
} from "@/lib/dominio/estados";

export default function PedidoDetallePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <DetallePedido />
    </RequireRole>
  );
}

function DetallePedido() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();

  const [pedido, setPedido] = useState<PedidoDetalle | null>(null);
  const [transicionElegida, setTransicionElegida] = useState<EstadoPedido | null>(null);
  const [motivo, setMotivo] = useState("");
  const [nuevaFecha, setNuevaFecha] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/pedidos/${id}`)
      .then((r) => leerJson<PedidoDetalle>(r))
      .then(setPedido)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el pedido."));
  }, [fetchConSesion, id]);

  useEffect(cargar, [cargar]);

  function elegirTransicion(estado: EstadoPedido) {
    setTransicionElegida(estado);
    setMotivo("");
    setNuevaFecha("");
    setError(null);
  }

  async function confirmarTransicion() {
    if (!transicionElegida) return;
    if (motivoObligatorio(transicionElegida) && !motivo.trim()) {
      setError("El motivo es obligatorio para esta transición.");
      return;
    }
    if (transicionElegida === "Reprogramado" && !nuevaFecha) {
      setError("Falta la nueva fecha de entrega.");
      return;
    }

    setEnviando(true);
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/pedidos/${id}/estado`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          estadoNuevo: transicionElegida,
          motivo: motivo || null,
          nuevaFechaEntrega: transicionElegida === "Reprogramado" ? nuevaFecha : null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setTransicionElegida(null);
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar el estado.");
    } finally {
      setEnviando(false);
    }
  }

  if (!pedido) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Pedido" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  const transiciones = transicionesDisponibles(pedido.estado);

  return (
    <div className="p-8 max-w-2xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Pedido #${pedido.id}`} />
      <Button variant="outline" render={<Link href="/pedidos" />} nativeButton={false} className="self-start">
        ← Pedidos
      </Button>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <Fila etiqueta="Cliente" valor={pedido.clienteRazonSocial} />
          <Fila etiqueta="Estado" valor={<span className="font-medium">{pedido.estado}</span>} />
          <Fila etiqueta="Destinatario" valor={`${pedido.destinatarioNombre} · ${pedido.destinatarioTelefono}`} />
          <Fila
            etiqueta="Dirección"
            valor={
              <span className={pedido.direccionDudosa ? "text-destructive" : undefined}>
                {pedido.destinoCalleNumero}
                {pedido.destinoLocalidad ? `, ${pedido.destinoLocalidad}` : ""}
                {pedido.direccionDudosa && " (dudosa)"}
              </span>
            }
          />
          <Fila etiqueta="Bultos" valor={String(pedido.bultos)} />
          <Fila etiqueta="Entrega" valor={`${pedido.fechaEntrega}${pedido.urgente ? " · urgente" : ""}`} />
          {pedido.referenciaCliente && <Fila etiqueta="Referencia" valor={pedido.referenciaCliente} />}
          {pedido.observaciones && <Fila etiqueta="Observaciones" valor={pedido.observaciones} />}
          {pedido.pedidoOrigenId && (
            <Fila
              etiqueta={pedido.tipo === "retorno" ? "Retorno de" : "Reintento de"}
              valor={
                <Link href={`/pedidos/${pedido.pedidoOrigenId}`} className="hover:underline">
                  #{pedido.pedidoOrigenId}
                </Link>
              }
            />
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Precio congelado</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <Fila etiqueta="Base" valor={`$${pedido.precioBase.toLocaleString("es-AR")}`} />
          {pedido.recargoUrgencia > 0 && (
            <Fila etiqueta="Recargo urgencia" valor={`+$${pedido.recargoUrgencia.toLocaleString("es-AR")}`} />
          )}
          {pedido.descuentoRuta > 0 && (
            <Fila etiqueta="Descuento ruta" valor={`-$${pedido.descuentoRuta.toLocaleString("es-AR")}`} />
          )}
          {pedido.peajes > 0 && <Fila etiqueta="Peajes" valor={`$${pedido.peajes.toLocaleString("es-AR")}`} />}
          <div className="flex justify-between border-t pt-2 font-semibold">
            <span>Total</span>
            <span>${pedido.total.toLocaleString("es-AR")}</span>
          </div>
          <p className="text-xs text-muted-foreground">
            Congelado el {new Date(pedido.precioCongeladoEn).toLocaleString("es-AR")}. No se recalcula (P1).
          </p>
        </CardContent>
      </Card>

      {transiciones.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Cambiar estado</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-wrap gap-2">
              {transiciones.map((t) => (
                <Button
                  key={t}
                  variant={transicionElegida === t ? "default" : "outline"}
                  size="sm"
                  onClick={() => elegirTransicion(t)}
                >
                  {ETIQUETA_TRANSICION[t]}
                </Button>
              ))}
            </div>

            {transicionElegida && (
              <div className="flex flex-col gap-3 border-t pt-4">
                {transicionElegida === "Reprogramado" && (
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="nueva-fecha">Nueva fecha de entrega</Label>
                    <Input
                      id="nueva-fecha"
                      type="date"
                      value={nuevaFecha}
                      onChange={(e) => setNuevaFecha(e.target.value)}
                      className="w-40"
                    />
                  </div>
                )}
                <div className="flex flex-col gap-2">
                  <Label htmlFor="motivo">
                    Motivo{motivoObligatorio(transicionElegida) ? "" : " (opcional)"}
                  </Label>
                  <Input id="motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                </div>
                {error && <p className="text-sm text-destructive">{error}</p>}
                <div className="flex gap-2">
                  <Button onClick={confirmarTransicion} disabled={enviando}>
                    {enviando ? "Guardando…" : `Confirmar: ${ETIQUETA_TRANSICION[transicionElegida]}`}
                  </Button>
                  <Button variant="outline" onClick={() => setTransicionElegida(null)} disabled={enviando}>
                    Cancelar
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Historial</CardTitle>
        </CardHeader>
        <CardContent>
          {pedido.historial.length === 0 ? (
            <p className="text-sm text-muted-foreground">Sin eventos.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {pedido.historial.map((e) => (
                <li key={e.id} className="text-sm border-b pb-2">
                  <span className="font-medium">
                    {e.estadoAnterior ? `${e.estadoAnterior} → ${e.estadoNuevo}` : `Creado en ${e.estadoNuevo}`}
                  </span>
                  {e.motivo && <span className="text-muted-foreground"> · {e.motivo}</span>}
                  <div className="text-xs text-muted-foreground">
                    {new Date(e.ocurridoEn).toLocaleString("es-AR")}
                    {" · "}
                    {e.actorNombre ?? (e.actorTipo === "sistema" ? "sistema" : "—")}
                  </div>
                </li>
              ))}
            </ul>
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
