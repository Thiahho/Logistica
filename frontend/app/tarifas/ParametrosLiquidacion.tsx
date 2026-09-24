"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ParametroLiquidacion, ParametrosLiquidacionResponse } from "@/lib/dominio/tipos";

const TIPOS = [
  { tipo: "camioneta", etiqueta: "Auto" },
  { tipo: "moto", etiqueta: "Moto" },
] as const;

const MOTIVOS_POR_DEFECTO = ["otro", "zona_inaccesible"];

interface Borrador {
  pagoPorEntrega: string;
  bonoRuta: string;
  pctMinimoExitosas: string;
  motivosImputables: string[];
}

function borradorDe(p: ParametroLiquidacion | undefined): Borrador {
  return p
    ? {
        pagoPorEntrega: String(p.pagoPorEntrega),
        bonoRuta: String(p.bonoRuta),
        pctMinimoExitosas: String(p.pctMinimoExitosas),
        motivosImputables: p.motivosImputables,
      }
    : // Sin valores de fábrica para los importes ni el porcentaje (acta §13); solo la lista de motivos
      // arranca con los que el diseño propone como imputables.
      { pagoPorEntrega: "", bonoRuta: "", pctMinimoExitosas: "", motivosImputables: MOTIVOS_POR_DEFECTO };
}

/**
 * B4 (acta RF-41, definición C del Anexo I): valores con que se calcula el pago al repartidor al cerrar
 * cada ruta. Configuración de la Empresa — sin fila vigente para un tipo de vehículo, el pago de sus
 * rutas se tipea a mano en el cierre, como antes.
 */
export function ParametrosLiquidacion() {
  const { fetchConSesion } = useAuth();
  const [datos, setDatos] = useState<ParametrosLiquidacionResponse | null>(null);
  const [borradores, setBorradores] = useState<Record<string, Borrador>>({});
  const [guardando, setGuardando] = useState<string | null>(null);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion("/api/parametros-liquidacion")
      .then((r) => leerJson<ParametrosLiquidacionResponse>(r))
      .then((d) => {
        setDatos(d);
        setBorradores(
          Object.fromEntries(TIPOS.map(({ tipo }) => [tipo, borradorDe(d.parametros.find((p) => p.tipoVehiculo === tipo))])),
        );
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los parámetros."));
  }, [fetchConSesion]);

  useEffect(cargar, [cargar]);

  function cambiar(tipo: string, cambios: Partial<Borrador>) {
    setBorradores((b) => ({ ...b, [tipo]: { ...b[tipo], ...cambios } }));
  }

  async function guardar(tipo: string) {
    const b = borradores[tipo];
    setError(null);
    setMensaje(null);
    setGuardando(tipo);
    try {
      const r = await fetchConSesion(`/api/parametros-liquidacion/${tipo}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          pagoPorEntrega: Number(b.pagoPorEntrega),
          bonoRuta: Number(b.bonoRuta),
          pctMinimoExitosas: Number(b.pctMinimoExitosas),
          motivosImputables: b.motivosImputables,
        }),
      });
      if (!r.ok) throw new Error((await leerError(r)).mensaje);
      setMensaje("Guardado. Rige desde hoy para las rutas que se cierren; las ya cerradas no cambian.");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar.");
    } finally {
      setGuardando(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Pago al repartidor</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <p className="text-sm text-muted-foreground">
          Al cerrar una ruta se paga cada parada entregada por el valor de su tipo de vehículo, más el bono si el
          porcentaje de éxito llega al mínimo. Los fallos con un motivo que no depende del repartidor no cuentan en
          su contra.
        </p>
        {!datos ? (
          <p className="text-sm text-muted-foreground">{error ?? "Cargando…"}</p>
        ) : (
          TIPOS.map(({ tipo, etiqueta }) => {
            const b = borradores[tipo];
            const vigente = datos.parametros.find((p) => p.tipoVehiculo === tipo);
            if (!b) return null;
            const completo = b.pagoPorEntrega !== "" && b.bonoRuta !== "" && b.pctMinimoExitosas !== "";
            return (
              <fieldset key={tipo} className="flex flex-col gap-3 border-t pt-4 first:border-t-0 first:pt-0">
                <legend className="font-medium">
                  {etiqueta}{" "}
                  <span className="text-xs font-normal text-muted-foreground">
                    {vigente ? `vigente desde ${vigente.vigenteDesde}` : "sin cargar: el pago se tipea a mano"}
                  </span>
                </legend>
                <div className="grid gap-3 sm:grid-cols-3">
                  <div className="flex flex-col gap-2">
                    <Label htmlFor={`pago-${tipo}`}>Pago por entrega</Label>
                    <Input
                      id={`pago-${tipo}`}
                      type="number"
                      min="0"
                      step="0.01"
                      value={b.pagoPorEntrega}
                      onChange={(e) => cambiar(tipo, { pagoPorEntrega: e.target.value })}
                    />
                  </div>
                  <div className="flex flex-col gap-2">
                    <Label htmlFor={`bono-${tipo}`}>Bono por ruta</Label>
                    <Input
                      id={`bono-${tipo}`}
                      type="number"
                      min="0"
                      step="0.01"
                      value={b.bonoRuta}
                      onChange={(e) => cambiar(tipo, { bonoRuta: e.target.value })}
                    />
                  </div>
                  <div className="flex flex-col gap-2">
                    <Label htmlFor={`pct-${tipo}`}>Mínimo de éxito para el bono (%)</Label>
                    <Input
                      id={`pct-${tipo}`}
                      type="number"
                      min="0"
                      max="100"
                      step="0.01"
                      value={b.pctMinimoExitosas}
                      onChange={(e) => cambiar(tipo, { pctMinimoExitosas: e.target.value })}
                    />
                  </div>
                </div>
                <div className="flex flex-col gap-2">
                  <span className="text-sm">Motivos de fallo que cuentan contra el repartidor</span>
                  <div className="flex flex-wrap gap-x-4 gap-y-2">
                    {datos.motivosFallo.map((m) => (
                      <label key={m} className="flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          checked={b.motivosImputables.includes(m)}
                          onChange={(e) =>
                            cambiar(tipo, {
                              motivosImputables: e.target.checked
                                ? [...b.motivosImputables, m]
                                : b.motivosImputables.filter((x) => x !== m),
                            })
                          }
                        />
                        {m.replaceAll("_", " ")}
                      </label>
                    ))}
                  </div>
                </div>
                <Button
                  size="sm"
                  className="self-start"
                  disabled={!completo || guardando === tipo}
                  onClick={() => guardar(tipo)}
                >
                  {guardando === tipo ? "Guardando…" : `Guardar ${etiqueta.toLowerCase()}`}
                </Button>
              </fieldset>
            );
          })
        )}
        {mensaje && <p className="text-sm text-muted-foreground">{mensaje}</p>}
        {datos && error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  );
}
