"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { EditorParadas, type ResultadoPrevisualizacion } from "@/components/viajes/EditorParadas";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  type ClienteDestinatarioResumen,
  type CorteHorarioPortal,
  type ParadaViajeEntrada,
  type PedidoPortalCreado,
  type PrevisualizacionViajePortal,
  type TipoVehiculo,
} from "@/lib/dominio/tipos";

const hoyISO = () => new Date().toLocaleDateString("en-CA");

// Acta changelog 4.31: la carga ya no ofrece urgencia ni vehículo. Todo entra como no urgente y se
// cotiza en camioneta; el vehículo real lo decide Operación al armar la ruta.
const URGENTE = false;
const TIPO_VEHICULO: TipoVehiculo = "camioneta";

export default function NuevoEnvioPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <NuevoEnvio />
    </RequireRole>
  );
}

/**
 * Carga del portal: la fecha de entrega y una o más paradas, cada una
 * con su destinatario (de "Mis clientes" o nuevo, con su dirección de entrega), bultos y
 * observaciones; el "+" suma paradas y "Cerrar ruta" muestra el recorrido y el precio antes de
 * confirmar (EditorParadas). Con una sola parada se carga como un envío común; con varias, como un
 * viaje con su ruta propuesta (acta changelog 4.29).
 */
function NuevoEnvio() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [contactos, setContactos] = useState<ClienteDestinatarioResumen[]>([]);
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  // Corte horario (§6): en vez de solo bloquear, ofrece pasar la carga al día siguiente.
  const [corteHorario, setCorteHorario] = useState<CorteHorarioPortal | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta/destinatarios")
      .then((r) => leerJson<ClienteDestinatarioResumen[]>(r))
      .then(setContactos)
      .catch(() => setContactos([])); // opcional: sin la libreta, las paradas se cargan a mano
  }, [fetchConSesion]);

  /** Error legible de una respuesta; el 409 de corte horario trae su propio cuerpo y ofrece mañana. */
  async function errorDe(resp: Response): Promise<Error> {
    if (resp.status === 409) {
      const cuerpo = await resp.clone().json().catch(() => null);
      if (cuerpo && typeof cuerpo === "object" && "fechaEntregaSugerida" in cuerpo) {
        setCorteHorario(cuerpo as CorteHorarioPortal);
        return new Error((cuerpo as CorteHorarioPortal).mensaje);
      }
    }
    return new Error((await leerError(resp)).mensaje);
  }

  const cuerpoViaje = (paradas: ParadaViajeEntrada[]) =>
    JSON.stringify({ fechaEntrega, urgente: URGENTE, tipoVehiculo: TIPO_VEHICULO, observaciones: null, paradas });

  async function previsualizar(paradas: ParadaViajeEntrada[]): Promise<ResultadoPrevisualizacion> {
    setCorteHorario(null);
    const resp = await fetchConSesion("/api/mi-cuenta/viajes/previsualizar", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: cuerpoViaje(paradas),
    });
    if (!resp.ok) throw await errorDe(resp);
    const r = await leerJson<PrevisualizacionViajePortal>(resp);
    const precios = r.precios ? paradas.map((_, i) => r.precios!.find((p) => p.indice === i)?.precio ?? null) : null;
    return { recorrido: r.recorrido, precios, total: r.total };
  }

  /** Los destinatarios nuevos marcados para guardar van a "Mis clientes". Si alguno falla (ya
   * existía, por ejemplo), la carga igual quedó hecha: no se frena por esto. */
  async function guardarContactos(nuevos: ParadaViajeEntrada[]) {
    await Promise.allSettled(nuevos.map((p) =>
      fetchConSesion("/api/mi-cuenta/destinatarios", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          nombre: p.destinatarioNombre, telefono: p.destinatarioTelefono,
          destinoUbicacionId: p.destinoUbicacionId, observaciones: p.observaciones,
        }),
      })));
  }

  async function confirmar(paradas: ParadaViajeEntrada[], nuevos: ParadaViajeEntrada[]) {
    setCorteHorario(null);
    if (paradas.length === 1) {
      const p = paradas[0];
      const resp = await fetchConSesion("/api/mi-cuenta/pedidos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          destinatarioNombre: p.destinatarioNombre,
          destinatarioTelefono: p.destinatarioTelefono,
          destinoUbicacionId: p.destinoUbicacionId,
          bultos: p.bultos,
          pesoKg: null,
          fechaEntrega,
          urgente: URGENTE,
          tipoVehiculo: TIPO_VEHICULO,
          observaciones: p.observaciones,
        }),
      });
      if (!resp.ok) throw await errorDe(resp);
      const creado = await leerJson<PedidoPortalCreado>(resp);
      await guardarContactos(nuevos);
      router.push(`/mis-envios/${creado.id}`);
      return;
    }

    const resp = await fetchConSesion("/api/mi-cuenta/viajes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: cuerpoViaje(paradas),
    });
    if (!resp.ok) throw await errorDe(resp);
    const { id } = await leerJson<{ id: number }>(resp);
    await guardarContactos(nuevos);
    router.push(`/mis-envios/viajes/${id}`);
  }

  return (
    <div className="p-4 md:p-8 max-w-3xl flex flex-col gap-6">
      <CabeceraSesion titulo="Nuevo envío" />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos del viaje</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 md:max-w-xs">
          <Label htmlFor="viaje-fecha">Fecha de entrega</Label>
          <Input id="viaje-fecha" type="date" required value={fechaEntrega} onChange={(e) => setFechaEntrega(e.target.value)} />
        </CardContent>
      </Card>

      {corteHorario && (
        <Card>
          <CardContent className="flex flex-col gap-3 pt-6">
            <p className="text-sm text-destructive">{corteHorario.mensaje}</p>
            <Button
              type="button"
              className="self-start"
              onClick={() => {
                setFechaEntrega(corteHorario.fechaEntregaSugerida);
                setCorteHorario(null);
              }}
            >
              Pasar la entrega a mañana
            </Button>
          </CardContent>
        </Card>
      )}

      <EditorParadas
        basePath="/api/mi-cuenta"
        contactos={contactos}
        permitirGuardarContacto
        previsualizar={previsualizar}
        confirmar={confirmar}
      />
    </div>
  );
}
