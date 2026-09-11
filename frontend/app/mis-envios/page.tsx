"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerJson } from "@/lib/api/errores";
import { etiquetaEstadoFactura, type CuentaPropia, type ListaPaginada, type PedidoResumen } from "@/lib/dominio/tipos";

export default function MisEnviosPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <ListaEnvios />
    </RequireRole>
  );
}

/** E1: resumen de cuenta corriente del cliente logueado — solo lectura, sin acciones (el cobro
 * lo gestiona la Empresa; Anexo I §7 excluye pasarelas de pago online). Card aparte arriba de la
 * lista de envíos, sin navegación nueva: el rol 'cliente' no recibe <Shell> hoy (construir nav
 * para ese rol es alcance de E4, portal del cliente, no de E1). */
function MiCuenta() {
  const { fetchConSesion } = useAuth();
  const [cuenta, setCuenta] = useState<CuentaPropia | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta")
      .then((r) => leerJson<CuentaPropia>(r))
      .then(setCuenta)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar tu cuenta."));
  }, [fetchConSesion]);

  if (error) return null; // no bloquea la lista de envíos si esto falla
  if (!cuenta) return <p className="text-sm text-muted-foreground mb-4">Cargando tu cuenta…</p>;

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Mi cuenta</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="grid grid-cols-3 gap-4 text-center">
          <div className="rounded-lg border p-3">
            <p className="text-2xl font-semibold">${cuenta.saldo.toLocaleString("es-AR")}</p>
            <p className="text-xs text-muted-foreground">Saldo</p>
          </div>
          <div className="rounded-lg border p-3">
            <p className={`text-2xl font-semibold ${cuenta.deudaVencida > 0 ? "text-destructive" : ""}`}>
              ${cuenta.deudaVencida.toLocaleString("es-AR")}
            </p>
            <p className="text-xs text-muted-foreground">Deuda vencida</p>
          </div>
          <div className="rounded-lg border p-3">
            <p className="text-sm font-semibold">{cuenta.proximoVencimiento ?? "—"}</p>
            <p className="text-xs text-muted-foreground">Próximo vencimiento</p>
          </div>
        </div>

        {cuenta.servicioCortado && (
          <p className="text-sm text-destructive">
            Tu servicio está interrumpido por deuda vencida. Los envíos ya confirmados o en ruta
            siguen su curso; no se pueden cargar envíos nuevos hasta regularizar.
          </p>
        )}

        {cuenta.facturas.length > 0 && (
          <ul className="flex flex-col gap-2 border-t pt-3">
            {cuenta.facturas.map((f) => (
              <li key={f.id} className="text-sm border-b pb-2 flex justify-between gap-4">
                <span>
                  {f.periodoDesde} al {f.periodoHasta}
                  <span className="text-muted-foreground"> · vence {f.fechaVencimiento}</span>
                </span>
                <span className="shrink-0">
                  <span className={f.estado === "vencida" ? "text-destructive font-medium" : "font-medium"}>
                    ${f.saldo.toLocaleString("es-AR")}
                  </span>
                  <span className="text-muted-foreground"> · {etiquetaEstadoFactura(f.estado)}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

function ListaEnvios() {
  const { fetchConSesion } = useAuth();
  const [pedidos, setPedidos] = useState<PedidoResumen[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/pedidos")
      .then((r) => leerJson<ListaPaginada<PedidoResumen>>(r))
      .then((r) => setPedidos(r.items))
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los envíos."));
  }, [fetchConSesion]);

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Mis envíos" />
      <MiCuenta />
      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !pedidos ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : pedidos.length === 0 ? (
        <p className="text-muted-foreground">Todavía no tenés envíos.</p>
      ) : (
        <ul className="flex flex-col gap-3">
          {pedidos.map((p) => (
            <li key={p.id} className="rounded-lg border p-4">
              <div className="flex items-center justify-between">
                <span className="font-medium">{p.destinatarioNombre}</span>
                <span className="text-xs uppercase text-muted-foreground">{p.estado}</span>
              </div>
              <p className="text-sm text-muted-foreground">Entrega: {p.fechaEntrega}</p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
