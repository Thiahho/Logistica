"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { EditorParadas, type ResultadoPrevisualizacion } from "@/components/viajes/EditorParadas";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ClienteSeleccion, ParadaViajeEntrada, PrevisualizacionViaje } from "@/lib/dominio/tipos";

const hoyISO = () => new Date().toLocaleDateString("en-CA");

/** Viaje cargado por Operación para un cliente. Sin precio al cargar: como cualquier pedido interno,
 * se cotiza al cerrar la planificación de la ruta, con su vehículo. */
export default function NuevoViajePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <NuevoViaje />
    </RequireRole>
  );
}

function NuevoViaje() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();
  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [clienteId, setClienteId] = useState<number | null>(null);
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la lista de clientes."));
  }, [fetchConSesion]);

  const cuerpo = (paradas: ParadaViajeEntrada[]) =>
    // Acta changelog 4.31: la carga ya no ofrece urgencia.
    JSON.stringify({ clienteId, fechaEntrega, urgente: false, observaciones: null, paradas });

  async function previsualizar(paradas: ParadaViajeEntrada[]): Promise<ResultadoPrevisualizacion> {
    if (clienteId === null) throw new Error("Elegí el cliente.");
    const resp = await fetchConSesion("/api/viajes/previsualizar", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: cuerpo(paradas),
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
    return { recorrido: await leerJson<PrevisualizacionViaje>(resp), precios: null, total: null };
  }

  async function confirmar(paradas: ParadaViajeEntrada[]) {
    // Una sola parada: un pedido común (PedidosController.Crear), sin ruta propia.
    if (paradas.length === 1) {
      const p = paradas[0];
      const resp = await fetchConSesion("/api/pedidos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clienteId, destinatarioNombre: p.destinatarioNombre, destinatarioTelefono: p.destinatarioTelefono,
          destinoUbicacionId: p.destinoUbicacionId, bultos: p.bultos, fechaEntrega, urgente: false, peajes: 0,
          observaciones: p.observaciones,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const { id } = await leerJson<{ id: number }>(resp);
      router.push(`/pedidos/${id}`);
      return;
    }

    const resp = await fetchConSesion("/api/viajes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: cuerpo(paradas),
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
    const { id } = await leerJson<{ id: number }>(resp);
    router.push(`/viajes/${id}`);
  }

  return (
    <div className="p-4 md:p-8 max-w-3xl flex flex-col gap-6">
      <CabeceraSesion titulo="Nuevo viaje" />
      {error && <p className="text-sm text-destructive">{error}</p>}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos del viaje</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label>Cliente</Label>
            <ComboboxBusqueda
              items={clientes.map((c) => ({ value: String(c.id), label: c.razonSocial }))}
              value={clienteId !== null ? String(clienteId) : null}
              onValueChange={(v) => setClienteId(v ? Number(v) : null)}
              placeholder="Elegir cliente"
            />
          </div>
          <div className="flex flex-col gap-2 md:max-w-xs">
            <Label htmlFor="viaje-fecha">Fecha de entrega</Label>
            <Input id="viaje-fecha" type="date" value={fechaEntrega} onChange={(e) => setFechaEntrega(e.target.value)} />
          </div>
          <p className="text-xs text-muted-foreground">
            Al confirmar se crea la ruta propuesta en planificación, con las paradas en este orden. Asignale vehículo y
            repartidor desde Rutas y cerrá la planificación como siempre.
          </p>
        </CardContent>
      </Card>

      <EditorParadas basePath="/api" previsualizar={previsualizar} confirmar={confirmar} />
    </div>
  );
}
