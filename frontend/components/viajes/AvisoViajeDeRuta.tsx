"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";

interface ViajeDeRuta {
  id: number;
  clienteId: number;
  clienteRazonSocial: string;
}

/** Aviso en el armado y el detalle de una ruta cuando es la propuesta de un viaje: es de un solo
 * cliente (el backend rechaza sumarle envíos de otro) y el orden lo sugirió el sistema. */
export function AvisoViajeDeRuta({ rutaId }: { rutaId: number }) {
  const { fetchConSesion } = useAuth();
  const [viaje, setViaje] = useState<ViajeDeRuta | null>(null);

  useEffect(() => {
    fetchConSesion(`/api/viajes/de-ruta/${rutaId}`)
      .then((r) => (r.status === 204 ? null : leerJson<ViajeDeRuta>(r)))
      .then(setViaje)
      .catch(() => setViaje(null));
  }, [fetchConSesion, rutaId]);

  if (!viaje) return null;

  return (
    <div className="rounded-xl border border-bf-azul/30 bg-accent px-4 py-3 text-sm">
      <p>
        <span className="font-semibold text-bf-azul">Viaje de {viaje.clienteRazonSocial}</span>{" "}
        <Link href={`/viajes/${viaje.id}`} className="underline-offset-2 hover:underline">
          (viaje #{viaje.id})
        </Link>
        . El orden lo propuso el sistema por recorrido más corto: revisalo, asigná vehículo y repartidor y cerrá la
        planificación. Esta ruta solo admite envíos de este cliente.
      </p>
    </div>
  );
}
