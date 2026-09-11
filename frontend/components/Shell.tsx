"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth/AuthProvider";
import type { Rol } from "@/lib/auth/types";
import { cn } from "@/lib/utils";

interface ItemNav {
  href: string;
  label: string;
  roles: Rol[];
}

const NAV: ItemNav[] = [
  { href: "/pedidos", label: "Pedidos", roles: ["administracion", "operacion"] },
  { href: "/rutas", label: "Rutas", roles: ["administracion", "operacion"] },
  { href: "/clientes", label: "Clientes", roles: ["administracion"] },
  { href: "/vehiculos", label: "Vehículos", roles: ["administracion"] },
  { href: "/tarifas", label: "Tarifas", roles: ["administracion"] },
  { href: "/facturas", label: "Facturas", roles: ["administracion"] },
  { href: "/usuarios", label: "Usuarios", roles: ["administracion"] },
  { href: "/depositos", label: "Depósitos", roles: ["administracion"] },
  { href: "/exportar", label: "Exportar", roles: ["administracion"] },
];

/**
 * Nav lateral filtrada por rol para el back-office (antes cada página tenía botones ad-hoc
 * "← Pedidos" para saltar entre secciones). Las páginas siguen mostrando su propia
 * <CabeceraSesion> con el título y el botón de salir; Shell solo aporta el marco de navegación.
 *
 * No se aplica fuera de administracion/operacion: /hoy (PWA del repartidor) tiene sus propias
 * reglas de diseño — un dedo, sin scroll (RNF-06) — y una sidebar las rompería.
 */
export function Shell({ children }: { children: React.ReactNode }) {
  const { usuario } = useAuth();
  const pathname = usePathname();

  if (!usuario || (usuario.rol !== "administracion" && usuario.rol !== "operacion")) {
    return <>{children}</>;
  }

  const items = NAV.filter((item) => item.roles.includes(usuario.rol));

  return (
    <div className="flex min-h-svh flex-1">
      <aside className="w-52 shrink-0 border-r p-4 flex flex-col gap-1">
        <p className="text-sm font-semibold px-2 mb-3">Logística</p>
        {items.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={cn(
              "rounded-md px-2 py-1.5 text-sm hover:bg-accent",
              pathname.startsWith(item.href) && "bg-accent font-medium",
            )}
          >
            {item.label}
          </Link>
        ))}
      </aside>
      <div className="flex-1">{children}</div>
    </div>
  );
}
