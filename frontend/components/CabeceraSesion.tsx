"use client";

import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";

export function CabeceraSesion({ titulo }: { titulo: string }) {
  const { usuario, logout } = useAuth();
  const router = useRouter();

  async function onLogout() {
    await logout();
    router.push("/login");
  }

  return (
    <div className="flex items-center justify-between mb-6">
      <h1 className="text-xl font-semibold">{titulo}</h1>
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
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
