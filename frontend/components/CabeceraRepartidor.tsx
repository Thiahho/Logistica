"use client";

import { useRouter } from "next/navigation";
import Link from "next/link";
import { ChevronLeft, LogOut } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { LogoBF } from "@/components/LogoBF";

/**
 * Cabecera de las pantallas del repartidor (/hoy/*): logo a la izquierda, sesión a la derecha y el
 * título de la pantalla debajo. Es la contraparte mobile de <CabeceraSesion>, que sigue siendo la
 * del back-office. `volverA` agrega la flecha de retorno de las pantallas hijas (parada, retiro…).
 */
export function CabeceraRepartidor({ titulo, volverA }: { titulo: string; volverA?: string }) {
  const { usuario, logout } = useAuth();
  const router = useRouter();

  async function onLogout() {
    await logout();
    router.push("/login");
  }

  return (
    <header className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          {volverA && (
            <Button
              variant="outline"
              size="icon"
              className="size-11 rounded-full bg-card"
              render={<Link href={volverA} aria-label="Volver" />}
              nativeButton={false}
            >
              <ChevronLeft className="size-5" />
            </Button>
          )}
          <LogoBF />
        </div>
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-sm text-muted-foreground">{usuario?.nombre}</span>
          <Button variant="outline" size="icon" className="size-11 rounded-full bg-card" onClick={onLogout} aria-label="Salir">
            <LogOut className="size-4" />
          </Button>
        </div>
      </div>
      <h1 className="font-display text-2xl font-bold tracking-tight text-bf-azul">{titulo}</h1>
    </header>
  );
}
