"use client";

import { useCallback, useEffect, useState } from "react";
import { Paperclip } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { leerError, leerJson } from "@/lib/api/errores";
import { etiquetaEstadoPagoInformado, etiquetaMedioPago, type PagoInformadoResumen } from "@/lib/dominio/tipos";

const pesos = (n: number) => `$${n.toLocaleString("es-AR", { maximumFractionDigits: 2 })}`;

/**
 * Pagos que los dueños de clientes informan desde el portal. Administración los confirma (se crea el
 * Pago, con monto y fecha corregibles) o los rechaza con motivo. Se usa en /cobranza (todos los
 * pendientes, `mostrarCliente`) y en la ficha de un cliente (los suyos, con el historial plegado).
 */
export function PagosInformadosRevision({
  ruta,
  mostrarCliente = false,
  onCambio,
  ocultarSiVacio = false,
}: {
  /** GET que devuelve PagoInformadoResumen[]: `/api/pagos-informados` o `/api/clientes/{id}/pagos-informados`. */
  ruta: string;
  mostrarCliente?: boolean;
  /** Después de confirmar: el saldo del cliente cambió. */
  onCambio?: () => void;
  ocultarSiVacio?: boolean;
}) {
  const { fetchConSesion } = useAuth();
  const [lista, setLista] = useState<PagoInformadoResumen[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(ruta)
      .then((r) => leerJson<PagoInformadoResumen[]>(r))
      .then(setLista)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los pagos informados."));
  }, [fetchConSesion, ruta]);

  useEffect(cargar, [cargar]);

  const pendientes = lista?.filter((p) => p.estado === "pendiente") ?? [];
  const revisados = lista?.filter((p) => p.estado !== "pendiente") ?? [];

  if (ocultarSiVacio && lista && lista.length === 0 && !error) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">
          Pagos informados por {mostrarCliente ? "clientes" : "el cliente"}
          {pendientes.length > 0 && (
            <span className="ml-2 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
              {pendientes.length} por revisar
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {error && <p className="text-sm text-destructive">{error}</p>}
        {!lista ? (
          error ? null : <p className="text-sm text-muted-foreground">Cargando…</p>
        ) : pendientes.length === 0 ? (
          <p className="text-sm text-muted-foreground">No hay pagos informados por revisar.</p>
        ) : (
          <ul className="flex flex-col divide-y">
            {pendientes.map((p) => (
              <PendienteFila
                key={p.id}
                pago={p}
                mostrarCliente={mostrarCliente}
                onRevisado={() => {
                  cargar();
                  onCambio?.();
                }}
              />
            ))}
          </ul>
        )}

        {revisados.length > 0 && (
          <details className="border-t pt-3">
            <summary className="cursor-pointer text-sm font-medium">Revisados ({revisados.length})</summary>
            <ul className="mt-2 flex flex-col gap-1 text-sm">
              {revisados.map((p) => (
                <li key={p.id} className="flex flex-wrap justify-between gap-2">
                  <span>
                    {p.fechaPago} · {pesos(p.monto)} · {etiquetaMedioPago(p.medio)}
                    {mostrarCliente && ` · ${p.clienteRazonSocial}`}
                  </span>
                  <span className={p.estado === "rechazado" ? "text-destructive" : "text-muted-foreground"}>
                    {etiquetaEstadoPagoInformado(p.estado)}
                    {p.revisadoPor && ` por ${p.revisadoPor}`}
                    {p.motivoRechazo && ` — ${p.motivoRechazo}`}
                  </span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </CardContent>
    </Card>
  );
}

function PendienteFila({ pago, mostrarCliente, onRevisado }: {
  pago: PagoInformadoResumen; mostrarCliente: boolean; onRevisado: () => void;
}) {
  const { fetchConSesion } = useAuth();
  const [monto, setMonto] = useState(String(pago.monto));
  const [fecha, setFecha] = useState(pago.fechaPago);
  const [rechazando, setRechazando] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function accion(ruta: string, cuerpo: object) {
    setError(null);
    setGuardando(true);
    try {
      const resp = await fetchConSesion(ruta, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cuerpo),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onRevisado();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(false);
    }
  }

  // El comprobante viaja con la sesión (header Authorization): se baja el blob y se abre en otra pestaña.
  async function verComprobante() {
    try {
      const resp = await fetchConSesion(`/api/pagos-informados/${pago.id}/comprobante`);
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      window.open(URL.createObjectURL(await resp.blob()), "_blank");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo abrir el comprobante.");
    }
  }

  return (
    <li className="flex flex-col gap-2 py-3 text-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span>
          <span className="font-medium tabular-nums">{pesos(pago.monto)}</span>
          <span className="text-muted-foreground">
            {" "}· {etiquetaMedioPago(pago.medio)} · pagado el {pago.fechaPago} · informó {pago.informadoPor}
            {mostrarCliente && ` (${pago.clienteRazonSocial})`}
          </span>
        </span>
        {pago.tieneComprobante && (
          <Button variant="link" size="sm" className="h-auto p-0" onClick={verComprobante}>
            <Paperclip className="size-3" /> Comprobante
          </Button>
        )}
      </div>
      {pago.nota && <p className="text-muted-foreground">Nota: {pago.nota}</p>}

      {!rechazando ? (
        <div className="flex flex-wrap items-center gap-2">
          <Input
            type="number"
            aria-label="Monto a imputar"
            className="w-32"
            min={0.01}
            step={0.01}
            value={monto}
            onChange={(e) => setMonto(e.target.value)}
          />
          <Input type="date" aria-label="Fecha del pago" className="w-40" value={fecha} onChange={(e) => setFecha(e.target.value)} />
          <Button
            size="sm"
            disabled={guardando || !(Number(monto) > 0)}
            onClick={() => accion(`/api/pagos-informados/${pago.id}/confirmar`, { monto: Number(monto), fechaPago: fecha })}
          >
            Confirmar e imputar
          </Button>
          <Button size="sm" variant="outline" disabled={guardando} onClick={() => setRechazando(true)}>
            Rechazar
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <Input
            aria-label="Motivo del rechazo"
            placeholder="Motivo (lo ve el cliente)"
            className="min-w-60 flex-1"
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <Button
            size="sm"
            variant="destructive"
            disabled={guardando || !motivo.trim()}
            onClick={() => accion(`/api/pagos-informados/${pago.id}/rechazar`, { motivo: motivo.trim() })}
          >
            Rechazar pago
          </Button>
          <Button size="sm" variant="ghost" onClick={() => setRechazando(false)}>
            Volver
          </Button>
        </div>
      )}
      {error && <p className="text-destructive">{error}</p>}
    </li>
  );
}
