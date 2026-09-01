"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { leerJson } from "@/lib/api/errores";
import type { ListaPaginada, PedidoResumen } from "@/lib/dominio/tipos";

export default function MisEnviosPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <ListaEnvios />
    </RequireRole>
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
