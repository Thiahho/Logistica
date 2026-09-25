"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { HistorialRango, RangoDeCliente } from "@/lib/dominio/tipos";

const NOMBRES: Record<string, string> = {
  sin_rango: "Sin rango",
  bronce: "Bronce",
  plata: "Plata",
  oro: "Oro",
  empresa: "Empresa",
};
const nombre = (codigo: string | null) => (codigo ? (NOMBRES[codigo] ?? codigo) : "—");

/** Lo medido en un recálculo, en palabras. El backend lo guarda con claves en PascalCase. */
function criteriosLegibles(json: string | null): string | null {
  if (!json) return null;
  try {
    const c = JSON.parse(json) as Record<string, number>;
    return `${c.Envios} envíos, $${Number(c.Facturacion).toLocaleString("es-AR")} facturados, ${c.SemanasActivas} semanas activas, ${c.AntiguedadMeses} meses de antigüedad, ${c.PctPagosEnTermino}% de pagos en término`;
  } catch {
    return null;
  }
}

/**
 * B3 (acta RF-42): rango del cliente — el calculado en el último recálculo trimestral y el efectivo con
 * el ajuste manual de la definición F (±1 rango, con motivo y vencimiento). Historial de cada cambio:
 * es la explicación de por qué cambió el precio de un cliente.
 */
export function RangoCliente({ clienteId }: { clienteId: number }) {
  const { fetchConSesion } = useAuth();
  const [rango, setRango] = useState<RangoDeCliente | null>(null);
  const [ajuste, setAjuste] = useState<"1" | "-1">("1");
  const [motivo, setMotivo] = useState("");
  const [vence, setVence] = useState("");
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/clientes/${clienteId}/rango`)
      .then((r) => leerJson<RangoDeCliente>(r))
      .then(setRango)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el rango."));
  }, [fetchConSesion, clienteId]);

  useEffect(cargar, [cargar]);

  async function enviar(cuerpo: { ajuste: number; motivo?: string; vence?: string }) {
    setError(null);
    setGuardando(true);
    try {
      const r = await fetchConSesion(`/api/clientes/${clienteId}/rango/ajuste`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cuerpo),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setMotivo("");
      setVence("");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el ajuste.");
    } finally {
      setGuardando(false);
    }
  }

  if (!rango) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Rango</CardTitle>
        </CardHeader>
        <CardContent>
          <p className={error ? "text-sm text-destructive" : "text-sm text-muted-foreground"}>{error ?? "Cargando…"}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">
          Rango: {nombre(rango.efectivo)}
          {rango.descuentoPct > 0 && (
            <span className="ml-2 text-sm font-normal text-muted-foreground">{rango.descuentoPct}% de descuento</span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-sm">
        <p className="text-muted-foreground">
          Calculado: <span className="text-foreground">{nombre(rango.calculado)}</span>
          {rango.calculadoEn
            ? ` (recálculo del ${new Date(rango.calculadoEn).toLocaleDateString("es-AR")})`
            : " (todavía sin recálculo)"}
          {rango.limiteCredito !== null && ` · Límite de crédito $${rango.limiteCredito.toLocaleString("es-AR")}`}
        </p>

        {rango.ajusteVigente ? (
          <div className="flex flex-col gap-2 rounded-lg border p-3">
            <p>
              Ajuste manual {rango.ajuste > 0 ? "+1" : "−1"} hasta el {rango.ajusteVence}: {rango.ajusteMotivo}
            </p>
            <Button size="sm" variant="outline" className="self-start" disabled={guardando} onClick={() => enviar({ ajuste: 0 })}>
              Quitar ajuste
            </Button>
          </div>
        ) : (
          <form
            className="flex flex-col gap-3 rounded-lg border p-3"
            onSubmit={(e) => {
              e.preventDefault();
              enviar({ ajuste: Number(ajuste), motivo, vence });
            }}
          >
            <p className="font-medium">Ajuste manual (trato)</p>
            <p className="text-xs text-muted-foreground">
              Mueve un rango hacia arriba o hacia abajo hasta la fecha que indiques. Queda registrado con el motivo.
            </p>
            <div className="flex flex-wrap gap-4">
              <label className="flex items-center gap-2">
                <input type="radio" name="ajuste" checked={ajuste === "1"} onChange={() => setAjuste("1")} />
                Subir un rango
              </label>
              <label className="flex items-center gap-2">
                <input type="radio" name="ajuste" checked={ajuste === "-1"} onChange={() => setAjuste("-1")} />
                Bajar un rango
              </label>
            </div>
            <div className="grid gap-3 sm:grid-cols-[1fr_auto]">
              <div className="flex flex-col gap-2">
                <Label htmlFor="motivo-rango">Motivo</Label>
                <Input id="motivo-rango" required maxLength={2000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="vence-rango">Vence</Label>
                <Input id="vence-rango" type="date" required value={vence} onChange={(e) => setVence(e.target.value)} />
              </div>
            </div>
            <Button type="submit" size="sm" className="self-start" disabled={guardando}>
              Aplicar ajuste
            </Button>
          </form>
        )}
        {error && <p className="text-destructive">{error}</p>}

        {rango.historial.length > 0 && (
          <details>
            <summary className="cursor-pointer font-medium">Historial ({rango.historial.length})</summary>
            <ul className="mt-2 flex flex-col gap-2">
              {rango.historial.map((h: HistorialRango, i) => (
                <li key={i} className="border-b pb-2">
                  <span className="font-medium">
                    {nombre(h.rangoAnterior)} → {nombre(h.rangoNuevo)}
                  </span>
                  <span className="text-muted-foreground">
                    {" "}
                    · {h.origen === "recalculo" ? `recálculo ${h.trimestre}` : "ajuste manual"} ·{" "}
                    {new Date(h.registradoEn).toLocaleDateString("es-AR")} · {h.registradoPor}
                  </span>
                  {criteriosLegibles(h.criterios) && (
                    <span className="block text-xs text-muted-foreground">{criteriosLegibles(h.criterios)}</span>
                  )}
                  {h.motivo && <span className="block text-xs">{h.motivo}</span>}
                </li>
              ))}
            </ul>
          </details>
        )}
      </CardContent>
    </Card>
  );
}
