"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { AlertTriangle, ClipboardCheck, Map as MapaIcono, PackageCheck, type LucideIcon } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthProvider";
import type { Rol } from "@/lib/auth/types";
import { cn } from "@/lib/utils";
import { useSondeo } from "@/lib/hooks/useSondeo";
import type { ListaPaginada, NovedadResumen } from "@/lib/dominio/tipos";

interface ItemNav {
  href: string;
  label: string;
  roles: Rol[];
}

const NAV: ItemNav[] = [
  { href: "/pedidos", label: "Pedidos", roles: ["administracion", "operacion"] },
  { href: "/deliverys", label: "Deliverys", roles: ["administracion", "operacion"] },
  { href: "/jornada", label: "Jornada", roles: ["administracion", "operacion"] },
  { href: "/rutas", label: "Rutas", roles: ["administracion", "operacion"] },
  { href: "/repartidores", label: "Repartidores", roles: ["administracion", "operacion"] },
  { href: "/clientes", label: "Clientes", roles: ["administracion"] },
  { href: "/vehiculos", label: "Vehículos", roles: ["administracion"] },
  { href: "/tarifas", label: "Tarifas", roles: ["administracion"] },
  { href: "/facturas", label: "Facturas", roles: ["administracion"] },
  { href: "/cobranza", label: "Cobranza", roles: ["administracion"] },
  { href: "/usuarios", label: "Usuarios", roles: ["administracion"] },
  { href: "/depositos", label: "Depósitos", roles: ["administracion"] },
  { href: "/exportar", label: "Exportar", roles: ["administracion"] },
];

/** Las cuatro pantallas del día del repartidor, en el orden en que las usa: mira su ruta, retira,
 * avisa si algo sale mal, cierra. La parada (/hoy/parada/[id]) no tiene entrada propia: se llega
 * desde Hoy y cuenta como Hoy. */
const NAV_REPARTIDOR: { href: string; label: string; icono: LucideIcon }[] = [
  { href: "/hoy", label: "Hoy", icono: MapaIcono },
  { href: "/hoy/retiro", label: "Retiro", icono: PackageCheck },
  { href: "/hoy/problema", label: "Problema", icono: AlertTriangle },
  { href: "/hoy/cierre", label: "Cierre", icono: ClipboardCheck },
];

function activoRepartidor(pathname: string, href: string): boolean {
  // "/hoy" es prefijo de todas las demás: comparar exacto (más la parada, que es "parte de Hoy").
  if (href === "/hoy") return pathname === "/hoy" || pathname.startsWith("/hoy/parada");
  return pathname.startsWith(href);
}

/**
 * Nav por rol. Back-office (administracion/operacion): sidebar fija a la izquierda. Repartidor: la
 * PWA se usa con una mano en el teléfono (RNF-06), así que en pantalla chica es una barra inferior
 * —al alcance del pulgar, sin comerse el ancho— y recién desde `md` pasa a sidebar, igual que la de
 * back-office. Las páginas siguen mostrando su propia <CabeceraSesion> con el título y el botón de
 * salir; Shell solo aporta el marco de navegación.
 *
 * Sin chrome para el rol cliente (/mis-envios).
 */
// Rutas sin chrome: /login y / (el redirector de app/page.tsx) no deben mostrar la sidebar aunque
// `usuario` ya esté seteado en el AuthProvider — el setUsuario del login corre antes de que
// router.push termine de navegar, y sin este chequeo la sidebar envuelve por un instante el
// propio formulario de login.
const RUTAS_SIN_CHROME = ["/login", "/"];

/** Cuántas novedades del repartidor esperan respuesta (acta changelog 4.8). Sondea cada 30 s con la pestaña
 * visible — una avería en la calle es justo lo que no puede esperar a que alguien abra /jornada. Pide una
 * página de 1 fila: solo interesa `total`. Un error o un total en cero no muestran nada. */
function IndicadorNovedades() {
  const { datos } = useSondeo<ListaPaginada<NovedadResumen>>("/api/novedades?estado=abierta&tamanioPagina=1", {
    intervaloMs: 30_000,
  });
  if (!datos || datos.total === 0) return null;
  return (
    <span className="ml-2 rounded-full bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-800">
      {datos.total}
    </span>
  );
}

export function Shell({ children }: { children: React.ReactNode }) {
  const { usuario } = useAuth();
  const pathname = usePathname();

  if (!usuario || RUTAS_SIN_CHROME.includes(pathname)) {
    return <>{children}</>;
  }

  if (usuario.rol === "repartidor") {
    return (
      <div className="flex min-h-svh flex-1 flex-col md:flex-row">
        <aside className="hidden w-52 shrink-0 flex-col gap-1 border-r p-4 md:flex">
          <p className="mb-3 px-2 text-sm font-semibold">Logística</p>
          {NAV_REPARTIDOR.map(({ href, label, icono: Icono }) => (
            <Link
              key={href}
              href={href}
              className={cn(
                "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent",
                activoRepartidor(pathname, href) && "bg-accent font-medium",
              )}
            >
              <Icono className="size-4" />
              {label}
            </Link>
          ))}
        </aside>

        {/* pb-16: el contenido no queda tapado por la barra fija de abajo (solo en pantalla chica). */}
        <div className="flex-1 pb-16 md:pb-0">{children}</div>

        {/* Barra inferior: botones de 56 px de alto, con ícono y etiqueta (RNF-06). z-40 queda por
        debajo del mapa expandido de /hoy (z-50), que tiene que tapar todo. */}
        <nav className="fixed inset-x-0 bottom-0 z-40 flex border-t bg-background md:hidden" aria-label="Mi jornada">
          {NAV_REPARTIDOR.map(({ href, label, icono: Icono }) => {
            const activo = activoRepartidor(pathname, href);
            return (
              <Link
                key={href}
                href={href}
                aria-current={activo ? "page" : undefined}
                className={cn(
                  "flex h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs",
                  activo ? "font-semibold text-blue-700" : "text-muted-foreground",
                )}
              >
                <Icono className="size-5" />
                {label}
              </Link>
            );
          })}
        </nav>
      </div>
    );
  }

  if (usuario.rol !== "administracion" && usuario.rol !== "operacion") {
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
            {item.href === "/jornada" && <IndicadorNovedades />}
          </Link>
        ))}
      </aside>
      <div className="flex-1">{children}</div>
    </div>
  );
}
