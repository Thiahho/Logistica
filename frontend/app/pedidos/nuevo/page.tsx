"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { ClienteSeleccion, Localidad } from "@/lib/dominio/tipos";

interface UbicacionResuelta {
  id: number;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
}

interface DesglosePrecio {
  precioBase: number;
  recargoUrgencia: number;
  descuentoRuta: number;
  peajes: number;
  total: number;
}

const hoyISO = () => new Date().toISOString().slice(0, 10);

export default function NuevoPedidoPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <FormularioAlta />
    </RequireRole>
  );
}

function FormularioAlta() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [localidades, setLocalidades] = useState<Localidad[]>([]);

  const [clienteId, setClienteId] = useState<number | null>(null);
  const [referenciaCliente, setReferenciaCliente] = useState("");
  const [destinatarioNombre, setDestinatarioNombre] = useState("");
  const [destinatarioTelefono, setDestinatarioTelefono] = useState("");
  const [calleNumero, setCalleNumero] = useState("");
  const [localidadId, setLocalidadId] = useState<number | null>(null);
  const [bultos, setBultos] = useState(1);
  const [pesoKg, setPesoKg] = useState("");
  const [valorDeclarado, setValorDeclarado] = useState("");
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  const [urgente, setUrgente] = useState(false);
  const [peajes, setPeajes] = useState("0");
  const [observaciones, setObservaciones] = useState("");

  const [ubicacion, setUbicacion] = useState<UbicacionResuelta | null>(null);
  const [geocodificando, setGeocodificando] = useState(false);

  const [cotizacion, setCotizacion] = useState<DesglosePrecio | null>(null);
  const [cotizando, setCotizando] = useState(false);
  const [errorCotizar, setErrorCotizar] = useState<string | null>(null);

  const [enviando, setEnviando] = useState(false);
  const [errorAlta, setErrorAlta] = useState<string | null>(null);

  useEffect(() => {
    // /seleccion (no /api/clientes: ese endpoint quedó privativo de administración) — sin
    // colores ni tarifas, que operación no debe ver.
    fetchConSesion("/api/clientes/seleccion").then((r) => r.json()).then(setClientes);
    fetchConSesion("/api/localidades").then((r) => r.json()).then(setLocalidades);
  }, [fetchConSesion]);

  const localidadElegida = useMemo(
    () => localidades.find((l) => l.id === localidadId) ?? null,
    [localidades, localidadId],
  );

  // Geocodifica al salir del campo dirección (construccion_v1.md §4.1). Resolver-o-crear en el
  // backend: repetir esta llamada con la misma dirección no vuelve a pegarle al geocoder.
  async function onBlurDireccion() {
    if (!calleNumero.trim() || !localidadId) return;
    setGeocodificando(true);
    setUbicacion(null);
    try {
      const resp = await fetchConSesion("/api/ubicaciones", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ calleNumero, localidadId, referencia: null }),
      });
      if (resp.ok) setUbicacion(await resp.json());
    } finally {
      setGeocodificando(false);
    }
  }

  // Precio en vivo con debounce mientras se completa el formulario.
  const listoParaCotizar = clienteId !== null && localidadId !== null && fechaEntrega !== "";

  useEffect(() => {
    if (!listoParaCotizar) return;

    const timeout = setTimeout(async () => {
      setCotizando(true);
      setErrorCotizar(null);
      try {
        const resp = await fetchConSesion("/api/pedidos/cotizar", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            clienteId,
            localidadId,
            fechaEntrega,
            urgente,
            peajes: Number(peajes) || 0,
          }),
        });
        if (resp.ok) setCotizacion(await resp.json());
        else {
          setCotizacion(null);
          setErrorCotizar(await resp.text());
        }
      } finally {
        setCotizando(false);
      }
    }, 400);
    return () => clearTimeout(timeout);
  }, [listoParaCotizar, clienteId, localidadId, fechaEntrega, urgente, peajes, fetchConSesion]);

  const cotizacionVisible = listoParaCotizar ? cotizacion : null;

  const direccionDudosa =
    ubicacion !== null && ubicacion.geoConfianza !== "alta" && ubicacion.geoConfianza !== "media";

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!clienteId || !localidadId) return;
    setErrorAlta(null);
    setEnviando(true);
    try {
      // Resolver-o-crear es idempotente: repetirlo acá garantiza el id aunque el operador no
      // haya salido del campo dirección (por ejemplo, tras corregir el resto del formulario).
      const respUbicacion = await fetchConSesion("/api/ubicaciones", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ calleNumero, localidadId, referencia: null }),
      });
      if (!respUbicacion.ok) throw new Error("No se pudo resolver la dirección.");
      const destino: UbicacionResuelta = await respUbicacion.json();

      const resp = await fetchConSesion("/api/pedidos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clienteId,
          referenciaCliente: referenciaCliente || null,
          destinatarioNombre,
          destinatarioTelefono,
          destinoUbicacionId: destino.id,
          bultos,
          pesoKg: pesoKg ? Number(pesoKg) : null,
          valorDeclarado: valorDeclarado ? Number(valorDeclarado) : null,
          fechaEntrega,
          urgente,
          peajes: Number(peajes) || 0,
          observaciones: observaciones || null,
        }),
      });
      if (!resp.ok) throw new Error(await resp.text());
      router.push("/pedidos");
    } catch (err) {
      setErrorAlta(err instanceof Error ? err.message : "No se pudo crear el pedido.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-8 max-w-2xl">
      <CabeceraSesion titulo="Nuevo pedido" />
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Cliente</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label>Cliente</Label>
              <Select
                items={clientes.map((c) => ({ value: String(c.id), label: c.razonSocial }))}
                value={clienteId !== null ? String(clienteId) : null}
                onValueChange={(v) => setClienteId(Number(v))}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Elegir cliente" />
                </SelectTrigger>
                <SelectContent>
                  {clientes.map((c) => (
                    <SelectItem key={c.id} value={String(c.id)}>
                      {c.razonSocial}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
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
            <div className="flex flex-col gap-2">
              <Label>Localidad</Label>
              <Select
                items={localidades.map((l) => ({
                  value: String(l.id),
                  label: l.nombre + (l.zonaId === null ? " (sin zona)" : ""),
                }))}
                value={localidadId !== null ? String(localidadId) : null}
                onValueChange={(v) => setLocalidadId(Number(v))}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Elegir localidad" />
                </SelectTrigger>
                <SelectContent>
                  {localidades.map((l) => (
                    <SelectItem key={l.id} value={String(l.id)}>
                      {l.nombre}
                      {l.zonaId === null ? " (sin zona)" : ""}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {localidadElegida?.zonaId === null && (
                <p className="text-sm text-destructive">
                  Esta localidad no tiene zona asignada: no se puede cotizar ni rutear.
                </p>
              )}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="direccion">Dirección</Label>
              <Input
                id="direccion"
                required
                value={calleNumero}
                onChange={(e) => setCalleNumero(e.target.value)}
                onBlur={onBlurDireccion}
                placeholder="Calle y número"
              />
              {geocodificando && (
                <p className="text-sm text-muted-foreground">Geocodificando…</p>
              )}
              {ubicacion && !geocodificando && (
                <p className={`text-sm ${direccionDudosa ? "text-destructive" : "text-muted-foreground"}`}>
                  {direccionDudosa
                    ? "Dirección sin confirmar: se guarda igual, pero queda marcada como dudosa."
                    : `Ubicación encontrada (confianza ${ubicacion.geoConfianza}).`}
                </p>
              )}
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
                <Input
                  id="peso"
                  type="number"
                  step="0.1"
                  value={pesoKg}
                  onChange={(e) => setPesoKg(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="valor">Valor declarado (opcional)</Label>
                <Input
                  id="valor"
                  type="number"
                  value={valorDeclarado}
                  onChange={(e) => setValorDeclarado(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="peajes">Peajes</Label>
                <Input
                  id="peajes"
                  type="number"
                  step="0.01"
                  value={peajes}
                  onChange={(e) => setPeajes(e.target.value)}
                />
              </div>
              <div className="flex items-center gap-2 pt-6">
                <Checkbox
                  id="urgente"
                  checked={urgente}
                  onCheckedChange={(v) => setUrgente(v === true)}
                />
                <Label htmlFor="urgente">Urgente</Label>
              </div>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="observaciones">Observaciones</Label>
              <Input
                id="observaciones"
                value={observaciones}
                onChange={(e) => setObservaciones(e.target.value)}
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="flex items-center justify-between pt-6">
            <div>
              <p className="text-sm text-muted-foreground">Precio</p>
              {cotizando ? (
                <p className="text-muted-foreground">Calculando…</p>
              ) : errorCotizar ? (
                <p className="text-sm text-destructive">{errorCotizar}</p>
              ) : cotizacionVisible ? (
                <div>
                  <p className="text-2xl font-semibold">
                    ${cotizacionVisible.total.toLocaleString("es-AR")}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    base ${cotizacionVisible.precioBase.toLocaleString("es-AR")}
                    {cotizacionVisible.recargoUrgencia > 0 &&
                      ` · urgencia +$${cotizacionVisible.recargoUrgencia.toLocaleString("es-AR")}`}
                    {cotizacionVisible.peajes > 0 &&
                      ` · peajes $${cotizacionVisible.peajes.toLocaleString("es-AR")}`}
                  </p>
                </div>
              ) : (
                <p className="text-muted-foreground">Elegí cliente y localidad para cotizar.</p>
              )}
            </div>
            <Button type="submit" disabled={enviando || !cotizacionVisible}>
              {enviando ? "Guardando…" : "Crear pedido"}
            </Button>
          </CardContent>
        </Card>

        {errorAlta && <p className="text-sm text-destructive">{errorAlta}</p>}
      </form>
    </div>
  );
}
