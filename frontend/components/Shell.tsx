"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  AlertTriangle,
  Bike,
  Building2,
  CalendarClock,
  ClipboardCheck,
  Download,
  LayoutGrid,
  LogOut,
  Map as MapaIcono,
  Menu,
  Package,
  PackageCheck,
  Receipt,
  Route,
  Tag,
  Truck,
  UserCog,
  Users,
  Wallet,
  Warehouse,
  X,
  type LucideIcon,
} from "lucide-react";
import { useAuth } from "@/lib/auth/AuthProvider";
import type { Rol } from "@/lib/auth/types";
import { cn } from "@/lib/utils";
import { LogoBF } from "@/components/LogoBF";
import { Button } from "@/components/ui/button";
import { Drawer, DrawerClose, DrawerContent, DrawerTitle } from "@/components/ui/drawer";
import { useSondeo } from "@/lib/hooks/useSondeo";
import type { ListaPaginada, NovedadResumen } from "@/lib/dominio/tipos";

interface ItemNav {
  href: string;
  label: string;
  icono: LucideIcon;
  roles: Rol[];
}

const NAV: ItemNav[] = [
  { href: "/pedidos", label: "Pedidos", icono: Package, roles: ["administracion", "operacion"] },
  { href: "/recepcion", label: "Recepción", icono: PackageCheck, roles: ["administracion", "operacion"] },
  { href: "/deliverys", label: "Deliverys", icono: Bike, roles: ["administracion", "operacion"] },
  { href: "/jornada", label: "Jornada", icono: CalendarClock, roles: ["administracion", "operacion"] },
  { href: "/rutas", label: "Rutas", icono: Route, roles: ["administracion", "operacion"] },
  { href: "/repartidores", label: "Repartidores", icono: Users, roles: ["administracion", "operacion"] },
  { href: "/clientes", label: "Clientes", icono: Building2, roles: ["administracion"] },
  { href: "/vehiculos", label: "Vehículos", icono: Truck, roles: ["administracion"] },
  { href: "/tarifas", label: "Tarifas", icono: Tag, roles: ["administracion"] },
  { href: "/facturas", label: "Facturas", icono: Receipt, roles: ["administracion"] },
  { href: "/cobranza", label: "Cobranza", icono: Wallet, roles: ["administracion"] },
  { href: "/usuarios", label: "Usuarios", icono: UserCog, roles: ["administracion"] },
  { href: "/depositos", label: "Depósitos", icono: Warehouse, roles: ["administracion"] },
  { href: "/exportar", label: "Exportar", icono: Download, roles: ["administracion"] },
];

/** Accesos de la barra inferior en pantalla chica: lo que se usa todo el día. El resto de NAV vive
 * en el menú (drawer) que abre el quinto botón. */
const NAV_PRINCIPAL_MOVIL = ["/pedidos", "/rutas", "/jornada", "/repartidores"];

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

function activoBackOffice(pathname: string, href: string): boolean {
  return pathname.startsWith(href);
}

/**
 * Marco del back-office (administracion/operacion). Desde `md`: sidebar fija con logo. En pantalla
 * chica: barra superior con logo + hamburguesa, y barra inferior con los cuatro accesos del día más
 * "Menú"; ambos abren el mismo drawer con todos los ítems del rol y la sesión.
 */
function ShellBackOffice({ children }: { children: React.ReactNode }) {
  const { usuario, logout } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const [menuAbierto, setMenuAbierto] = useState(false);

  if (!usuario) return null;

  const items = NAV.filter((item) => item.roles.includes(usuario.rol));
  const principales = items.filter((item) => NAV_PRINCIPAL_MOVIL.includes(item.href));

  async function onLogout() {
    setMenuAbierto(false);
    await logout();
    router.push("/login");
  }

  function enlace({ href, label, icono: Icono }: ItemNav, alTocar?: () => void) {
    return (
      <Link
        key={href}
        href={href}
        onClick={alTocar}
        className={cn(
          "flex items-center gap-2 rounded-xl px-3 py-2 text-sm text-muted-foreground hover:bg-accent",
          activoBackOffice(pathname, href) && "bg-accent font-semibold text-bf-azul",
        )}
      >
        <Icono className="size-4" />
        {label}
        {href === "/jornada" && <IndicadorNovedades />}
      </Link>
    );
  }

  const sesion = (
    <div className="mt-auto flex flex-col gap-2 border-t pt-3">
      <p className="truncate px-1 text-sm text-muted-foreground">
        {usuario.nombre} · {usuario.rol}
      </p>
      <Button variant="outline" className="w-full justify-start gap-2" onClick={onLogout}>
        <LogOut className="size-4" />
        Salir
      </Button>
    </div>
  );

  return (
    <div className="flex min-h-svh flex-1 flex-col bg-background text-foreground md:flex-row">
      <aside className="hidden w-56 shrink-0 flex-col gap-1 border-r bg-card p-4 md:sticky md:top-0 md:flex md:h-svh md:overflow-y-auto">
        <LogoBF className="mb-4 px-2" />
        {items.map((item) => enlace(item))}
        {sesion}
      </aside>

      <header className="sticky top-0 z-30 flex items-center justify-between border-b bg-card px-4 pb-2 pt-[max(0.5rem,env(safe-area-inset-top))] md:hidden">
        <LogoBF />
        <Button
          variant="outline"
          size="icon"
          className="size-10 rounded-full"
          onClick={() => setMenuAbierto(true)}
          aria-label="Abrir el menú"
        >
          <Menu className="size-5" />
        </Button>
      </header>

      {/* pb-20: el contenido no queda tapado por la barra fija de abajo (solo en pantalla chica). */}
      <div className="min-w-0 flex-1 pb-20 md:pb-0">{children}</div>

      <nav
        className="fixed inset-x-0 bottom-0 z-40 flex rounded-t-2xl border-t bg-card pb-[env(safe-area-inset-bottom)] shadow-[0_-4px_16px_rgba(0,59,149,0.08)] md:hidden"
        aria-label="Navegación principal"
      >
        {principales.map(({ href, label, icono: Icono }) => {
          const activo = activoBackOffice(pathname, href);
          return (
            <Link
              key={href}
              href={href}
              aria-current={activo ? "page" : undefined}
              className={cn(
                "flex h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs",
                activo ? "font-semibold text-bf-azul" : "text-muted-foreground",
              )}
            >
              <span className={cn("flex h-7 w-12 items-center justify-center rounded-full", activo && "bg-accent")}>
                <Icono className="size-5" />
              </span>
              {label}
            </Link>
          );
        })}
        <button
          type="button"
          onClick={() => setMenuAbierto(true)}
          className="flex h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground"
        >
          <span className="flex h-7 w-12 items-center justify-center rounded-full">
            <LayoutGrid className="size-5" />
          </span>
          Menú
        </button>
      </nav>

      <Drawer open={menuAbierto} onOpenChange={setMenuAbierto}>
        <DrawerContent>
          <div className="mb-3 flex items-center justify-between">
            <DrawerTitle>Menú</DrawerTitle>
            <DrawerClose
              className="flex size-10 items-center justify-center rounded-full border hover:bg-accent"
              aria-label="Cerrar el menú"
            >
              <X className="size-4" />
            </DrawerClose>
          </div>
          {items.map((item) => enlace(item, () => setMenuAbierto(false)))}
          {sesion}
        </DrawerContent>
      </Drawer>
    </div>
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
      <div className="flex min-h-svh flex-1 flex-col bg-background text-foreground md:flex-row">
        <aside className="hidden w-56 shrink-0 flex-col gap-1 border-r bg-card p-4 md:flex">
          <LogoBF className="mb-4 px-2" />
          {NAV_REPARTIDOR.map(({ href, label, icono: Icono }) => (
            <Link
              key={href}
              href={href}
              className={cn(
                "flex items-center gap-2 rounded-xl px-3 py-2 text-sm text-muted-foreground hover:bg-accent",
                activoRepartidor(pathname, href) && "bg-accent font-semibold text-bf-azul",
              )}
            >
              <Icono className="size-4" />
              {label}
            </Link>
          ))}
        </aside>

        {/* pb-20: el contenido no queda tapado por la barra fija de abajo (solo en pantalla chica). */}
        <div className="flex-1 pb-20 md:pb-0">{children}</div>

        {/* Barra inferior: botones de 56 px de alto, con ícono y etiqueta (RNF-06). z-40 queda por
        debajo del mapa expandido de /hoy (z-50), que tiene que tapar todo. El padding inferior
        respeta el área segura de los teléfonos con barra de gestos (viewportFit: cover). */}
        <nav
          className="fixed inset-x-0 bottom-0 z-40 flex rounded-t-2xl border-t bg-card pb-[env(safe-area-inset-bottom)] shadow-[0_-4px_16px_rgba(0,59,149,0.08)] md:hidden"
          aria-label="Mi jornada"
        >
          {NAV_REPARTIDOR.map(({ href, label, icono: Icono }) => {
            const activo = activoRepartidor(pathname, href);
            return (
              <Link
                key={href}
                href={href}
                aria-current={activo ? "page" : undefined}
                className={cn(
                  "flex h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs",
                  activo ? "font-semibold text-bf-azul" : "text-muted-foreground",
                )}
              >
                <span className={cn("flex h-7 w-12 items-center justify-center rounded-full", activo && "bg-accent")}>
                  <Icono className="size-5" />
                </span>
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

  return <ShellBackOffice>{children}</ShellBackOffice>;
}
