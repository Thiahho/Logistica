import { Check } from "lucide-react";
import { cn } from "@/lib/utils";
import { etiquetaEstadoPedido } from "@/components/EstadoBadge";
import type { EstadoPedido } from "@/lib/dominio/tipos";

export interface EventoLinea {
  estado: EstadoPedido;
  ocurridoEn: string;
  motivo?: string | null;
}

interface LineaTiempoEstadosProps {
  /** Cambios de estado del pedido, en orden cronológico. */
  eventos: EventoLinea[];
  estadoActual: EstadoPedido;
  /** `vertical` para el detalle (con motivo); `compacta` para la tarjeta de "Hoy". */
  variante?: "vertical" | "compacta";
}

/** Camino feliz de un envío. Fallido/Reprogramado/Devuelto/Cancelado no están acá: si el pedido
 * termina en uno de ellos se agrega como último paso, en color de alerta. */
const CAMINO: EstadoPedido[] = ["Borrador", "Confirmado", "EnRuta", "Entregado"];

const ALERTA: Partial<Record<EstadoPedido, string>> = {
  Fallido: "bg-red-600 text-white",
  Reprogramado: "bg-amber-500 text-white",
  Devuelto: "bg-neutral-500 text-white",
  Cancelado: "bg-neutral-500 text-white",
};

interface Paso {
  estado: EstadoPedido;
  alcanzado: boolean;
  actual: boolean;
  ocurridoEn: string | null;
  motivo: string | null;
}

function armarPasos(eventos: EventoLinea[], estadoActual: EstadoPedido): Paso[] {
  const ultimo = (estado: EstadoPedido) => [...eventos].reverse().find((e) => e.estado === estado);

  const indiceActual = CAMINO.indexOf(estadoActual);
  // Fuera del camino feliz, "hasta dónde llegó" se saca de los eventos ya registrados.
  const indiceAlcanzado =
    indiceActual >= 0
      ? indiceActual
      : Math.max(-1, ...eventos.map((e) => CAMINO.indexOf(e.estado)));

  const pasos: Paso[] = CAMINO.map((estado, i) => {
    const evento = ultimo(estado);
    return {
      estado,
      alcanzado: i <= indiceAlcanzado,
      actual: i === indiceActual,
      ocurridoEn: evento?.ocurridoEn ?? null,
      motivo: null,
    };
  });

  if (indiceActual < 0) {
    const evento = ultimo(estadoActual);
    pasos.push({
      estado: estadoActual,
      alcanzado: true,
      actual: true,
      ocurridoEn: evento?.ocurridoEn ?? null,
      motivo: evento?.motivo ?? null,
    });
  }
  return pasos;
}

/** Hora si el cambio fue hoy; fecha corta + hora si fue otro día. */
function formatearMomento(iso: string): string {
  const d = new Date(iso);
  const hora = d.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
  return d.toDateString() === new Date().toDateString()
    ? hora
    : `${d.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit" })} ${hora}`;
}

function Punto({ paso }: { paso: Paso }) {
  const alerta = ALERTA[paso.estado];
  return (
    <span
      className={cn(
        "flex size-6 shrink-0 items-center justify-center rounded-full border-2 text-xs",
        alerta ?? (paso.alcanzado ? "border-bf-azul bg-bf-azul text-white" : "border-border bg-background text-muted-foreground"),
        alerta && "border-transparent",
        paso.actual && "ring-4 ring-bf-celeste/40",
      )}
      aria-hidden
    >
      {paso.alcanzado && <Check className="size-3.5" />}
    </span>
  );
}

export function LineaTiempoEstados({ eventos, estadoActual, variante = "vertical" }: LineaTiempoEstadosProps) {
  const pasos = armarPasos(eventos, estadoActual);

  if (variante === "compacta") {
    return (
      <ol className="flex items-start" aria-label="Estado del envío">
        {pasos.map((paso, i) => (
          <li key={paso.estado} className="flex flex-1 flex-col items-center gap-1 text-center">
            <div className="flex w-full items-center">
              <span className={cn("h-0.5 flex-1", i === 0 ? "bg-transparent" : paso.alcanzado ? "bg-bf-azul" : "bg-border")} />
              <Punto paso={paso} />
              <span className={cn("h-0.5 flex-1", i === pasos.length - 1 ? "bg-transparent" : pasos[i + 1].alcanzado ? "bg-bf-azul" : "bg-border")} />
            </div>
            <span className={cn("text-[11px] leading-tight", paso.actual ? "font-semibold text-foreground" : "text-muted-foreground")}>
              {etiquetaEstadoPedido(paso.estado)}
            </span>
            {paso.ocurridoEn && (
              <span className="text-[10px] leading-none text-muted-foreground">{formatearMomento(paso.ocurridoEn)}</span>
            )}
          </li>
        ))}
      </ol>
    );
  }

  return (
    <ol className="flex flex-col" aria-label="Estado del envío">
      {pasos.map((paso, i) => (
        <li key={paso.estado} className="flex gap-3">
          <div className="flex flex-col items-center">
            <Punto paso={paso} />
            {i < pasos.length - 1 && (
              <span className={cn("my-1 w-0.5 flex-1", pasos[i + 1].alcanzado ? "bg-bf-azul" : "bg-border")} />
            )}
          </div>
          <div className={cn("pb-4", i === pasos.length - 1 && "pb-0")}>
            <p className={cn("text-sm", paso.actual ? "font-semibold" : paso.alcanzado ? "font-medium" : "text-muted-foreground")}>
              {etiquetaEstadoPedido(paso.estado)}
            </p>
            {paso.ocurridoEn ? (
              <p className="text-xs text-muted-foreground">{formatearMomento(paso.ocurridoEn)}</p>
            ) : (
              !paso.alcanzado && <p className="text-xs text-muted-foreground">Pendiente</p>
            )}
            {paso.motivo && <p className="text-xs text-muted-foreground">{paso.motivo}</p>}
          </div>
        </li>
      ))}
    </ol>
  );
}
