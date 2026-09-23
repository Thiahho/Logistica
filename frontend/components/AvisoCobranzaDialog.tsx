"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaCategoriaRiesgo,
  type PrevisualizacionAvisos,
  type ResultadoAvisoCliente,
} from "@/lib/dominio/tipos";

interface AvisoCobranzaDialogProps {
  /** null = cerrado. No-null = abierto, con esa selección de clientes. */
  clienteIds: number[] | null;
  onOpenChange: (open: boolean) => void;
  onEnviado?: () => void;
}

/**
 * Envío de avisos de cobranza — mismo molde que CierreCicloDialog (app/facturas/page.tsx):
 * previsualizar → confirmar → resultado. El asunto/mensaje de cada aviso los arma el backend
 * (nunca este componente), así lo que se previsualiza es exactamente lo que se manda.
 *
 * `AvisoCobranzaContenido` se monta fresco cada vez que `clienteIds` deja de ser null (el
 * `Dialog` de afuera siempre está montado, el contenido no) — mismo patrón que
 * FacturaDetalleContenido/PedidoDetalleContenido: todo el estado local arranca limpio con cada
 * apertura, sin necesitar un efecto de reset aparte que observe `open`.
 */
export function AvisoCobranzaDialog({ clienteIds, onOpenChange, onEnviado }: AvisoCobranzaDialogProps) {
  return (
    <Dialog open={clienteIds !== null} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Enviar aviso de cobranza</DialogTitle>
        </DialogHeader>
        {clienteIds !== null && <AvisoCobranzaContenido clienteIds={clienteIds} onEnviado={onEnviado} />}
      </DialogContent>
    </Dialog>
  );
}

function AvisoCobranzaContenido({ clienteIds, onEnviado }: { clienteIds: number[]; onEnviado?: () => void }) {
  const { fetchConSesion } = useAuth();
  const [previsualizacion, setPrevisualizacion] = useState<PrevisualizacionAvisos | null>(null);
  const [enviarEmail, setEnviarEmail] = useState(true);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<ResultadoAvisoCliente[] | null>(null);
  const [abiertos, setAbiertos] = useState<Set<number>>(new Set());

  // Sin setState síncrono en el cuerpo del efecto (react-hooks/set-state-in-effect): "cargando"
  // se deriva de !previsualizacion && !error, igual que FacturaDetalleContenido/
  // PedidoDetalleContenido — todo el setState real queda dentro de then/catch, async.
  const cargar = useCallback(() => {
    fetchConSesion("/api/clientes/avisos/previsualizacion", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clienteIds }),
    })
      .then((r) => leerJson<PrevisualizacionAvisos>(r))
      .then((p) => {
        setPrevisualizacion(p);
        if (!p.resendConfigurado) setEnviarEmail(false);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo previsualizar el aviso."));
  }, [fetchConSesion, clienteIds]);

  useEffect(cargar, [cargar]);

  async function confirmar() {
    setEnviando(true);
    setError(null);
    try {
      const resp = await fetchConSesion("/api/clientes/avisos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clienteIds, enviarEmail }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setResultado(await leerJson<ResultadoAvisoCliente[]>(resp));
      setPrevisualizacion(null);
      onEnviado?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron enviar los avisos.");
    } finally {
      setEnviando(false);
    }
  }

  // resultado ya seteado (después de confirmar) también cuenta como "no está cargando" —
  // confirmar() vacía previsualizacion al mismo tiempo que llena resultado.
  if (!previsualizacion && !error && !resultado) return <p className="text-muted-foreground">Cargando…</p>;

  const conAviso = (previsualizacion?.avisos ?? []).filter((a) => a.omitido === null);
  const omitidos = (previsualizacion?.avisos ?? []).filter((a) => a.omitido !== null);
  const conTelefono = (resultado ?? []).filter((r) => r.linkWhatsApp !== null);

  return (
    <div className="flex flex-col gap-4">
      {error && <p className="text-sm text-destructive">{error}</p>}

      {previsualizacion && !resultado && (
        <>
          <p className="text-sm text-muted-foreground">
            Se va a mandar el siguiente aviso a {conAviso.length} cliente{conAviso.length === 1 ? "" : "s"}.
          </p>

          <label className="flex flex-wrap items-center gap-2 text-sm">
            <Checkbox
              checked={enviarEmail}
              onCheckedChange={(v) => setEnviarEmail(v)}
              disabled={!previsualizacion.resendConfigurado}
            />
            Enviar también por email
            {!previsualizacion.resendConfigurado && (
              <span className="text-xs text-amber-600">
                (Resend sin configurar — el email sale simulado, no se manda de verdad)
              </span>
            )}
          </label>

          {conAviso.length === 0 ? (
            <p className="text-sm text-muted-foreground">Ningún cliente seleccionado tiene un aviso pendiente.</p>
          ) : (
            <ul className="flex flex-col gap-2 max-h-80 overflow-y-auto">
              {conAviso.map((a) => (
                <li key={a.clienteId} className="text-sm border-b pb-2">
                  <div className="flex justify-between gap-4">
                    <span className="font-medium">{a.razonSocial}</span>
                    <span className={a.categoria === "vencido" ? "text-destructive" : "text-amber-600"}>
                      {etiquetaCategoriaRiesgo(a.categoria)} — ${a.monto.toLocaleString("es-AR")}
                    </span>
                  </div>
                  <p className="text-xs text-muted-foreground mt-1">{a.asunto}</p>
                  <p className="text-xs text-muted-foreground">
                    {a.email ?? "sin email"} · {a.telefonoNormalizado ? `+${a.telefonoNormalizado}` : "sin teléfono"}
                  </p>
                </li>
              ))}
            </ul>
          )}

          {omitidos.length > 0 && (
            <p className="text-xs text-muted-foreground">
              Sin aviso: {omitidos.map((o) => `${o.razonSocial || `#${o.clienteId}`} (${o.omitido})`).join(", ")}.
            </p>
          )}

          {conAviso.length > 0 && (
            <Button onClick={confirmar} disabled={enviando}>
              {enviando ? "Enviando…" : `Enviar aviso a ${conAviso.length} cliente${conAviso.length === 1 ? "" : "s"}`}
            </Button>
          )}
        </>
      )}

      {resultado && (
        <div className="flex flex-col gap-3">
          <ul className="flex flex-col gap-2 max-h-80 overflow-y-auto">
            {resultado.map((r) => (
              <li key={r.clienteId} className="text-sm border-b pb-2 flex flex-col gap-1">
                <div className="flex justify-between gap-4">
                  <span className="font-medium">{r.razonSocial}</span>
                  {r.omitido ? (
                    <span className="text-xs text-muted-foreground">{r.omitido}</span>
                  ) : r.emailEnviado ? (
                    <span className="text-green-700">Email enviado</span>
                  ) : r.emailError ? (
                    <span className="text-destructive">{r.emailError}</span>
                  ) : r.emailSimulado ? (
                    <span className="text-amber-600">Email simulado (Resend sin configurar)</span>
                  ) : (
                    <span className="text-xs text-muted-foreground">Sin email</span>
                  )}
                </div>
                {r.linkWhatsApp && (
                  // Uno por uno, nunca todos a la vez: el navegador bloquea múltiples window.open
                  // programáticos, un link wa.me necesita un gesto real del usuario.
                  <Button
                    size="sm"
                    variant="outline"
                    className="self-start"
                    nativeButton={false}
                    render={
                      <a
                        href={r.linkWhatsApp}
                        target="_blank"
                        rel="noopener noreferrer"
                        onClick={() => setAbiertos((prev) => new Set(prev).add(r.clienteId))}
                      />
                    }
                  >
                    {abiertos.has(r.clienteId) ? "WhatsApp abierto ✓" : "Abrir WhatsApp"}
                  </Button>
                )}
              </li>
            ))}
          </ul>
          {conTelefono.length > 0 && (
            <p className="text-xs text-muted-foreground">
              {abiertos.size} de {conTelefono.length} WhatsApp abiertos — cada uno se abre con un click, el
              navegador bloquea abrirlos todos de una.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
