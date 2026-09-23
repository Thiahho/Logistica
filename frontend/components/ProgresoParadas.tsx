/** Barra apilada verde/rojo sobre el total — extraída de app/hoy/page.tsx (el panel de progreso
 * del repartidor), para reusar en /jornada y en el detalle de ruta del back-office. */
export function ProgresoParadas({
  total,
  completadas,
  fallidas,
  canceladas = 0,
}: {
  total: number;
  completadas: number;
  fallidas: number;
  /** Paradas que operación canceló entera con la ruta en curso: resueltas, pero ni entregas ni fallos. */
  canceladas?: number;
}) {
  const pctCompletadas = total > 0 ? (completadas / total) * 100 : 0;
  const pctFallidas = total > 0 ? (fallidas / total) * 100 : 0;
  const pctCanceladas = total > 0 ? (canceladas / total) * 100 : 0;

  return (
    <div className="flex h-2.5 w-full overflow-hidden rounded-full bg-muted">
      {pctCompletadas > 0 && <div className="h-full bg-green-600" style={{ width: `${pctCompletadas}%` }} />}
      {pctFallidas > 0 && <div className="h-full bg-red-600" style={{ width: `${pctFallidas}%` }} />}
      {pctCanceladas > 0 && <div className="h-full bg-neutral-400" style={{ width: `${pctCanceladas}%` }} />}
    </div>
  );
}
