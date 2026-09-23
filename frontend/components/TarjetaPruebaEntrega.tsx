"use client";

import { useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerJson } from "@/lib/api/errores";
import type { PruebaEntregaResumen } from "@/lib/dominio/tipos";

/**
 * Lo que el repartidor capturó al cerrar la parada: quién recibió, su DNI (o por qué no lo dio), hora y
 * desvío contra el destino. Solo administración: el DNI es un dato personal de un tercero (RNF-09) y el
 * endpoint (`GET /api/pedidos/{id}/prueba-entrega`) ya está restringido a ese rol. Sin prueba (pedido no
 * cerrado todavía) no se muestra nada.
 */
export function TarjetaPruebaEntrega({ pedidoId }: { pedidoId: number }) {
  const { fetchConSesion } = useAuth();
  const [prueba, setPrueba] = useState<PruebaEntregaResumen | null>(null);

  useEffect(() => {
    fetchConSesion(`/api/pedidos/${pedidoId}/prueba-entrega`)
      .then((r) => (r.ok ? leerJson<PruebaEntregaResumen>(r) : null))
      .then(setPrueba)
      .catch(() => setPrueba(null)); // decorativa: si falla, no se muestra
  }, [fetchConSesion, pedidoId]);

  if (!prueba) return null;

  const entregado = prueba.resultado === "entregado";
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Prueba de entrega</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        <Fila etiqueta="Resultado" valor={entregado ? "Entregado" : `Fallido${prueba.motivoFallo ? ` — ${prueba.motivoFallo.replaceAll("_", " ")}` : ""}`} />
        {entregado && <Fila etiqueta="Recibió" valor={prueba.receptorNombre ?? "—"} />}
        {entregado && (
          <Fila
            etiqueta="DNI"
            valor={
              prueba.documentoNumero ?? (
                <span className="text-amber-700">No lo dio: {prueba.sinDocumentoMotivo ?? "sin motivo"}</span>
              )
            }
          />
        )}
        <Fila etiqueta="Hora" valor={new Date(prueba.capturadaEn).toLocaleString("es-AR")} />
        {prueba.desvioMetros !== null && (
          <Fila
            etiqueta="Desvío del destino"
            valor={
              <span className={prueba.desvioAlto ? "font-medium text-destructive" : undefined}>
                {prueba.desvioMetros} m{prueba.desvioAlto ? " (alto)" : ""}
              </span>
            }
          />
        )}
        <Fila etiqueta="Foto" valor={prueba.tieneFoto ? "Sí" : "No"} />
      </CardContent>
    </Card>
  );
}

function Fila({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <span className="shrink-0 text-muted-foreground">{etiqueta}</span>
      <span className="text-right">{valor}</span>
    </div>
  );
}
