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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaTipoVehiculo,
  type ClienteSeleccion,
  type DesglosePrecio,
  type TipoVehiculo,
} from "@/lib/dominio/tipos";

const hoyISO = () => new Date().toISOString().slice(0, 10);

export default function NuevoDeliveryPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <FormularioAltaDelivery />
    </RequireRole>
  );
}

function FormularioAltaDelivery() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [clienteId, setClienteId] = useState<number | null>(null);
  const [referenciaCliente, setReferenciaCliente] = useState("");

  const [origen, setOrigen] = useState<DireccionResuelta | null>(null);
  const [destino, setDestino] = useState<DireccionResuelta | null>(null);

  const [destinatarioNombre, setDestinatarioNombre] = useState("");
  const [destinatarioTelefono, setDestinatarioTelefono] = useState("");
  const [bultos, setBultos] = useState(1);
  const [pesoKg, setPesoKg] = useState("");
  const [valorDeclarado, setValorDeclarado] = useState("");
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  const [urgente, setUrgente] = useState(false);
  const [tipoVehiculo, setTipoVehiculo] = useState<TipoVehiculo>("moto");
  const [peajes, setPeajes] = useState("0");
  const [kmManual, setKmManual] = useState("");
  const [precioManual, setPrecioManual] = useState("");
  const [observaciones, setObservaciones] = useState("");

  const [cotizacion, setCotizacion] = useState<DesglosePrecio | null>(null);
  const [cotizando, setCotizando] = useState(false);
  const [errorCotizar, setErrorCotizar] = useState<string | null>(null);

  const [enviando, setEnviando] = useState(false);
  const [errorAlta, setErrorAlta] = useState<string | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la lista de clientes."));
  }, [fetchConSesion]);

  // Mismo patrón que pedidos/nuevo (debounce 400ms + AbortController, sin setState síncrono en
  // el cuerpo del efecto): acá el tipo de vehículo ya está elegido, así que cotiza uno solo en
  // vez del rango camioneta/moto.
  const listoParaCotizar =
    clienteId !== null && origen !== null && destino !== null && fechaEntrega !== "";

  useEffect(() => {
    if (!listoParaCotizar) return;
    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setCotizando(true);
      setErrorCotizar(null);
      try {
        const resp = await fetchConSesion("/api/deliverys/cotizar", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            origenUbicacionId: origen!.ubicacionId,
            destinoUbicacionId: destino!.ubicacionId,
            clienteId,
            fechaEntrega,
            urgente,
            tipoVehiculo,
            peajes: Number(peajes) || 0,
            kmManual: kmManual ? Number(kmManual) : null,
            precioManual: precioManual ? Number(precioManual) : null,
          }),
          signal: abort.signal,
        });
        if (resp.ok) setCotizacion(await resp.json());
        else {
          setCotizacion(null);
          setErrorCotizar((await leerError(resp)).mensaje);
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setCotizacion(null);
        setErrorCotizar(err instanceof Error ? err.message : "No se pudo cotizar.");
      } finally {
        if (!abort.signal.aborted) setCotizando(false);
      }
    }, 400);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
  }, [listoParaCotizar, origen, destino, clienteId, fechaEntrega, urgente, tipoVehiculo, peajes, kmManual, precioManual, fetchConSesion]);

  const cotizacionVisible = listoParaCotizar ? cotizacion : null;
  // La zona sin tarifa cargada es el único 400 esperable de /cotizar en este punto del formulario
  // (Anexo I B9) — el resto de los 400 (tipo de vehículo inválido, ubicación inexistente) no
  // debería pasar nunca con este formulario, así que no hace falta distinguirlos acá.
  const requiereCotizacionManual = errorCotizar !== null && !precioManual;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!clienteId || !origen || !destino) return;
    setErrorAlta(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/deliverys", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clienteId,
          referenciaCliente: referenciaCliente.trim() || null,
          origenUbicacionId: origen.ubicacionId,
          destinoUbicacionId: destino.ubicacionId,
          destinatarioNombre: destinatarioNombre.trim(),
          destinatarioTelefono: destinatarioTelefono.trim(),
          bultos,
          pesoKg: pesoKg ? Number(pesoKg) : null,
          valorDeclarado: valorDeclarado ? Number(valorDeclarado) : null,
          fechaEntrega,
          urgente,
          tipoVehiculo,
          peajes: Number(peajes) || 0,
          kmManual: kmManual ? Number(kmManual) : null,
          precioManual: precioManual ? Number(precioManual) : null,
          observaciones: observaciones || null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      router.push("/deliverys");
    } catch (err) {
      setErrorAlta(err instanceof Error ? err.message : "No se pudo crear el delivery.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-8 max-w-2xl">
      <CabeceraSesion titulo="Nuevo delivery" />
      <p className="text-sm text-muted-foreground mb-6">
        Servicio punto a punto ad-hoc (Anexo I D14): sin retiro programado. El precio se cotiza y
        se congela en el acto, con el tipo de vehículo elegido acá — no depende de una ruta.
      </p>
      {errorCarga && <p className="text-sm text-destructive mb-4">{errorCarga}</p>}
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Cliente</CardTitle>
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
            <div className="flex flex-col gap-2">
              <Label htmlFor="referencia">Referencia (opcional)</Label>
              <Input
                id="referencia"
                value={referenciaCliente}
                onChange={(e) => setReferenciaCliente(e.target.value)}
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Origen</CardTitle>
          </CardHeader>
          <CardContent>
            <SelectorDireccion inicial={null} onCambio={setOrigen} idPrefijo="origen" />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Destino</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <SelectorDireccion inicial={null} onCambio={setDestino} idPrefijo="destino" />
            <div className="flex flex-col gap-2">
              <Label htmlFor="destinatario">Destinatario</Label>
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
              <div className="flex flex-col gap-2">
                <Label htmlFor="peso">Peso (kg, opcional)</Label>
                <Input id="peso" type="number" step="0.1" value={pesoKg} onChange={(e) => setPesoKg(e.target.value)} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="valor">Valor declarado (opcional)</Label>
                <Input id="valor" type="number" value={valorDeclarado} onChange={(e) => setValorDeclarado(e.target.value)} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="peajes">Peajes</Label>
                <Input id="peajes" type="number" step="0.01" value={peajes} onChange={(e) => setPeajes(e.target.value)} />
              </div>
              <div className="flex flex-col gap-2">
                <Label>Vehículo</Label>
                <Select
                  items={[
                    { value: "moto", label: etiquetaTipoVehiculo("moto") },
                    { value: "camioneta", label: etiquetaTipoVehiculo("camioneta") },
                  ]}
                  value={tipoVehiculo}
                  onValueChange={(v) => v && setTipoVehiculo(v as TipoVehiculo)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="moto">{etiquetaTipoVehiculo("moto")}</SelectItem>
                    <SelectItem value="camioneta">{etiquetaTipoVehiculo("camioneta")}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="flex items-center gap-2 pt-6">
                <Checkbox id="urgente" checked={urgente} onCheckedChange={(v) => setUrgente(v === true)} />
                <Label htmlFor="urgente">Urgente</Label>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="km-manual">Km manual (opcional)</Label>
                <Input
                  id="km-manual"
                  type="number"
                  step="0.1"
                  min="0"
                  placeholder="Auto (ruta real)"
                  value={kmManual}
                  onChange={(e) => setKmManual(e.target.value)}
                />
              </div>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="observaciones">Observaciones</Label>
              <Input id="observaciones" value={observaciones} onChange={(e) => setObservaciones(e.target.value)} />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="flex flex-col gap-4 pt-6">
            {requiereCotizacionManual && (
              <div className="flex flex-col gap-2 border-b pb-4">
                <p className="text-sm font-medium text-amber-600">
                  {errorCotizar} — fijá un precio manual para poder completar el alta (Anexo I, B9).
                </p>
                <div className="flex flex-col gap-2 max-w-40">
                  <Label htmlFor="precio-manual">Precio manual</Label>
                  <Input
                    id="precio-manual"
                    type="number"
                    min="0.01"
                    step="0.01"
                    value={precioManual}
                    onChange={(e) => setPrecioManual(e.target.value)}
                  />
                </div>
              </div>
            )}
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Estimado</p>
                {cotizando ? (
                  <p className="text-muted-foreground">Calculando…</p>
                ) : errorCotizar && !requiereCotizacionManual ? (
                  <p className="text-sm text-destructive">{errorCotizar}</p>
                ) : cotizacionVisible ? (
                  <div>
                    <p className="text-lg font-semibold">
                      ${cotizacionVisible.total.toLocaleString("es-AR")} en {etiquetaTipoVehiculo(tipoVehiculo)}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Base ${cotizacionVisible.precioBase.toLocaleString("es-AR")}
                      {cotizacionVisible.kmCobrados !== null &&
                        ` · ${cotizacionVisible.kmCobrados.toLocaleString("es-AR")} km (${cotizacionVisible.kmFuente})`}
                      {cotizacionVisible.recargoKm > 0 && ` · +$${cotizacionVisible.recargoKm.toLocaleString("es-AR")} por km`}
                      {cotizacionVisible.recargoUrgencia > 0 &&
                        ` · +$${cotizacionVisible.recargoUrgencia.toLocaleString("es-AR")} urgencia`}
                    </p>
                  </div>
                ) : (
                  <p className="text-muted-foreground">Completá cliente, origen y destino para ver un estimado.</p>
                )}
              </div>
              <Button type="submit" disabled={enviando || !clienteId || !origen || !destino}>
                {enviando ? "Guardando…" : "Crear delivery"}
              </Button>
            </div>
          </CardContent>
        </Card>

        {errorAlta && <p className="text-sm text-destructive">{errorAlta}</p>}
      </form>
    </div>
  );
}
