import type { ReactNode } from "react";

interface TarjetaMetricaProps {
  valor: ReactNode;
  etiqueta?: ReactNode;
  tono?: "normal" | "alerta";
  /** Texto del valor más chico — para un estado ("Al día"/"Servicio cortado") en vez de un
   * número grande. */
  chico?: boolean;
}

/** Stat tile: valor grande + etiqueta chica, con tono de alerta opcional. Estaba triplicado
 * (app/clientes/[id]/page.tsx: cuenta corriente y contador de eventos; app/mis-envios/page.tsx)
 * — extraído acá, el panel de cobranza (/cobranza) suma el cuarto uso. */
export function TarjetaMetrica({ valor, etiqueta, tono = "normal", chico = false }: TarjetaMetricaProps) {
  return (
    <div className="rounded-lg border p-3 text-center">
      <p className={`${chico ? "text-sm" : "text-2xl"} font-semibold ${tono === "alerta" ? "text-destructive" : ""}`}>
        {valor}
      </p>
      {etiqueta && <p className="text-xs text-muted-foreground">{etiqueta}</p>}
    </div>
  );
}
