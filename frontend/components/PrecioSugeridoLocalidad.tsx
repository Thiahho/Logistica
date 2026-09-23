"use client";

import { useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";
import {
  etiquetaTipoVehiculo,
  type PrecioSugeridoLocalidad as PrecioSugerido,
  type TipoVehiculo,
} from "@/lib/dominio/tipos";

interface PrecioSugeridoLocalidadProps {
  localidadId: number | null;
  /** "/api/mi-cuenta" para el portal del cliente; "/api" para el back-office. */
  basePath: string;
  /** Vehículo elegido en el formulario: se resalta su precio. */
  tipoVehiculo?: TipoVehiculo;
}

type Resultado = { localidadId: number; precio: PrecioSugerido | null };

const pesos = (n: number) => `$${n.toLocaleString("es-AR")}`;

/** Precio orientativo al elegir la localidad: solo la tarifa de la zona (que la localidad recibe sola
 * según su distancia al depósito), nunca recargos. El precio vinculante se fija al crear el envío. */
export function PrecioSugeridoLocalidad({ localidadId, basePath, tipoVehiculo }: PrecioSugeridoLocalidadProps) {
  const { fetchConSesion } = useAuth();
  const [resultado, setResultado] = useState<Resultado | null>(null);

  // "Cargando" se deriva de resultado.localidadId !== localidadId: sin setState síncrono en el
  // efecto (react-hooks/set-state-in-effect), mismo criterio que FacturaDetalleContenido.
  useEffect(() => {
    if (localidadId === null) return;
    const abort = new AbortController();
    fetchConSesion(`${basePath}/localidades/${localidadId}/precio-sugerido`, { signal: abort.signal })
      .then((r) => leerJson<PrecioSugerido>(r))
      .then((precio) => setResultado({ localidadId, precio }))
      .catch(() => {
        if (!abort.signal.aborted) setResultado({ localidadId, precio: null });
      });
    return () => abort.abort();
  }, [localidadId, basePath, fetchConSesion]);

  if (localidadId === null) return null;
  if (resultado?.localidadId !== localidadId) {
    return <p className="text-sm text-muted-foreground">Calculando precio sugerido…</p>;
  }

  const p = resultado.precio;
  if (!p) return null; // el aviso es un extra: si falla, el formulario sigue igual
  if (p.requiereCotizacion) {
    return (
      <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800">
        {p.zonaCodigo
          ? `Zona ${p.zonaCodigo} todavía sin tarifa cargada: te confirmamos el precio antes del retiro.`
          : "No pudimos ubicar esta localidad en una zona: te confirmamos el precio antes del retiro."}
      </p>
    );
  }

  return (
    <div className="rounded-lg border bg-muted/40 p-3 text-sm">
      <p className="font-medium">
        Zona {p.zonaCodigo}
        {p.zonaNombre ? ` — ${p.zonaNombre}` : ""}
        {p.distanciaKm !== null && p.distanciaKm > 0 ? ` · a ${p.distanciaKm} km del depósito` : ""}
      </p>
      <ul className="mt-1 flex flex-wrap gap-x-4 text-muted-foreground">
        {(["camioneta", "moto"] as const).map((tipo) => {
          const valor = p[tipo];
          return (
            <li key={tipo} className={tipo === tipoVehiculo ? "font-semibold text-foreground" : undefined}>
              {etiquetaTipoVehiculo(tipo)}: {valor !== null ? pesos(valor) : "a cotizar"}
            </li>
          );
        })}
      </ul>
      <p className="mt-1 text-xs text-muted-foreground">
        Precio sugerido por zona. El total se confirma al cargar el envío (urgencia y otros recargos
        se suman aparte).
      </p>
    </div>
  );
}
