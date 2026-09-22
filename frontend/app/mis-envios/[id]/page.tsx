"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { PedidoDetalleCliente } from "@/components/PedidoDetalleCliente";

export default function DetalleEnvioPage() {
  const params = useParams<{ id: string }>();
  const pedidoId = Number(params.id);

  return (
    <RequireRole roles={["cliente"]}>
      <div className="p-4 md:p-8 max-w-xl">
        <Button variant="outline" render={<Link href="/mis-envios" />} nativeButton={false} className="mb-4">
          ← Mis envíos
        </Button>
        <CabeceraSesion titulo="Detalle del envío" />
        {Number.isFinite(pedidoId) ? (
          <PedidoDetalleCliente pedidoId={pedidoId} />
        ) : (
          <p className="text-sm text-destructive">Envío inválido.</p>
        )}
      </div>
    </RequireRole>
  );
}
