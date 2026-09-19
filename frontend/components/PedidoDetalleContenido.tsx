"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaTipoVehiculo,
  type AjusteResumen,
  type EstadoPedido,
  type PedidoDetalle,
  type ReintentoCreado,
} from "@/lib/dominio/tipos";
import { ETIQUETA_TRANSICION, motivoObligatorio, transicionesDisponibles } from "@/lib/dominio/estados";
import { ContactoYNovedadesDePedido } from "@/components/ContactoYNovedadesDePedido";

interface PedidoDetalleContenidoProps {
  pedidoId: number;
  /** Se llama después de un cambio de estado exitoso — para que quien lo embeba (la lista de
   * /pedidos, por ejemplo) pueda refrescar lo que tenga cacheado. */
  onCambio?: () => void;
  /** Click en "Retorno de #X"/"Reintento de #X": si se pasa, controla la navegación (ej. el
   * dialog pasa a mostrar ese otro pedido sin cerrarse); si no, es un <Link> a /pedidos/{id}. */
  onAbrirPedidoOrigen?: (id: number) => void;
}

/**
 * Contenido de detalle de un pedido — datos, precio, cambio de estado e historial. Extraído de
 * pedidos/[id]/page.tsx para reusarlo también en el dialog de /pedidos (clic en cualquier parte
 * de la fila): la página standalone sigue existiendo para acceso directo por URL, pero ya no es
 * la única forma de ver el detalle. Sin CabeceraSesion ni el botón "← Pedidos" — eso es cosa de
 * quien lo embeba (la página o el dialog), no de este contenido.
 */
export function PedidoDetalleContenido({ pedidoId, onCambio, onAbrirPedidoOrigen }: PedidoDetalleContenidoProps) {
  const { fetchConSesion, usuario } = useAuth();

  const [pedido, setPedido] = useState<PedidoDetalle | null>(null);
  const [transicionElegida, setTransicionElegida] = useState<EstadoPedido | null>(null);
  const [motivo, setMotivo] = useState("");
  const [nuevaFecha, setNuevaFecha] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [precioManualInput, setPrecioManualInput] = useState("");
  const [guardandoPrecioManual, setGuardandoPrecioManual] = useState(false);
  const [errorPrecioManual, setErrorPrecioManual] = useState<string | null>(null);

  // E1/D13: la 4ta reprogramación no reprograma — devuelve 200 con el pedido nuevo en vez del
  // 204 habitual. Se guarda aparte para mostrarlo como resultado, no como un error.
  const [reintentoCreado, setReintentoCreado] = useState<ReintentoCreado | null>(null);

  // E1/B16: ajustes de bultos al retiro físico.
  const [ajustes, setAjustes] = useState<AjusteResumen[] | null>(null);
  const [bultosReales, setBultosReales] = useState("");
  const [ajusteDescripcion, setAjusteDescripcion] = useState("");
  const [solicitandoAjuste, setSolicitandoAjuste] = useState(false);
  const [errorAjuste, setErrorAjuste] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/pedidos/${pedidoId}`)
      .then((r) => leerJson<PedidoDetalle>(r))
      .then(setPedido)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el pedido."));
  }, [fetchConSesion, pedidoId]);

  const cargarAjustes = useCallback(() => {
    fetchConSesion(`/api/pedidos/${pedidoId}/ajustes`)
      .then((r) => leerJson<AjusteResumen[]>(r))
      .then(setAjustes)
      .catch(() => setAjustes([]));
  }, [fetchConSesion, pedidoId]);

  useEffect(cargar, [cargar]);
  useEffect(cargarAjustes, [cargarAjustes]);

  async function solicitarAjuste() {
    if (!bultosReales || Number(bultosReales) < 0) {
      setErrorAjuste("Ingresá la cantidad real de bultos.");
      return;
    }
    setSolicitandoAjuste(true);
    setErrorAjuste(null);
    try {
      const resp = await fetchConSesion(`/api/pedidos/${pedidoId}/ajustes`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ bultosReales: Number(bultosReales), descripcion: ajusteDescripcion }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setBultosReales("");
      setAjusteDescripcion("");
      cargarAjustes();
    } catch (err) {
      setErrorAjuste(err instanceof Error ? err.message : "No se pudo solicitar el ajuste.");
    } finally {
      setSolicitandoAjuste(false);
    }
  }

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
      const resp = await fetchConSesion(`/api/pedidos/${pedidoId}/estado`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          estadoNuevo: transicionElegida,
          motivo: motivo || null,
          nuevaFechaEntrega: transicionElegida === "Reprogramado" ? nuevaFecha : null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      // E1/D13: solo la rama de la 4ta reprogramación devuelve 200 + cuerpo; el resto de las
      // transiciones sigue siendo 204 sin cuerpo — leer el body ahí tiraría al intentar parsear.
      setReintentoCreado(resp.status === 200 ? await leerJson<ReintentoCreado>(resp) : null);
      setTransicionElegida(null);
      cargar();
      onCambio?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar el estado.");
    } finally {
      setEnviando(false);
    }
  }

  async function fijarPrecioManual() {
    const precio = Number(precioManualInput);
    if (!precioManualInput || precio <= 0) {
      setErrorPrecioManual("Ingresá un precio mayor a cero.");
      return;
    }
    setGuardandoPrecioManual(true);
    setErrorPrecioManual(null);
    try {
      const resp = await fetchConSesion(`/api/pedidos/${pedidoId}/precio-manual`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ precio }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setPrecioManualInput("");
      cargar();
      onCambio?.();
    } catch (err) {
      setErrorPrecioManual(err instanceof Error ? err.message : "No se pudo fijar el precio manual.");
    } finally {
      setGuardandoPrecioManual(false);
    }
  }

  if (!pedido) {
    return <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>{errorCarga ?? "Cargando…"}</p>;
  }

  const transiciones = transicionesDisponibles(pedido.estado);

  return (
    <div className="flex flex-col gap-6">
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
                onAbrirPedidoOrigen ? (
                  <button
                    type="button"
                    className="hover:underline"
                    onClick={() => onAbrirPedidoOrigen(pedido.pedidoOrigenId!)}
                  >
                    #{pedido.pedidoOrigenId}
                  </button>
                ) : (
                  <a href={`/pedidos/${pedido.pedidoOrigenId}`} className="hover:underline">
                    #{pedido.pedidoOrigenId}
                  </a>
                )
              }
            />
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Precio {pedido.total !== null ? "congelado" : ""}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          {pedido.total === null ? (
            <>
              <p className="text-muted-foreground">
                Pendiente: el precio depende del tipo de vehículo ({etiquetaTipoVehiculo("camioneta")} o{" "}
                {etiquetaTipoVehiculo("moto")}) que termine llevando el pedido — se fija recién
                cuando la ruta que lo lleva cierra su planificación.
                {pedido.peajes > 0 && ` Peajes ya cargados: $${pedido.peajes.toLocaleString("es-AR")}.`}
              </p>

              {pedido.precioManual && (
                <p className="text-sm">
                  Precio manual fijado: <span className="font-medium">${pedido.precioManual.precio.toLocaleString("es-AR")}</span>
                  {" "}
                  <span className="text-xs text-muted-foreground">
                    por {pedido.precioManual.fijadoPor ?? "—"} el{" "}
                    {new Date(pedido.precioManual.fijadoEn).toLocaleString("es-AR")}
                  </span>
                </p>
              )}

              {pedido.requiereCotizacion && usuario?.rol === "administracion" && (
                <div className="flex flex-col gap-2 border-t pt-3 mt-1">
                  <p className="text-sm font-medium text-amber-600">
                    Esta zona no tiene tarifa cargada (Anexo I, B9) — fijá un precio manual para
                    poder cerrar la planificación de la ruta que lo lleve.
                  </p>
                  <div className="flex items-end gap-2">
                    <div className="flex flex-col gap-1 flex-1">
                      <Label htmlFor="precio-manual">Precio manual</Label>
                      <Input
                        id="precio-manual"
                        type="number"
                        min="0.01"
                        step="0.01"
                        value={precioManualInput}
                        onChange={(e) => setPrecioManualInput(e.target.value)}
                      />
                    </div>
                    <Button onClick={fijarPrecioManual} disabled={guardandoPrecioManual}>
                      {guardandoPrecioManual ? "Guardando…" : "Fijar precio"}
                    </Button>
                  </div>
                  {errorPrecioManual && <p className="text-sm text-destructive">{errorPrecioManual}</p>}
                </div>
              )}
            </>
          ) : (
            <>
              <Fila etiqueta="Base" valor={`$${(pedido.precioBase ?? 0).toLocaleString("es-AR")}`} />
              {pedido.kmCobrados !== null && (
                <Fila
                  etiqueta="Km cobrados"
                  valor={`${pedido.kmCobrados.toLocaleString("es-AR")} km${pedido.kmFuente ? ` (${pedido.kmFuente})` : ""}`}
                />
              )}
              {(pedido.recargoKm ?? 0) > 0 && (
                <Fila etiqueta="Recargo km" valor={`+$${pedido.recargoKm!.toLocaleString("es-AR")}`} />
              )}
              {(pedido.recargoUrgencia ?? 0) > 0 && (
                <Fila etiqueta="Recargo urgencia" valor={`+$${pedido.recargoUrgencia!.toLocaleString("es-AR")}`} />
              )}
              {(pedido.descuentoRuta ?? 0) > 0 && (
                <Fila etiqueta="Descuento ruta" valor={`-$${pedido.descuentoRuta!.toLocaleString("es-AR")}`} />
              )}
              {pedido.peajes > 0 && <Fila etiqueta="Peajes" valor={`$${pedido.peajes.toLocaleString("es-AR")}`} />}
              <div className="flex justify-between border-t pt-2 font-semibold">
                <span>Total</span>
                <span>${pedido.total.toLocaleString("es-AR")}</span>
              </div>
              <p className="text-xs text-muted-foreground">
                Congelado el {new Date(pedido.precioCongeladoEn!).toLocaleString("es-AR")}. No se recalcula (P1).
              </p>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Facturación y ajustes</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <Fila
            etiqueta="Reprogramaciones"
            valor={
              <span className={pedido.vecesReprogramado >= 3 ? "text-destructive font-medium" : undefined}>
                {pedido.vecesReprogramado} de 3
                {pedido.vecesReprogramado >= 3 && " — la próxima genera un reintento con cargo"}
              </span>
            }
          />
          <Fila
            etiqueta="Facturación"
            valor={
              pedido.facturado
                ? "Ya generó un cargo"
                : pedido.total !== null
                  ? "Pendiente de facturar"
                  : "Todavía no es facturable"
            }
          />

          <div className="flex flex-col gap-2 border-t pt-3">
            <Label>Solicitar ajuste</Label>
            <p className="text-xs text-muted-foreground">
              Al retirar, si los bultos reales no coinciden con los declarados ({pedido.bultos}),
              marcalo acá. Un admin define el nuevo monto antes de que impacte en lo que se cobra
              (B16, tope de 3 sin cargo extra por pedido).
            </p>
            <div className="flex items-end gap-2">
              <div className="flex flex-col gap-1">
                <Label htmlFor="ajuste-bultos">Bultos reales</Label>
                <Input
                  id="ajuste-bultos"
                  type="number"
                  min="0"
                  className="w-24"
                  value={bultosReales}
                  onChange={(e) => setBultosReales(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-1 flex-1">
                <Label htmlFor="ajuste-descripcion">Descripción</Label>
                <Input
                  id="ajuste-descripcion"
                  value={ajusteDescripcion}
                  onChange={(e) => setAjusteDescripcion(e.target.value)}
                />
              </div>
              <Button onClick={solicitarAjuste} disabled={solicitandoAjuste || !bultosReales}>
                {solicitandoAjuste ? "Enviando…" : "Solicitar"}
              </Button>
            </div>
            {errorAjuste && <p className="text-sm text-destructive">{errorAjuste}</p>}
          </div>

          {ajustes && ajustes.length > 0 && (
            <div className="flex flex-col gap-2 border-t pt-3">
              <Label>Ajustes</Label>
              <ul className="flex flex-col gap-2">
                {ajustes.map((a) => (
                  <li key={a.id} className="text-sm border-b pb-2 flex flex-col gap-1">
                    <div className="flex justify-between gap-4">
                      <span>{a.descripcion}</span>
                      <span className="shrink-0 font-medium">
                        {a.monto !== null ? `$${a.monto.toLocaleString("es-AR")}` : "—"}
                      </span>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {a.estado === "pendiente" ? "Pendiente" : a.estado === "aprobado" ? "Aprobado" : "Rechazado"}
                      {" · "}
                      {new Date(a.creadoEn).toLocaleString("es-AR")} · {a.creadoPorNombre ?? "—"}
                    </div>
                    {a.estado === "pendiente" && usuario?.rol === "administracion" && (
                      <AjusteAccionesPendiente
                        pedidoId={pedidoId}
                        ajuste={a}
                        exigeCargoGestion={ajustes.filter((x) => x.estado === "aprobado").length >= 3}
                        fetchConSesion={fetchConSesion}
                        onResuelto={() => {
                          cargarAjustes();
                          cargar();
                          onCambio?.();
                        }}
                      />
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </CardContent>
      </Card>

      {transiciones.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Cambiar estado</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {reintentoCreado && (
              <p className="text-sm rounded-md border border-amber-600/40 bg-amber-600/10 p-3">
                Agotadas las 3 reprogramaciones gratis — se generó el pedido{" "}
                {onAbrirPedidoOrigen ? (
                  <button
                    type="button"
                    className="font-medium hover:underline"
                    onClick={() => onAbrirPedidoOrigen(reintentoCreado.reintentoId)}
                  >
                    #{reintentoCreado.reintentoId}
                  </button>
                ) : (
                  <a href={`/pedidos/${reintentoCreado.reintentoId}`} className="font-medium hover:underline">
                    #{reintentoCreado.reintentoId}
                  </a>
                )}{" "}
                como reintento con cargo (D13).
              </p>
            )}
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
                {transicionElegida === "Cancelado" && pedido.estado !== "Borrador" && (
                  <p className="text-sm font-medium text-destructive">
                    Este pedido ya está confirmado: cancelarlo se factura al 100%
                    {pedido.total !== null ? ` ($${pedido.total.toLocaleString("es-AR")})` : ""} (§10.2-I).
                  </p>
                )}
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

      {(usuario?.rol === "administracion" || usuario?.rol === "operacion") && (
        <ContactoYNovedadesDePedido
          pedido={pedido}
          onCambio={() => {
            cargar();
            onCambio?.();
          }}
        />
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

/** Aprobar/rechazar un ajuste pendiente (E1/B16). Componente aparte porque cada fila de la lista
 * necesita su propio estado de formulario (monto, cargo de gestión, motivo). */
function AjusteAccionesPendiente({
  pedidoId,
  ajuste,
  exigeCargoGestion,
  fetchConSesion,
  onResuelto,
}: {
  pedidoId: number;
  ajuste: AjusteResumen;
  exigeCargoGestion: boolean;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onResuelto: () => void;
}) {
  const [monto, setMonto] = useState("");
  const [cargoGestion, setCargoGestion] = useState("");
  const [motivoRechazo, setMotivoRechazo] = useState("");
  const [enviando, setEnviando] = useState<"aprobar" | "rechazar" | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function aprobar() {
    if (!monto || Number(monto) <= 0) {
      setError("Ingresá un monto mayor a cero.");
      return;
    }
    if (exigeCargoGestion && (!cargoGestion || Number(cargoGestion) <= 0)) {
      setError("A partir del 4º ajuste corresponde un cargo de gestión. No hay porcentaje definido: ingresalo a mano.");
      return;
    }
    setEnviando("aprobar");
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/pedidos/${pedidoId}/ajustes/${ajuste.id}/aprobar`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          monto: Number(monto),
          cargoGestion: cargoGestion ? Number(cargoGestion) : null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onResuelto();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo aprobar el ajuste.");
    } finally {
      setEnviando(null);
    }
  }

  async function rechazar() {
    if (!motivoRechazo.trim()) {
      setError("El motivo es obligatorio para rechazar.");
      return;
    }
    setEnviando("rechazar");
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/pedidos/${pedidoId}/ajustes/${ajuste.id}/rechazar`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ motivo: motivoRechazo }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onResuelto();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo rechazar el ajuste.");
    } finally {
      setEnviando(null);
    }
  }

  return (
    <div className="flex flex-col gap-2 rounded-md border p-2 mt-1">
      {exigeCargoGestion && (
        <p className="text-xs text-amber-600">
          4º ajuste sobre este pedido — corresponde un cargo de gestión adicional.
        </p>
      )}
      <div className="flex flex-wrap items-end gap-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor={`ajuste-${ajuste.id}-monto`} className="text-xs">
            Monto
          </Label>
          <Input
            id={`ajuste-${ajuste.id}-monto`}
            type="number"
            min="0.01"
            step="0.01"
            className="w-28"
            value={monto}
            onChange={(e) => setMonto(e.target.value)}
          />
        </div>
        {exigeCargoGestion && (
          <div className="flex flex-col gap-1">
            <Label htmlFor={`ajuste-${ajuste.id}-cargo`} className="text-xs">
              Cargo de gestión
            </Label>
            <Input
              id={`ajuste-${ajuste.id}-cargo`}
              type="number"
              min="0.01"
              step="0.01"
              className="w-28"
              value={cargoGestion}
              onChange={(e) => setCargoGestion(e.target.value)}
            />
          </div>
        )}
        <Button size="sm" disabled={enviando !== null || !monto} onClick={aprobar}>
          {enviando === "aprobar" ? "Aprobando…" : "Aprobar"}
        </Button>
      </div>
      <div className="flex items-end gap-2">
        <div className="flex flex-col gap-1 flex-1">
          <Label htmlFor={`ajuste-${ajuste.id}-motivo`} className="text-xs">
            Motivo de rechazo
          </Label>
          <Input
            id={`ajuste-${ajuste.id}-motivo`}
            className="w-56"
            value={motivoRechazo}
            onChange={(e) => setMotivoRechazo(e.target.value)}
          />
        </div>
        <Button size="sm" variant="outline" disabled={enviando !== null || !motivoRechazo} onClick={rechazar}>
          {enviando === "rechazar" ? "Rechazando…" : "Rechazar"}
        </Button>
      </div>
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
