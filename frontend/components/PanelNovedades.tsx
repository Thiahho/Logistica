"use client";

import { useState } from "react";
import Link from "next/link";
import { Camera } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { leerError } from "@/lib/api/errores";
import {
  ETIQUETA_CAMPO_EDITABLE,
  ETIQUETA_TIPO_NOVEDAD,
  etiquetaCategoria,
  type NovedadResumen,
} from "@/lib/dominio/tipos";

/**
 * Lo que informó el repartidor desde la calle y lo que operación hizo con eso (RF-36/RF-37, acta
 * changelog 4.8). Lo informado no se edita: acá solo se responde. Aceptar un cambio propuesto es lo
 * que recién lo aplica al pedido.
 *
 * Datos por prop, no fetch propio: la pantalla que lo monta ya pollea la ruta en curso (useSondeo) y
 * es quien sabe cuándo recargar tras responder.
 */
export function PanelNovedades({
  novedades,
  onCambio,
  mostrarRuta = false,
}: {
  novedades: NovedadResumen[];
  onCambio: () => void;
  /** En el listado global hace falta saber de qué ruta es; dentro de una ruta es ruido. */
  mostrarRuta?: boolean;
}) {
  if (novedades.length === 0) {
    return <p className="text-sm text-muted-foreground">Sin novedades.</p>;
  }

  return (
    <ul className="flex flex-col gap-3">
      {novedades.map((n) => (
        <NovedadFila key={n.id} novedad={n} onCambio={onCambio} mostrarRuta={mostrarRuta} />
      ))}
    </ul>
  );
}

const ESTILO_ESTADO: Record<NovedadResumen["estado"], string> = {
  abierta: "bg-amber-100 text-amber-800",
  resuelta: "bg-green-100 text-green-700",
  rechazada: "bg-neutral-200 text-neutral-700",
};

function NovedadFila({
  novedad: n,
  onCambio,
  mostrarRuta,
}: {
  novedad: NovedadResumen;
  onCambio: () => void;
  mostrarRuta: boolean;
}) {
  const { fetchConSesion } = useAuth();
  const [resolucion, setResolucion] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Solo lo que informó el repartidor se responde; lo que creó operación es un aviso hacia la calle.
  const respondible = n.origen === "repartidor" && n.estado === "abierta";
  const esCambio = n.tipo === "cambio_propuesto";

  async function resolver(aceptar: boolean) {
    setEnviando(true);
    setError(null);
    try {
      const resp = await fetchConSesion(`/api/novedades/${n.id}/resolver`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ aceptar, resolucion: resolucion.trim() || null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      onCambio();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo responder la novedad.");
    } finally {
      setEnviando(false);
    }
  }

  // El token va en el header Authorization: no se puede usar <img src> directo. Se baja el blob con
  // la sesión y se abre en otra pestaña — nunca queda una URL pública de la foto.
  async function verFoto() {
    try {
      const resp = await fetchConSesion(`/api/novedades/${n.id}/foto`);
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      window.open(URL.createObjectURL(await resp.blob()), "_blank");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo abrir la foto.");
    }
  }

  return (
    <li className="flex flex-col gap-2 rounded-lg border p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium">{ETIQUETA_TIPO_NOVEDAD[n.tipo]}</span>
        {n.categoria && (
          <span className="rounded-full bg-muted px-2 py-0.5 text-xs">{etiquetaCategoria(n.categoria)}</span>
        )}
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${ESTILO_ESTADO[n.estado]}`}>{n.estado}</span>
        <span className="ml-auto text-xs text-muted-foreground">
          {n.creadaPorNombre} · {new Date(n.creadaEn).toLocaleString("es-AR")}
        </span>
      </div>

      {(mostrarRuta || n.paradaOrden !== null || n.pedidoId !== null) && (
        <p className="text-xs text-muted-foreground">
          {mostrarRuta && (
            <>
              <Link href={`/rutas/${n.rutaId}`} className="underline">
                Ruta #{n.rutaId}
              </Link>{" "}
              · {n.repartidorNombre ?? "sin repartidor"}
              {n.paradaOrden !== null || n.pedidoId !== null ? " · " : ""}
            </>
          )}
          {n.paradaOrden !== null && `Parada ${n.paradaOrden}`}
          {n.paradaOrden !== null && n.pedidoId !== null && " · "}
          {n.pedidoId !== null && (
            <Link href={`/pedidos/${n.pedidoId}`} className="underline">
              Pedido #{n.pedidoId}
              {n.pedidoDestinatario ? ` (${n.pedidoDestinatario})` : ""}
            </Link>
          )}
        </p>
      )}

      {n.descripcion && <p className="text-sm">{n.descripcion}</p>}

      {esCambio && n.propuestaCampo && (
        <p className="rounded-md bg-muted p-2 text-sm">
          <span className="font-medium">{ETIQUETA_CAMPO_EDITABLE[n.propuestaCampo] ?? n.propuestaCampo}:</span>{" "}
          <span className="line-through">{n.propuestaValorAnterior || "(vacío)"}</span> →{" "}
          <span className="font-medium">{n.propuestaValorNuevo}</span>
        </p>
      )}

      {n.tieneFoto && (
        <Button size="sm" variant="outline" className="self-start" onClick={verFoto}>
          <Camera className="size-3.5" />
          Ver foto
        </Button>
      )}

      {n.resolucion && (
        <p className="text-sm text-muted-foreground">
          <span className="font-medium">Respuesta{n.resueltaPorNombre ? ` de ${n.resueltaPorNombre}` : ""}:</span>{" "}
          {n.resolucion}
        </p>
      )}
      {n.origen === "operacion" && (
        <p className="text-xs text-muted-foreground">
          {n.vistoEn
            ? `El repartidor lo vio a las ${new Date(n.vistoEn).toLocaleTimeString("es-AR")}.`
            : "El repartidor todavía no lo vio."}
        </p>
      )}

      {respondible && (
        <div className="flex flex-col gap-2 border-t pt-2">
          <Input
            placeholder={esCambio ? "Motivo (obligatorio si rechazás)" : "Qué se hizo con esto"}
            value={resolucion}
            onChange={(e) => setResolucion(e.target.value)}
          />
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex gap-2">
            {esCambio ? (
              <>
                <Button size="sm" disabled={enviando} onClick={() => resolver(true)}>
                  Aceptar y aplicar
                </Button>
                <Button size="sm" variant="outline" disabled={enviando} onClick={() => resolver(false)}>
                  Rechazar
                </Button>
              </>
            ) : (
              <Button size="sm" disabled={enviando || !resolucion.trim()} onClick={() => resolver(true)}>
                Marcar como resuelta
              </Button>
            )}
          </div>
        </div>
      )}
      {!respondible && error && <p className="text-sm text-destructive">{error}</p>}
    </li>
  );
}

/** Tarjeta lista para usar dentro de una pantalla de ruta. */
export function TarjetaNovedades({
  novedades,
  onCambio,
}: {
  novedades: NovedadResumen[];
  onCambio: () => void;
}) {
  const abiertas = novedades.filter((n) => n.estado === "abierta" && n.origen === "repartidor").length;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          Novedades
          {abiertas > 0 && (
            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
              {abiertas} para responder
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        <PanelNovedades novedades={novedades} onCambio={onCambio} />
      </CardContent>
    </Card>
  );
}
