import type { Disponibilidad, EstadoPedido, EstadoRuta } from "@/lib/dominio/tipos";
import { etiquetaEstadoRuta } from "@/lib/dominio/tipos";

/** Estilo/etiqueta de estado de parada, antes duplicado literal en app/hoy/page.tsx y
 * app/hoy/parada/[paradaId]/page.tsx. Unificado acá y sumado el estado de ruta, que hasta
 * ahora se mostraba crudo ("en_curso") en /rutas. Verde/amarillo/azul/rojo literales acá son
 * indicadores de estado (misma excepción que components/mapa/Mapa.tsx), no decoración. */
const ESTILO_PARADA: Record<string, string> = {
  pendiente: "bg-bf-celeste/20 text-bf-profundo",
  completada: "bg-green-100 text-green-700",
  fallida: "bg-red-100 text-red-700",
  cancelada: "bg-neutral-200 text-neutral-700",
};

const ETIQUETA_PARADA: Record<string, string> = {
  pendiente: "Pendiente",
  completada: "Entregada",
  fallida: "Fallida",
  cancelada: "Cancelada",
};

const ESTILO_RUTA: Record<EstadoRuta, string> = {
  planificada: "bg-neutral-100 text-neutral-700",
  en_curso: "bg-bf-celeste/20 text-bf-profundo",
  cerrada: "bg-green-100 text-green-700",
};

export function etiquetaEstadoParada(estado: string): string {
  return ETIQUETA_PARADA[estado] ?? estado;
}

const PADDING: Record<"sm" | "md", string> = {
  sm: "px-2 py-0.5",
  md: "px-3 py-1",
};

function Badge({ className, size = "sm", children }: { className: string; size?: "sm" | "md"; children: React.ReactNode }) {
  return <span className={`rounded-full text-xs font-medium ${PADDING[size]} ${className}`}>{children}</span>;
}

export function EstadoParadaBadge({ estado, size }: { estado: string; size?: "sm" | "md" }) {
  return (
    <Badge className={ESTILO_PARADA[estado] ?? "bg-neutral-100 text-neutral-700"} size={size}>
      {etiquetaEstadoParada(estado)}
    </Badge>
  );
}

export function EstadoRutaBadge({ estado, size }: { estado: EstadoRuta; size?: "sm" | "md" }) {
  return (
    <Badge className={ESTILO_RUTA[estado]} size={size}>
      {etiquetaEstadoRuta(estado)}
    </Badge>
  );
}

/** Disponibilidad del repartidor (RF-34). Vive acá y no en archivo propio porque es un badge de
 * estado más: reusa el mismo Badge y el mismo PADDING, sin una tercera copia del span. */
const ESTILO_DISPONIBILIDAD: Record<Disponibilidad, string> = {
  en_ruta: "bg-bf-celeste/20 text-bf-profundo",
  asignado: "bg-amber-100 text-amber-700",
  libre: "bg-green-100 text-green-700",
  inactivo: "bg-neutral-100 text-neutral-500",
};

const ETIQUETA_DISPONIBILIDAD: Record<Disponibilidad, string> = {
  en_ruta: "En ruta",
  asignado: "Asignado",
  libre: "Libre",
  inactivo: "Inactivo",
};

export function etiquetaDisponibilidad(disponibilidad: string): string {
  return ETIQUETA_DISPONIBILIDAD[disponibilidad as Disponibilidad] ?? disponibilidad;
}

/** Estado de un pedido, visto desde el portal del cliente (/mis-envios) — hasta ahora se
 * imprimía crudo (`{p.estado}`). Mismo patrón que ESTILO_RUTA/ESTILO_DISPONIBILIDAD arriba. */
const ESTILO_PEDIDO: Record<EstadoPedido, string> = {
  Borrador: "bg-neutral-100 text-neutral-700",
  Confirmado: "bg-amber-100 text-amber-700",
  EnRuta: "bg-bf-celeste/20 text-bf-profundo",
  Entregado: "bg-green-100 text-green-700",
  Fallido: "bg-red-100 text-red-700",
  Reprogramado: "bg-amber-100 text-amber-700",
  Devuelto: "bg-neutral-200 text-neutral-700",
  Cancelado: "bg-neutral-200 text-neutral-500",
};

const ETIQUETA_PEDIDO: Record<EstadoPedido, string> = {
  Borrador: "Cargado",
  Confirmado: "Confirmado",
  EnRuta: "En camino",
  Entregado: "Entregado",
  Fallido: "Entrega fallida",
  Reprogramado: "Reprogramado",
  Devuelto: "Devuelto",
  Cancelado: "Cancelado",
};

export function etiquetaEstadoPedido(estado: string): string {
  return ETIQUETA_PEDIDO[estado as EstadoPedido] ?? estado;
}

export function EstadoPedidoBadge({ estado, size }: { estado: EstadoPedido; size?: "sm" | "md" }) {
  return (
    <Badge className={ESTILO_PEDIDO[estado] ?? "bg-neutral-100 text-neutral-700"} size={size}>
      {etiquetaEstadoPedido(estado)}
    </Badge>
  );
}

export function DisponibilidadBadge({
  disponibilidad,
  size,
}: {
  disponibilidad: Disponibilidad;
  size?: "sm" | "md";
}) {
  return (
    <Badge className={ESTILO_DISPONIBILIDAD[disponibilidad] ?? "bg-neutral-100 text-neutral-700"} size={size}>
      {etiquetaDisponibilidad(disponibilidad)}
    </Badge>
  );
}
