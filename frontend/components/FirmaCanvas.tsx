"use client";

import { useCallback, useEffect, useImperativeHandle, useRef, useState, type Ref } from "react";
import { Button } from "@/components/ui/button";

export interface FirmaCanvasHandle {
  /** JPEG con fondo blanco, o null si no se dibujó nada. */
  exportar: () => Promise<Blob | null>;
  borrar: () => void;
}

const ALTO_PX = 180;

/**
 * Firma a mano (RF-35). Es un path sobre un canvas: no justifica una librería, mismo criterio que
 * las flechas ↑/↓ en vez de drag & drop (construccion_v1.md §4.1).
 *
 * El canvas se rellena de BLANCO antes de dibujar: JPEG no tiene canal alfa y un canvas
 * transparente sale negro en `toBlob("image/jpeg")` — el chequeo de magic bytes del servidor
 * pasa igual y la firma sería un rectángulo negro.
 */
export function FirmaCanvas({
  ref,
  onCambio,
}: {
  ref?: Ref<FirmaCanvasHandle>;
  /** Avisa si hay trazo o no, para habilitar el botón de confirmar. */
  onCambio?: (hayFirma: boolean) => void;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const dibujando = useRef(false);
  const [hayFirma, setHayFirma] = useState(false);

  const pintarFondo = useCallback(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.strokeStyle = "#111111";
    ctx.lineWidth = 3;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
  }, []);

  // El ancho real del canvas sale del contenedor: el atributo `width` fija la resolución de
  // dibujo y tiene que coincidir con el ancho en pantalla o el trazo sale desplazado.
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    canvas.width = canvas.clientWidth;
    canvas.height = ALTO_PX;
    pintarFondo();
  }, [pintarFondo]);

  function punto(e: React.PointerEvent<HTMLCanvasElement>) {
    const rect = e.currentTarget.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  function onDown(e: React.PointerEvent<HTMLCanvasElement>) {
    const ctx = e.currentTarget.getContext("2d");
    if (!ctx) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    dibujando.current = true;
    const { x, y } = punto(e);
    ctx.beginPath();
    ctx.moveTo(x, y);
    // Un toque sin arrastre también cuenta como trazo.
    ctx.lineTo(x + 0.01, y + 0.01);
    ctx.stroke();
    if (!hayFirma) {
      setHayFirma(true);
      onCambio?.(true);
    }
  }

  function onMove(e: React.PointerEvent<HTMLCanvasElement>) {
    if (!dibujando.current) return;
    const ctx = e.currentTarget.getContext("2d");
    if (!ctx) return;
    const { x, y } = punto(e);
    ctx.lineTo(x, y);
    ctx.stroke();
  }

  function onUp() {
    dibujando.current = false;
  }

  const borrar = useCallback(() => {
    pintarFondo();
    setHayFirma(false);
    onCambio?.(false);
  }, [pintarFondo, onCambio]);

  useImperativeHandle(
    ref,
    () => ({
      borrar,
      exportar: () =>
        new Promise<Blob | null>((resolve) => {
          const canvas = canvasRef.current;
          if (!canvas || !hayFirma) return resolve(null);
          canvas.toBlob((blob) => resolve(blob), "image/jpeg", 0.8);
        }),
    }),
    [borrar, hayFirma],
  );

  return (
    <div className="flex flex-col gap-2">
      <canvas
        ref={canvasRef}
        // touch-none: sin esto el navegador interpreta el arrastre como scroll y corta el trazo.
        className="w-full touch-none rounded-lg border bg-white"
        style={{ height: ALTO_PX }}
        onPointerDown={onDown}
        onPointerMove={onMove}
        onPointerUp={onUp}
        onPointerCancel={onUp}
      />
      <Button type="button" variant="outline" className="h-11 text-base" onClick={borrar} disabled={!hayFirma}>
        Borrar firma
      </Button>
    </div>
  );
}
