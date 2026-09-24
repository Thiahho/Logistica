"use client";

import { useEffect, useMemo, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { leerError, leerJson } from "@/lib/api/errores";
import type { CandidatoRuta, UrgenciaInsertada, VentanaUrgencias } from "@/lib/dominio/tipos";

/** Espejo de RutasController.ParadaArmada, lo que hace falta para ubicar la urgencia. */
interface ParadaArmada {
  id: number;
  ubicacionId: number;
  calleNumero: string;
  orden: number;
  estado: string;
}

const AL_FINAL = "final";
const hhmm = (t: string) => t.slice(0, 5);

/**
 * Urgencia en una ruta en curso (acta RF-45, §7). Solo dentro de la ventana de reagrupamiento y si
 * desplaza como mucho `maxParadasDesplazadas` pendientes; el servidor vuelve a validar las dos cosas. Si el
 * destino ya es una parada pendiente, el pedido se suma ahí y no desplaza a nadie (RF-14). Siempre con
 * recargo de urgencia: el precio se congela al agregarla, con el vehículo de la ruta.
 */
export function DialogoUrgencia({
  rutaId,
  fecha,
  ventana,
  onCerrar,
  onListo,
}: {
  rutaId: number;
  fecha: string;
  ventana: VentanaUrgencias;
  onCerrar: () => void;
  onListo: (r: UrgenciaInsertada) => void;
}) {
  const { fetchConSesion } = useAuth();
  const [candidatos, setCandidatos] = useState<CandidatoRuta[] | null>(null);
  const [paradas, setParadas] = useState<ParadaArmada[]>([]);
  const [pedidoId, setPedidoId] = useState<string | null>(null);
  const [posicion, setPosicion] = useState(AL_FINAL);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      fetchConSesion(`/api/pedidos/candidatos-ruta?fecha=${fecha}`).then((r) => leerJson<CandidatoRuta[]>(r)),
      fetchConSesion(`/api/rutas/${rutaId}/paradas`).then((r) => leerJson<ParadaArmada[]>(r)),
    ])
      .then(([c, p]) => {
        // Solo lo que se puede rutear: una dirección dudosa la frena el servidor igual (RF-05).
        setCandidatos(c.filter((x) => x.direccionApta && !x.yaEnEstaRuta));
        setParadas(p);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los pedidos."));
  }, [fetchConSesion, fecha, rutaId]);

  const pendientes = useMemo(() => paradas.filter((p) => p.estado === "pendiente").sort((a, b) => a.orden - b.orden), [paradas]);
  const candidato = candidatos?.find((c) => String(c.pedidoId) === pedidoId) ?? null;
  const consolidaEn = candidato ? pendientes.find((p) => p.ubicacionId === candidato.destinoUbicacionId) : undefined;
  const antes = pendientes.find((p) => String(p.id) === posicion);
  const desplazadas = consolidaEn || !antes ? 0 : pendientes.filter((p) => p.orden >= antes.orden).length;
  const excede = desplazadas > ventana.maxParadasDesplazadas;

  const items = useMemo(
    () =>
      (candidatos ?? []).map((c) => ({
        value: String(c.pedidoId),
        label: `#${c.pedidoId} · ${c.clienteRazonSocial} · ${c.destinatarioNombre} — ${c.destinoCalleNumero}`,
      })),
    [candidatos],
  );

  async function confirmar() {
    if (!candidato) return;
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion(`/api/rutas/${rutaId}/urgencias`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ pedidoId: candidato.pedidoId, antesDeParadaId: antes && !consolidaEn ? antes.id : null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onListo(await resp.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo agregar la urgencia.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Agregar urgencia</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-4 pt-2 text-sm">
          <p className="text-muted-foreground">
            Ventana de urgencias: {hhmm(ventana.desde)} a {hhmm(ventana.hasta)}. Puede desplazar hasta{" "}
            {ventana.maxParadasDesplazadas} paradas pendientes y siempre lleva recargo de urgencia.
          </p>

          <div className="flex flex-col gap-2">
            <Label>Pedido</Label>
            {candidatos === null ? (
              <p className="text-muted-foreground">Cargando…</p>
            ) : candidatos.length === 0 ? (
              <p className="text-muted-foreground">No hay pedidos en borrador de {fecha} con dirección apta.</p>
            ) : (
              <ComboboxBusqueda items={items} value={pedidoId} onValueChange={setPedidoId} placeholder="Elegir pedido" />
            )}
          </div>

          {candidato &&
            (consolidaEn ? (
              <p className="rounded-lg border bg-muted/50 p-3">
                El destino ya es la parada {consolidaEn.orden} ({consolidaEn.calleNumero}): el pedido se suma ahí y no
                desplaza ninguna parada.
              </p>
            ) : (
              <div className="flex flex-col gap-2">
                <Label htmlFor="posicion-urgencia">Dónde entra</Label>
                <select
                  id="posicion-urgencia"
                  className="h-11 rounded-md border bg-background px-3 md:h-9"
                  value={posicion}
                  onChange={(e) => setPosicion(e.target.value)}
                >
                  <option value={AL_FINAL}>Al final de la ruta (no desplaza ninguna)</option>
                  {pendientes.map((p) => (
                    <option key={p.id} value={String(p.id)}>
                      Antes de la parada {p.orden} — {p.calleNumero}
                    </option>
                  ))}
                </select>
                <p className={excede ? "font-medium text-destructive" : "text-muted-foreground"}>
                  Desplaza {desplazadas} parada{desplazadas === 1 ? "" : "s"} pendiente{desplazadas === 1 ? "" : "s"}
                  {excede && ` — el máximo es ${ventana.maxParadasDesplazadas}. Elegí un lugar más adelante.`}
                </p>
              </div>
            ))}

          {error && <p className="text-destructive">{error}</p>}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="outline" className="h-11 sm:h-8" onClick={onCerrar} disabled={enviando}>
              Cancelar
            </Button>
            <Button className="h-11 sm:h-8" onClick={confirmar} disabled={!candidato || excede || enviando}>
              {enviando ? "Agregando…" : "Agregar urgencia"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
