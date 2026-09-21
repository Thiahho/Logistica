"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import {
  CheckCircle2,

  Clock,
  Navigation,
  Package,
  Pencil,
  Phone,
  XCircle,
} from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraRepartidor } from "@/components/CabeceraRepartidor";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { leerError, leerJson } from "@/lib/api/errores";
import { comprimirFoto } from "@/lib/captura/foto";
import { limpiarUuidDeCaptura, uuidDeCaptura } from "@/lib/captura/dispositivo";
import { EstadoParadaBadge, etiquetaEstadoParada } from "@/components/EstadoBadge";
import {
  ETIQUETA_CAMPO_EDITABLE,
  type CierreResultado,
  type JornadaDelDia,
  type NovedadResultado,
  type ParadaDelDia,
} from "@/lib/dominio/tipos";

export default function ParadaPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <ParadaDetalle />
    </RequireRole>
  );
}

type Modo = "detalle" | "entregado" | "fallido" | "corregir";

/** Timeout corto a propósito (RF-29): nunca vale la pena bloquear el cierre esperando un fix
 * de GPS. Sin posición, el servidor no calcula desvío y sigue andando igual. */
function obtenerPosicion(): Promise<{ lat: number; lng: number } | null> {
  return new Promise((resolve) => {
    if (!("geolocation" in navigator)) {
      resolve(null);
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
      () => resolve(null),
      { timeout: 5000 },
    );
  });
}

function ParadaDetalle() {
  const { paradaId } = useParams<{ paradaId: string }>();
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [jornada, setJornada] = useState<JornadaDelDia | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  const [modo, setModo] = useState<Modo>("detalle");
  const [llegadaEn, setLlegadaEn] = useState<string | null>(null);
  const [registrandoLlegada, setRegistrandoLlegada] = useState(false);

  const [receptorNombre, setReceptorNombre] = useState("");
  const [identidadVerificada, setIdentidadVerificada] = useState(false);
  const [foto, setFoto] = useState<File | null>(null);

  const [enviando, setEnviando] = useState(false);
  const [envioError, setEnvioError] = useState<string | null>(null);

  // "Corregir un dato" (RF-36): propone un cambio, no lo hace. El pedido no cambia hasta que operación
  // lo acepta. El uuid nace al abrir el formulario, no al enviar, por la misma razón que el del cierre.
  const [corregirPedidoId, setCorregirPedidoId] = useState<number | null>(null);
  const [corregirCampo, setCorregirCampo] = useState("destinatario_telefono");
  const [corregirValor, setCorregirValor] = useState("");
  const [corregirUuid, setCorregirUuid] = useState("");
  const [correccionEnviada, setCorreccionEnviada] = useState(false);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas/dia")
      .then((r) => leerJson<JornadaDelDia>(r))
      .then((j) => {
        setJornada(j);
        const p = j.paradas.find((x) => x.paradaId === Number(paradaId));
        setLlegadaEn(p?.llegadaEn ?? null);
      })
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la parada."));
  }, [fetchConSesion, paradaId]);

  // createObjectURL es puramente derivable de `foto` (mismo valor mientras no cambie el
  // archivo), así que se computa en el render vía useMemo en vez de en un efecto — evita
  // set-state-in-effect por completo. El efecto de abajo solo se ocupa de la limpieza
  // (revocar), que no es un setState y no dispara esa regla.
  const previewFoto = useMemo(() => (foto ? URL.createObjectURL(foto) : null), [foto]);

  useEffect(() => {
    if (!previewFoto) return;
    return () => URL.revokeObjectURL(previewFoto);
  }, [previewFoto]);

  const parada: ParadaDelDia | undefined = jornada?.paradas.find((p) => p.paradaId === Number(paradaId));

  async function onLlegue() {
    setRegistrandoLlegada(true);
    setEnvioError(null);
    try {
      const resp = await fetchConSesion(`/api/mis-paradas/${paradaId}/llegada`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ llegadaEn: new Date().toISOString(), deviceUuid: uuidDeCaptura(Number(paradaId)) }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const { llegadaEn: nuevaLlegada } = await resp.json();
      setLlegadaEn(nuevaLlegada);
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo registrar la llegada.");
    } finally {
      setRegistrandoLlegada(false);
    }
  }

  async function cerrar(resultado: "entregado" | "fallido", motivoFallo?: string) {
    setEnviando(true);
    setEnvioError(null);
    try {
      const posicion = await obtenerPosicion();
      const form = new FormData();
      form.append("deviceUuid", uuidDeCaptura(Number(paradaId)));
      form.append("resultado", resultado);
      form.append("capturadaEn", new Date().toISOString());
      if (llegadaEn) form.append("llegadaEn", llegadaEn);
      if (posicion) {
        form.append("lat", String(posicion.lat));
        form.append("lng", String(posicion.lng));
      }
      if (resultado === "entregado") {
        if (!foto) throw new Error("La entrega necesita una foto.");
        if (!receptorNombre.trim()) throw new Error("La entrega necesita el nombre del receptor.");
        form.append("receptorNombre", receptorNombre);
        form.append("identidadVerificada", String(identidadVerificada));
        form.append("foto", await comprimirFoto(foto), "entrega.jpg");
      } else {
        form.append("motivoFallo", motivoFallo!);
      }

      const resp = await fetchConSesion(`/api/mis-paradas/${paradaId}/cierre`, { method: "POST", body: form });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const cierre = await leerJson<CierreResultado>(resp);
      limpiarUuidDeCaptura(Number(paradaId));
      // replace, no push: atrás desde la parada siguiente no debe volver a una ya cerrada, que
      // solo puede mostrar "ya quedó completada".
      router.replace(cierre.siguienteParadaId ? `/hoy/parada/${cierre.siguienteParadaId}` : "/hoy");
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo cerrar la parada.");
    } finally {
      setEnviando(false);
    }
  }

  function abrirCorreccion() {
    const vivos = parada?.pedidos.filter((p) => p.estado !== "Cancelado") ?? [];
    setCorregirPedidoId(vivos[0]?.pedidoId ?? null);
    setCorregirCampo("destinatario_telefono");
    setCorregirValor("");
    setCorregirUuid(crypto.randomUUID());
    setCorreccionEnviada(false);
    setEnvioError(null);
    setModo("corregir");
  }

  async function enviarCorreccion() {
    setEnviando(true);
    setEnvioError(null);
    try {
      const form = new FormData();
      form.append("deviceUuid", corregirUuid);
      form.append("tipo", "cambio_propuesto");
      form.append("pedidoId", String(corregirPedidoId));
      form.append("campo", corregirCampo);
      form.append("valorNuevo", corregirValor.trim());

      const resp = await fetchConSesion("/api/mi-jornada/novedades", { method: "POST", body: form });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      await leerJson<NovedadResultado>(resp);
      setCorreccionEnviada(true);
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo enviar la corrección.");
    } finally {
      setEnviando(false);
    }
  }

  if (errorCarga) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Parada" />
        <p className="text-sm text-destructive">{errorCarga}</p>
      </div>
    );
  }

  if (!jornada || !parada) {
    return (
      <div className="p-4">
        <CabeceraRepartidor titulo="Parada" />
        <p className="text-muted-foreground">{jornada ? "Parada no encontrada." : "Cargando…"}</p>
      </div>
    );
  }

  const tieneCoordenadas = parada.lat !== null && parada.lng !== null;
  // Un pedido que operación canceló con la ruta en curso no se entrega ni se llama: se muestra tachado.
  const pedidosVivos = parada.pedidos.filter((p) => p.estado !== "Cancelado");

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <CabeceraRepartidor titulo="Parada" volverA="/hoy" />

      <div className="rounded-2xl border bg-card p-4 flex flex-col gap-3 shadow-sm">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-bf-azul text-sm font-bold text-white">
            {parada.orden}
          </span>
          <div className="min-w-0 flex-1">
            <h2 className="text-lg font-semibold">{parada.calleNumero}</h2>
            <p className="text-muted-foreground">
              {parada.localidad}
              {parada.referencia ? ` · ${parada.referencia}` : ""}
            </p>
          </div>
          <EstadoParadaBadge estado={parada.estado} size="md" />
        </div>
        {tieneCoordenadas && (
          <Button
            variant="outline"
            className="h-11 w-full text-base"
            render={
              <a
                href={`https://www.google.com/maps/dir/?api=1&destination=${parada.lat},${parada.lng}&travelmode=driving`}
                target="_blank"
                rel="noopener noreferrer"
              />
            }
            nativeButton={false}
          >
            <Navigation className="size-4" />
            Cómo llegar
          </Button>
        )}
      </div>

      <div className="flex flex-col gap-2">
        {parada.pedidos.map((pedido) => (
          <div
            key={pedido.pedidoId}
            className={`rounded-2xl border bg-card shadow-sm p-3 flex flex-col gap-1 ${pedido.estado === "Cancelado" ? "border-red-300 bg-red-50/40" : ""}`}
          >
            <p className={`font-medium ${pedido.estado === "Cancelado" ? "line-through" : ""}`}>
              {pedido.destinatarioNombre}
            </p>
            {pedido.estado === "Cancelado" && (
              <p className="text-sm font-medium text-red-700">Cancelado por operación: no entregar.</p>
            )}
            <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
              <Package className="size-3.5" />
              {pedido.bultos} bulto(s)
            </p>
            {pedido.observaciones && (
              <p className="rounded-md bg-muted p-2 text-sm text-muted-foreground">{pedido.observaciones}</p>
            )}
          </div>
        ))}
      </div>

      {envioError && <p className="text-sm text-destructive">{envioError}</p>}

      {modo === "detalle" && (
        <div className="flex flex-col gap-2">
          <Button
            variant="outline"
            render={<a href={`tel:${pedidosVivos[0]?.destinatarioTelefono ?? ""}`} />}
            nativeButton={false}
            className="h-12 w-full text-base"
          >
            <Phone className="size-4" />
            Llamar
          </Button>

          {llegadaEn ? (
            <p className="flex items-center justify-center gap-1.5 text-center text-sm text-muted-foreground">
              <Clock className="size-3.5" />
              Llegada registrada: {new Date(llegadaEn).toLocaleTimeString()}
            </p>
          ) : parada.estado === "pendiente" ? (
            <Button variant="outline" className="h-12 w-full text-base" disabled={registrandoLlegada} onClick={onLlegue}>
              <Clock className="size-4" />
              {registrandoLlegada ? "Registrando…" : "Llegué"}
            </Button>
          ) : null}

          {parada.estado === "pendiente" && jornada.cierreRepartidorEn === null && pedidosVivos.length > 0 && (
            <Button variant="outline" className="h-12 w-full text-base" onClick={abrirCorreccion}>
              <Pencil className="size-4" />
              Corregir un dato
            </Button>
          )}

          {parada.estado === "pendiente" ? (
            <>
              <Button className="h-12 w-full text-base" onClick={() => setModo("entregado")}>
                <CheckCircle2 className="size-4" />
                Entregado
              </Button>
              <Button variant="destructive" className="h-12 w-full text-base" onClick={() => setModo("fallido")}>
                <XCircle className="size-4" />
                No pude entregar
              </Button>
            </>
          ) : (
            // Ya resuelta: mostrar de nuevo Entregado/No pude dispararía un 409 del backend
            // (TransicionesPedido no permite un estado terminal a sí mismo) — nada que ganar.
            <p className="text-center text-sm text-muted-foreground">
              Esta parada ya quedó {etiquetaEstadoParada(parada.estado).toLowerCase()}.
            </p>
          )}
        </div>
      )}

      {modo === "corregir" && (
        <div className="flex flex-col gap-3 rounded-2xl border bg-card shadow-sm p-4">
          {correccionEnviada ? (
            <>
              <p className="font-medium">Corrección enviada</p>
              <p className="text-sm text-muted-foreground">
                El dato no cambia hasta que operación la acepte. Te avisamos en Hoy cuando responda.
              </p>
              <Button className="h-12 w-full text-base" onClick={() => setModo("detalle")}>
                Volver a la parada
              </Button>
            </>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Solo se corrige el teléfono, el nombre o las observaciones. Si la dirección está mal, informá la
                entrega como fallida.
              </p>
              {pedidosVivos.length > 1 && (
                <div className="flex flex-col gap-2">
                  <Label htmlFor="corregir-pedido">Pedido</Label>
                  <select
                    id="corregir-pedido"
                    className="h-12 rounded-lg border bg-background px-2.5 text-base"
                    value={corregirPedidoId ?? ""}
                    onChange={(e) => setCorregirPedidoId(Number(e.target.value))}
                  >
                    {pedidosVivos.map((p) => (
                      <option key={p.pedidoId} value={p.pedidoId}>
                        {p.destinatarioNombre}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className="flex flex-col gap-2">
                <Label htmlFor="corregir-campo">Qué dato</Label>
                <select
                  id="corregir-campo"
                  className="h-12 rounded-lg border bg-background px-2.5 text-base"
                  value={corregirCampo}
                  onChange={(e) => setCorregirCampo(e.target.value)}
                >
                  {Object.entries(ETIQUETA_CAMPO_EDITABLE).map(([valor, etiqueta]) => (
                    <option key={valor} value={valor}>
                      {etiqueta}
                    </option>
                  ))}
                </select>
                <p className="text-xs text-muted-foreground">
                  Ahora dice:{" "}
                  {(() => {
                    const p = parada.pedidos.find((x) => x.pedidoId === corregirPedidoId);
                    if (!p) return "—";
                    const actual =
                      corregirCampo === "destinatario_telefono"
                        ? p.destinatarioTelefono
                        : corregirCampo === "destinatario_nombre"
                          ? p.destinatarioNombre
                          : p.observaciones;
                    return actual || "(vacío)";
                  })()}
                </p>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="corregir-valor">Valor correcto</Label>
                <Input
                  id="corregir-valor"
                  className="h-12 text-base"
                  value={corregirValor}
                  onChange={(e) => setCorregirValor(e.target.value)}
                />
              </div>
              <Button
                className="h-12 w-full text-base"
                disabled={enviando || corregirPedidoId === null || !corregirValor.trim()}
                onClick={enviarCorreccion}
              >
                {enviando ? "Enviando…" : "Proponer corrección"}
              </Button>
              <Button variant="outline" className="h-12 w-full text-base" disabled={enviando} onClick={() => setModo("detalle")}>
                Cancelar
              </Button>
            </>
          )}
        </div>
      )}

      {modo === "entregado" && (
        <div className="flex flex-col gap-3 rounded-2xl border bg-card shadow-sm p-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="receptor">Nombre del receptor</Label>
            <Input
              id="receptor"
              className="h-12 text-base"
              value={receptorNombre}
              onChange={(e) => setReceptorNombre(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="foto">Foto de la entrega</Label>
            <Input
              id="foto"
              type="file"
              accept="image/*"
              capture="environment"
              onChange={(e) => setFoto(e.target.files?.[0] ?? null)}
            />
            {previewFoto && (
              // eslint-disable-next-line @next/next/no-img-element -- objectURL local, no pasa por el optimizador de next/image
              <img src={previewFoto} alt="Foto de la entrega" className="h-40 w-full rounded-lg border object-cover" />
            )}
          </div>
          <div className="flex items-center gap-2">
            <Checkbox
              id="identidad"
              checked={identidadVerificada}
              onCheckedChange={(v) => setIdentidadVerificada(v === true)}
            />
            <Label htmlFor="identidad" className="font-normal">
              Identidad verificada
            </Label>
          </div>
          <Button className="h-12 w-full text-base" disabled={enviando} onClick={() => cerrar("entregado")}>
            {enviando ? "Enviando…" : "Confirmar entrega"}
          </Button>
          <Button variant="outline" className="h-12 w-full text-base" disabled={enviando} onClick={() => setModo("detalle")}>
            Cancelar
          </Button>
        </div>
      )}

      {modo === "fallido" && (
        <div className="flex flex-col gap-2 rounded-2xl border bg-card shadow-sm p-4">
          <p className="text-sm text-muted-foreground">Motivo:</p>
          {jornada.motivosFallo.map((motivo) => (
            <Button
              key={motivo}
              variant="outline"
              className="h-12 w-full text-base"
              disabled={enviando}
              onClick={() => cerrar("fallido", motivo)}
            >
              {motivo.replaceAll("_", " ")}
            </Button>
          ))}
          <Button variant="outline" className="h-12 w-full text-base" disabled={enviando} onClick={() => setModo("detalle")}>
            Cancelar
          </Button>
        </div>
      )}
    </div>
  );
}
