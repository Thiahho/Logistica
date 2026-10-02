"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useAuth } from "./AuthProvider";
import { esClienteDueno, rutaPorRol, type Rol } from "./types";

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
  const pathname = usePathname();

  useEffect(() => {
    if (!cargando && !usuario) router.replace("/login");
  }, [cargando, usuario, router]);

  if (cargando) return <p className="p-4 md:p-8 text-muted-foreground">Cargando…</p>;
  if (!usuario) return null;

  const rolNoAlcanza = !roles.includes(usuario.rol);
  if (rolNoAlcanza || (soloDueno && !esClienteDueno(usuario))) {
    // Nunca un callejón sin salida: se dice con qué cuenta está la sesión y se ofrece cómo seguir. El
    // caso típico es haber entrado con otra cuenta en otra pestaña (la sesión es una por navegador).
    return (
      <div className="flex max-w-xl flex-col gap-3 p-4 md:p-8">
        <p className="text-destructive">
          {rolNoAlcanza
            ? `No autorizado: tu rol (${usuario.rol}) no puede ver esta pantalla.`
            : "No autorizado: esta pantalla es solo para el dueño de la cuenta."}
        </p>
        <p className="text-sm text-muted-foreground">
          Estás con la cuenta de {usuario.nombre}. La sesión es una sola por navegador: si entraste con otra cuenta
          en otra pestaña, esta pestaña también pasó a usarla.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => router.replace(rutaPorRol(usuario.rol))}>Ir a mi inicio</Button>
          {/* Sin cerrar la sesión antes: entrar con otra cuenta ya la reemplaza, y cerrarla acá
              dispararía el efecto de arriba, que manda a /login sin el "volver". */}
          <Button variant="outline" onClick={() => router.push(`/login?volver=${encodeURIComponent(pathname)}`)}>
            Entrar con otra cuenta
          </Button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
