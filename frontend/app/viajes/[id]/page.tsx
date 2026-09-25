"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { DetalleViaje } from "@/components/viajes/DetalleViaje";
import { Button } from "@/components/ui/button";

export default function ViajeBackOfficePage() {
  const params = useParams<{ id: string }>();
  const viajeId = Number(params.id);

  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <div className="p-4 md:p-8 max-w-4xl">
        <Button variant="outline" render={<Link href="/viajes" />} nativeButton={false} className="mb-4">
          ← Viajes
        </Button>
        <CabeceraSesion titulo={`Viaje #${params.id}`} />
        {Number.isFinite(viajeId) ? (
          <DetalleViaje
            ruta={`/api/viajes/${viajeId}`}
            hrefParada={(id) => `/pedidos/${id}`}
            rutaCancelar={`/api/viajes/${viajeId}/cancelar`}
            mostrarRuta
          />
        ) : (
          <p className="text-sm text-destructive">Viaje inválido.</p>
        )}
      </div>
    </RequireRole>
  );
}
