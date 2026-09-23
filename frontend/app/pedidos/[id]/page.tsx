"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { PedidoDetalleContenido } from "@/components/PedidoDetalleContenido";

export default function PedidoDetallePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <DetallePedido />
    </RequireRole>
  );
}

function DetallePedido() {
  const { id } = useParams<{ id: string }>();

  return (
    <div className="p-4 md:p-8 max-w-2xl flex flex-col gap-6">
      <CabeceraSesion titulo={`Pedido #${id}`} />
      <Button variant="outline" render={<Link href="/pedidos" />} nativeButton={false} className="self-start">
        ← Pedidos
      </Button>
      <PedidoDetalleContenido pedidoId={Number(id)} />
    </div>
  );
}
