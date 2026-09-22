"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaTipoVehiculo,
  type ClienteDestinatarioResumen,
  type CorteHorarioPortal,
  type PedidoPortalCreado,
  type TipoVehiculo,
} from "@/lib/dominio/tipos";

const hoyISO = () => new Date().toISOString().slice(0, 10);

export default function NuevoEnvioPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <FormularioPortal />
    </RequireRole>
  );
}

function FormularioPortal() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [destinatarioNombre, setDestinatarioNombre] = useState("");
  const [destinatarioTelefono, setDestinatarioTelefono] = useState("");
  const [direccion, setDireccion] = useState<DireccionResuelta | null>(null);
  // "Mis clientes" (§5 del plan): elegir un contacto guardado prellena nombre/teléfono/dirección
  // sin tipear ni geocodificar de nuevo. direccionInicial + direccionKey fuerzan el remonte de
  // SelectorDireccion (no controlado — mismo criterio que /depositos al limpiar tras crear).
  const [contactos, setContactos] = useState<ClienteDestinatarioResumen[]>([]);
  const [contactoElegidoId, setContactoElegidoId] = useState<string | null>(null);
  const [direccionInicial, setDireccionInicial] = useState<DireccionResuelta | null>(null);
  const [direccionKey, setDireccionKey] = useState(0);
  const [bultos, setBultos] = useState(1);
  const [tipoVehiculo, setTipoVehiculo] = useState<TipoVehiculo>("camioneta");
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  const [urgente, setUrgente] = useState(false);
  const [observaciones, setObservaciones] = useState("");

  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Corte horario (§6): en vez de solo bloquear, ofrece cargar directo para el día siguiente.
  const [corteHorario, setCorteHorario] = useState<CorteHorarioPortal | null>(null);
  const [creado, setCreado] = useState<PedidoPortalCreado | null>(null);

  const listoParaEnviar = destinatarioNombre.trim() !== "" && destinatarioTelefono.trim() !== "" && direccion !== null;

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta/destinatarios")
      .then((r) => leerJson<ClienteDestinatarioResumen[]>(r))
      .then(setContactos)
      .catch(() => setContactos([])); // opcional: si falla, el formulario sigue usable a mano
  }, [fetchConSesion]);

  function elegirContacto(id: string | null) {
    setContactoElegidoId(id);
    if (id === null) return;
    const c = contactos.find((x) => x.id === id);
    if (!c) return;
    setDestinatarioNombre(c.nombre);
    setDestinatarioTelefono(c.telefono);
    const resuelta: DireccionResuelta = {
      ubicacionId: c.destinoUbicacionId,
      calleNumero: c.destinoCalleNumero,
      localidadId: c.localidadId,
      localidadNombre: c.localidadNombre,
      lat: c.lat,
      lng: c.lng,
      geoConfianza: c.geoConfianza,
    };
    setDireccionInicial(resuelta);
    setDireccion(resuelta);
    setDireccionKey((k) => k + 1);
  }

  async function enviar(fecha: string) {
    if (!direccion) return;
    setError(null);
    setCorteHorario(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/mi-cuenta/pedidos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          destinatarioNombre: destinatarioNombre.trim(),
          destinatarioTelefono: destinatarioTelefono.trim(),
          destinoUbicacionId: direccion.ubicacionId,
          bultos,
          pesoKg: null,
          fechaEntrega: fecha,
          urgente,
          tipoVehiculo,
          observaciones: observaciones.trim() || null,
        }),
      });
      if (resp.status === 409) {
        // §6: distingue el corte horario (tiene fechaEntregaSugerida) del resto de los 409
        // (deuda vencida, cuenta inactiva) — esos sí son un bloqueo liso, no una oferta de reintento.
        const cuerpo = await resp.json().catch(() => null);
        if (cuerpo && typeof cuerpo === "object" && "fechaEntregaSugerida" in cuerpo) {
          setCorteHorario(cuerpo as CorteHorarioPortal);
          return;
        }
        throw new Error((cuerpo as { mensaje?: string } | null)?.mensaje ?? "No se pudo cargar el envío.");
      }
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setCreado(await resp.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el envío.");
    } finally {
      setEnviando(false);
    }
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    await enviar(fechaEntrega);
  }

  if (creado) {
    return (
      <div className="p-4 md:p-8 max-w-xl">
        <CabeceraSesion titulo="Envío cargado" />
        <Card>
          <CardContent className="flex flex-col gap-4 pt-6">
            {creado.requiereCotizacion ? (
              <div>
                <p className="text-lg font-semibold text-amber-600">Pendiente de cotización</p>
                <p className="text-sm text-muted-foreground">
                  Tu zona todavía no tiene una tarifa cargada. Te contactamos para confirmar el
                  precio antes del retiro.
                </p>
              </div>
            ) : (
              <div>
                <p className="text-lg font-semibold">${creado.precio?.toLocaleString("es-AR")}</p>
                <p className="text-sm text-muted-foreground">
                  Precio definitivo, no estimado. Puede diferir solo si la ruta termina usando otro
                  vehículo — esa diferencia la absorbe la Empresa, no vos.
                </p>
              </div>
            )}
            <p className="text-sm">Entrega: {creado.fechaEntrega}. Cargado, pendiente de retiro.</p>
            <div className="flex gap-2">
              <Button onClick={() => router.push("/mis-envios")}>Ver mis envíos</Button>
              <Button
                variant="secondary"
                onClick={() => {
                  setCreado(null);
                  setDestinatarioNombre("");
                  setDestinatarioTelefono("");
                  setDireccion(null);
                  setDireccionInicial(null);
                  setContactoElegidoId(null);
                  setDireccionKey((k) => k + 1);
                  setBultos(1);
                  setObservaciones("");
                }}
              >
                Cargar otro envío
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-8 max-w-xl">
      <CabeceraSesion titulo="Nuevo envío" />
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        {contactos.length > 0 && (
          <Card>
            <CardContent className="flex flex-col gap-2 pt-6">
              <Label>Usar un cliente guardado</Label>
              <ComboboxBusqueda
                items={contactos.map((c) => ({ value: c.id, label: c.nombre }))}
                value={contactoElegidoId}
                onValueChange={elegirContacto}
                placeholder="Elegir de tu libreta de clientes…"
              />
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Destinatario</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="destinatario">Nombre</Label>
              <Input
                id="destinatario"
                required
                value={destinatarioNombre}
                onChange={(e) => setDestinatarioNombre(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="telefono">Teléfono</Label>
              <Input
                id="telefono"
                required
                value={destinatarioTelefono}
                onChange={(e) => setDestinatarioTelefono(e.target.value)}
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Dirección de entrega</CardTitle>
          </CardHeader>
          <CardContent>
            {/* basePath="/api/mi-cuenta": el rol 'cliente' no llega a /api/ubicaciones ni
             * /api/localidades (BackOffice de clase) — MiCuentaController expone el mismo par de
             * acciones bajo su propia policy. */}
            <SelectorDireccion
              key={direccionKey}
              inicial={direccionInicial}
              onCambio={setDireccion}
              basePath="/api/mi-cuenta"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Envío</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-2">
                <Label htmlFor="bultos">Bultos</Label>
                <Input
                  id="bultos"
                  type="number"
                  min={1}
                  required
                  value={bultos}
                  onChange={(e) => setBultos(Number(e.target.value))}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="fecha">Fecha de entrega</Label>
                <Input
                  id="fecha"
                  type="date"
                  required
                  value={fechaEntrega}
                  onChange={(e) => setFechaEntrega(e.target.value)}
                />
              </div>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Vehículo</Label>
              <p className="text-xs text-muted-foreground">
                Define el precio final de este envío — no es solo una preferencia.
              </p>
              <div className="flex gap-2">
                {(["camioneta", "moto"] as const).map((tipo) => (
                  <Button
                    key={tipo}
                    type="button"
                    variant={tipoVehiculo === tipo ? "default" : "outline"}
                    onClick={() => setTipoVehiculo(tipo)}
                  >
                    {etiquetaTipoVehiculo(tipo)}
                  </Button>
                ))}
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="urgente" checked={urgente} onCheckedChange={(v) => setUrgente(v === true)} />
              <Label htmlFor="urgente">Urgente</Label>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="observaciones">Observaciones (opcional)</Label>
              <Input id="observaciones" value={observaciones} onChange={(e) => setObservaciones(e.target.value)} />
            </div>
          </CardContent>
        </Card>

        {corteHorario && (
          <Card>
            <CardContent className="flex flex-col gap-3 pt-6">
              <p className="text-sm text-destructive">{corteHorario.mensaje}</p>
              <Button
                type="button"
                disabled={enviando}
                onClick={() => {
                  setFechaEntrega(corteHorario.fechaEntregaSugerida);
                  void enviar(corteHorario.fechaEntregaSugerida);
                }}
              >
                Cargar para mañana
              </Button>
            </CardContent>
          </Card>
        )}

        {error && <p className="text-sm text-destructive">{error}</p>}

        <Button type="submit" disabled={enviando || !listoParaEnviar}>
          {enviando ? "Guardando…" : "Cargar envío"}
        </Button>
      </form>
    </div>
  );
}
