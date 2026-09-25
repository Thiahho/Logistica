import Link from "next/link";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { RutaLiquidable } from "@/lib/dominio/tipos";

export const pesos = (n: number) => `$${n.toLocaleString("es-AR")}`;

/** Rutas de una liquidación (o candidatas a entrar): el desglose calculado al cerrar cada una y lo que
 * se pagó. Una ruta sin desglose se pagó a mano (no había parámetros vigentes). */
export function TablaRutasLiquidadas({ rutas, total }: { rutas: RutaLiquidable[]; total: number }) {
  return (
    <div className="flex flex-col gap-2">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Ruta</TableHead>
            <TableHead>Fecha</TableHead>
            <TableHead>Vehículo</TableHead>
            <TableHead className="text-right">Entregas</TableHead>
            <TableHead className="text-right">Éxito</TableHead>
            <TableHead className="text-right">Entregas $</TableHead>
            <TableHead className="text-right">Bono</TableHead>
            <TableHead className="text-right">Pago</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rutas.map((r) => (
            <TableRow key={r.id}>
              <TableCell>
                <Link className="underline" href={`/rutas/${r.id}/cierre`}>
                  #{r.id}
                </Link>
              </TableCell>
              <TableCell>{r.fecha}</TableCell>
              <TableCell>{r.vehiculoPatente ?? "—"}</TableCell>
              <TableCell className="text-right">{r.entregas ?? "—"}</TableCell>
              <TableCell className="text-right">{r.pctExito !== null ? `${r.pctExito}%` : "—"}</TableCell>
              <TableCell className="text-right">{r.pagoEntregas !== null ? pesos(r.pagoEntregas) : "a mano"}</TableCell>
              <TableCell className="text-right">{r.bono !== null ? pesos(r.bono) : "—"}</TableCell>
              <TableCell className="text-right">
                {pesos(r.pagoRepartidor)}
                {r.pagoAjusteMotivo && (
                  <span className="block text-xs text-muted-foreground">ajustado: {r.pagoAjusteMotivo}</span>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {/* Fuera de la tabla: en pantalla chica cada fila se vuelve tarjeta y una fila de total quedaba
          como una tarjeta más, con rótulos que no le corresponden. */}
      <div className="flex justify-end gap-4 px-2 font-semibold">
        <span>Total</span>
        <span>{pesos(total)}</span>
      </div>
    </div>
  );
}
