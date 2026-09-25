"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { esClienteDueno } from "@/lib/auth/types";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { DetalleViaje } from "@/components/viajes/DetalleViaje";
import { Button } from "@/components/ui/button";

export default function ViajePortalPage() {
  const params = useParams<{ id: string }>();
  const viajeId = Number(params.id);

  return (
    <RequireRole roles={["cliente"]}>
      <div className="p-4 md:p-8 max-w-4xl">
        <Button variant="outline" render={<Link href="/mis-envios" />} nativeButton={false} className="mb-4">
          ← Mis envíos
        </Button>
        <CabeceraSesion titulo={`Viaje #${params.id}`} />
        {Number.isFinite(viajeId) ? <Detalle viajeId={viajeId} /> : <p className="text-sm text-destructive">Viaje inválido.</p>}
      </div>
    </RequireRole>
  );
}

function Detalle({ viajeId }: { viajeId: number }) {
  const { usuario } = useAuth();
  return (
    <DetalleViaje
      ruta={`/api/mi-cuenta/viajes/${viajeId}`}
      hrefParada={(id) => `/mis-envios/${id}`}
      // Cancelar el viaje entero es del dueño; el empleado lo ve pero no lo cancela.
      rutaCancelar={esClienteDueno(usuario) ? `/api/mi-cuenta/viajes/${viajeId}/cancelar` : undefined}
    />
  );
}
