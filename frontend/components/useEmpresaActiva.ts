"use client";

import { useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";
import type { ServicioPropio } from "@/lib/dominio/tipos";

/**
 * Portal: si la empresa del login está activa (GET /api/mi-cuenta/servicio, dueño y empleado). null
 * mientras no se sabe o si la consulta falla: sin el dato no se afirma nada, y lo retenido se sigue
 * diciendo "Suspendido por falta de pago" (etiquetaRetenidoPortal).
 *
 * `habilitado = false` no consulta: para los componentes que también usa el back-office, donde ese
 * endpoint no aplica.
 */
export function useEmpresaActiva(habilitado = true): boolean | null {
  const { fetchConSesion } = useAuth();
  const [empresaActiva, setEmpresaActiva] = useState<boolean | null>(null);

  useEffect(() => {
    if (!habilitado) return;
    fetchConSesion("/api/mi-cuenta/servicio")
      .then((r) => leerJson<ServicioPropio>(r))
      .then((s) => setEmpresaActiva(s.empresaActiva))
      .catch(() => setEmpresaActiva(null));
  }, [habilitado, fetchConSesion]);

  return empresaActiva;
}
