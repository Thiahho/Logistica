"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "./AuthProvider";
import { esClienteDueno, type Rol } from "./types";

export function RequireRole({
  roles,
  soloDueno = false,
  children,
}: {
  roles: Rol[];
  /** Solo el dueño de la empresa cliente (no sus empleados). */
  soloDueno?: boolean;
  children: React.ReactNode;
}) {
  const { usuario, cargando } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!cargando && !usuario) router.replace("/login");
  }, [cargando, usuario, router]);

  if (cargando) return <p className="p-4 md:p-8 text-muted-foreground">Cargando…</p>;
  if (!usuario) return null;

  if (!roles.includes(usuario.rol)) {
    return (
      <p className="p-4 md:p-8 text-destructive">
        No autorizado: tu rol ({usuario.rol}) no puede ver esta pantalla.
      </p>
    );
  }

  if (soloDueno && !esClienteDueno(usuario)) {
    return (
      <p className="p-4 md:p-8 text-destructive">
        No autorizado: esta pantalla es solo para el dueño de la cuenta.
      </p>
    );
  }

  return <>{children}</>;
}
