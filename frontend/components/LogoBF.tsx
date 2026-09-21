import { cn } from "@/lib/utils";

/**
 * Logo de BF Transportes. Todavía no hay un archivo del logo sobre fondo claro en public/ (docs/Logo.png
 * es un resplandor azul sobre negro, que no sirve en un header blanco), así que por ahora es un
 * wordmark tipográfico. Cuando llegue el archivo (public/logo-bf.svg o .png) se cambia solo este
 * componente por un <Image> y todas las pantallas lo toman.
 */
export function LogoBF({ className }: { className?: string }) {
  return (
    <span
      role="img"
      aria-label="BF Transportes"
      className={cn("inline-flex flex-col leading-none font-black italic tracking-tight text-bf-azul", className)}
    >
      <span className="text-2xl">BF</span>
      <span className="text-[0.6rem] tracking-wide">TRANSPORTES</span>
    </span>
  );
}
