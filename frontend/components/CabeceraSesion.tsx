"use client";

import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";

/** Cabecera de las pantallas del back-office (y de /mis-envios). En pantalla chica el usuario y
 * "Salir" ya están en el menú del Shell del back-office, así que ahí queda solo el título; a partir
 * de `md` (sin barra superior) se muestran a la derecha. El cliente (/mis-envios) no tiene Shell: ve
 * la sesión en todos los tamaños. */
export function CabeceraSesion({ titulo }: { titulo: string }) {
  const { usuario, logout } = useAuth();
  const router = useRouter();

  async function onLogout() {
    await logout();
    router.push("/login");
  }

  return (
    <div className="mb-4 flex items-center justify-between gap-3 md:mb-6">
      <h1 className="font-display text-xl font-bold tracking-tight text-bf-azul md:text-2xl">{titulo}</h1>
      <div
        className={`items-center gap-3 text-sm text-muted-foreground ${usuario?.rol === "cliente" ? "flex" : "hidden md:flex"}`}
      >
        <span>
          {usuario?.nombre} · {usuario?.rol}
        </span>
        <Button variant="outline" size="sm" onClick={onLogout}>
          Salir
        </Button>
      </div>
    </div>
  );
}
