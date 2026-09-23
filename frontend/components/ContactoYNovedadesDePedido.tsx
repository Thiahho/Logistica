"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PanelNovedades } from "@/components/PanelNovedades";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ListaPaginada, NovedadResumen, PedidoDetalle } from "@/lib/dominio/tipos";

/**
 * Corrección de los datos de contacto de un pedido (RF-37) y las novedades que lo tocaron. Solo
 * back-office: el que llama decide si lo monta. Edita únicamente lo que P1 no congela — teléfono,
 * nombre del destinatario y observaciones. Destino y precio no están acá: fn_congelar_pedido los
 * protege desde que el pedido sale de Borrador, y si la dirección está mal la salida es una entrega
 * fallida, no una edición.
 *
 * Si el pedido ya va en una ruta en curso, el cambio le llega al repartidor como aviso: sin eso él
 * seguiría llamando al teléfono viejo.
 */
export function ContactoYNovedadesDePedido({
  pedido,
  onCambio,
}: {
  pedido: PedidoDetalle;
  onCambio: () => void;
}) {
  const { fetchConSesion } = useAuth();

  const editable = pedido.estado === "Borrador" || pedido.estado === "Confirmado" || pedido.estado === "EnRuta";

  const [editando, setEditando] = useState(false);
  const [telefono, setTelefono] = useState("");
  const [nombre, setNombre] = useState("");
  const [observaciones, setObservaciones] = useState("");
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [novedades, setNovedades] = useState<NovedadResumen[]>([]);

  const cargarNovedades = useCallback(() => {
    fetchConSesion(`/api/novedades?pedidoId=${pedido.id}&estado=todas`)
      .then((r) => leerJson<ListaPaginada<NovedadResumen>>(r))
      .then((l) => setNovedades(l.items))
      .catch(() => setNovedades([])); // decorativo: sin novedades cargadas no rompe el detalle
  }, [fetchConSesion, pedido.id]);

  useEffect(cargarNovedades, [cargarNovedades]);

  function empezar() {
    setTelefono(pedido.destinatarioTelefono);
    setNombre(pedido.destinatarioNombre);
    setObservaciones(pedido.observaciones ?? "");
    setError(null);
    setEditando(true);
  }

  async function guardar() {
    setGuardando(true);
    setError(null);
    try {
      // Solo se manda lo que cambió: null = "no tocar ese campo" para el servidor.
      const cambios = {
        destinatarioTelefono: telefono.trim() !== pedido.destinatarioTelefono ? telefono : null,
        destinatarioNombre: nombre.trim() !== pedido.destinatarioNombre ? nombre : null,
        observaciones: observaciones.trim() !== (pedido.observaciones ?? "") ? observaciones : null,
      };
      const resp = await fetchConSesion(`/api/pedidos/${pedido.id}/contacto`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cambios),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setEditando(false);
      onCambio();
      cargarNovedades();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(false);
    }
  }

  const sinCambios =
    telefono.trim() === pedido.destinatarioTelefono &&
    nombre.trim() === pedido.destinatarioNombre &&
    observaciones.trim() === (pedido.observaciones ?? "");

  return (
    <>
      {editable && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Contacto</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {!editando ? (
              <>
                <p className="text-sm text-muted-foreground">
                  {pedido.destinatarioNombre} · {pedido.destinatarioTelefono}
                </p>
                <Button size="sm" variant="outline" className="self-start" onClick={empezar}>
                  Corregir datos de contacto
                </Button>
              </>
            ) : (
              <>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="contacto-nombre">Destinatario</Label>
                  <Input id="contacto-nombre" value={nombre} onChange={(e) => setNombre(e.target.value)} />
                </div>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="contacto-telefono">Teléfono</Label>
                  <Input id="contacto-telefono" value={telefono} onChange={(e) => setTelefono(e.target.value)} />
                </div>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="contacto-obs">Observaciones</Label>
                  <Input id="contacto-obs" value={observaciones} onChange={(e) => setObservaciones(e.target.value)} />
                </div>
                <p className="text-xs text-muted-foreground">
                  El destino y el precio no se editan desde acá: se congelaron al confirmar el pedido.
                </p>
                {error && <p className="text-sm text-destructive">{error}</p>}
                <div className="flex gap-2">
                  <Button size="sm" onClick={guardar} disabled={guardando || sinCambios}>
                    {guardando ? "Guardando…" : "Guardar"}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => setEditando(false)} disabled={guardando}>
                    Cancelar
                  </Button>
                </div>
              </>
            )}
          </CardContent>
        </Card>
      )}

      {novedades.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Novedades de la calle</CardTitle>
          </CardHeader>
          <CardContent>
            <PanelNovedades
              novedades={novedades}
              onCambio={() => {
                cargarNovedades();
                onCambio();
              }}
              mostrarRuta
            />
          </CardContent>
        </Card>
      )}
    </>
  );
}
