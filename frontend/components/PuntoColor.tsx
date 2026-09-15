/** Semáforo de cliente (colorPago/colorTrato/colorOper, RF-32/33) — extraído de
 * app/clientes/page.tsx para reusarlo también en /cobranza. "verde/amarillo/rojo son
 * indicadores de estado, no decoración" (mismo criterio que components/mapa/Mapa.tsx). */
const COLOR_CLASE: Record<string, string> = {
  verde: "bg-green-500",
  amarillo: "bg-yellow-500",
  rojo: "bg-red-500",
};

export function PuntoColor({ color }: { color: string }) {
  return (
    <span
      title={color}
      className={`inline-block size-2.5 rounded-full ${COLOR_CLASE[color] ?? "bg-muted"}`}
    />
  );
}
