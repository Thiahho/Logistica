"use client";

import { useState } from "react";
import { useRouter, usePathname, useSearchParams } from "next/navigation";
import { CheckCircle2, X } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * Confirmación de que la parada anterior quedó cerrada: al cerrar una parada la PWA navega con
 * `?hecha=<orden>` y esta franja lo muestra ("✓ Parada 1 cerrada · quedan 3"). Se descarta con la X, que
 * también saca el parámetro de la URL para que no reaparezca al recargar. Va dentro de un Suspense.
 */
export function AvisoParadaHecha({ pendientes }: { pendientes: number }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const [oculto, setOculto] = useState(false);

  const orden = params.get("hecha");
  if (!orden || oculto) return null;

  function descartar() {
    setOculto(true);
    const nuevos = new URLSearchParams(params.toString());
    nuevos.delete("hecha");
    const resto = nuevos.toString();
    router.replace(resto ? `${pathname}?${resto}` : pathname);
  }

  return (
    <div role="status" className="flex items-center gap-3 rounded-2xl border-2 border-green-600 bg-green-50/70 p-3">
      <CheckCircle2 className="size-5 shrink-0 text-green-700" />
      <p className="flex-1 text-sm font-medium">
        Parada {orden} cerrada.{" "}
        {pendientes > 0 ? `Quedan ${pendientes} por hacer.` : "No te quedan paradas: ya podés terminar la ruta."}
      </p>
      <Button variant="ghost" size="icon" className="size-9" aria-label="Cerrar aviso" onClick={descartar}>
        <X className="size-4" />
      </Button>
    </div>
  );
}
