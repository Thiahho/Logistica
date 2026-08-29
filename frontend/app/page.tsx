"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth/AuthProvider";
import { rutaPorRol } from "@/lib/auth/types";

export default function Home() {
  const { usuario, cargando } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (cargando) return;
    router.replace(usuario ? rutaPorRol(usuario.rol) : "/login");
  }, [cargando, usuario, router]);

  return null;
}
