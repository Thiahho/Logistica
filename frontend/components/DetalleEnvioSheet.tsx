"use client";

import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PedidoDetalleCliente } from "@/components/PedidoDetalleCliente";

interface DetalleEnvioSheetProps {
  /** null = cerrado. */
  pedidoId: number | null;
  onCerrar: () => void;
}

/** Detalle de un envío en un popup: hoja inferior en pantalla chica y modal centrado desde `md`
 * (ver ui/dialog.tsx). `PedidoDetalleCliente` se monta solo mientras está abierto, así que cada
 * apertura arranca con estado limpio y vuelve a pedir el envío — el estado siempre está al día. */
export function DetalleEnvioSheet({ pedidoId, onCerrar }: DetalleEnvioSheetProps) {
  return (
    <Dialog open={pedidoId !== null} onOpenChange={(abierto) => !abierto && onCerrar()}>
      <DialogContent className="max-w-xl bg-background">
        <div className="mx-auto mb-3 h-1 w-10 rounded-full bg-border md:hidden" aria-hidden />
        <DialogHeader>
          <DialogTitle>Envío #{pedidoId}</DialogTitle>
        </DialogHeader>
        {pedidoId !== null && <PedidoDetalleCliente pedidoId={pedidoId} />}
      </DialogContent>
    </Dialog>
  );
}
