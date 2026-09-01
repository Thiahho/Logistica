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
import { SelectorLocalidad, type LocalidadConocida } from "@/components/SelectorLocalidad";
import { SugerenciaDestinatario } from "@/components/SugerenciaDestinatario";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ClienteSeleccion, CotizacionEstimada, DestinatarioFrecuente } from "@/lib/dominio/tipos";

interface UbicacionResuelta {
  id: number;
  lat: number | null;
  lng: number | null;
  geoConfianza: string | null;
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

  const [clienteId, setClienteId] = useState<number | null>(null);
  const [referenciaCliente, setReferenciaCliente] = useState("");
  const [destinatarioNombre, setDestinatarioNombre] = useState("");
  const [destinatarioTelefono, setDestinatarioTelefono] = useState("");
  const [calleNumero, setCalleNumero] = useState("");
  const [localidadId, setLocalidadId] = useState<number | null>(null);
  const [localidadConocida, setLocalidadConocida] = useState<LocalidadConocida | null>(null);
  const [bultos, setBultos] = useState(1);
  const [pesoKg, setPesoKg] = useState("");
  const [valorDeclarado, setValorDeclarado] = useState("");
  const [fechaEntrega, setFechaEntrega] = useState(hoyISO());
  const [urgente, setUrgente] = useState(false);
  const [peajes, setPeajes] = useState("0");
  const [observaciones, setObservaciones] = useState("");

  // La ubicación resuelta (por geocodificación o por elegir una sugerencia de destinatario
  // frecuente) queda atada a la (calle, localidad) para las que vale. `ubicacionVigente`, más
  // abajo, es la derivación de si sigue valiendo para lo que hay en pantalla AHORA — así el
  // efecto de geocodificación nunca necesita resetear estado de forma síncrona (evita
  // react-hooks/set-state-in-effect: editar calle/localidad invalida solo por dejar de matchear,
  // no por un setState de limpieza).
  const [ubicacionResuelta, setUbicacionResuelta] = useState<{
    calleNumero: string;
    localidadId: number;
    ubicacion: UbicacionResuelta;
  } | null>(null);
  const [geocodificando, setGeocodificando] = useState(false);
  const [errorUbicacion, setErrorUbicacion] = useState<string | null>(null);

  const [cotizacion, setCotizacion] = useState<CotizacionEstimada | null>(null);
  const [cotizando, setCotizando] = useState(false);
  const [errorCotizar, setErrorCotizar] = useState<string | null>(null);

  const [enviando, setEnviando] = useState(false);
  const [errorAlta, setErrorAlta] = useState<string | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  useEffect(() => {
    // /seleccion (no /api/clientes: ese endpoint quedó privativo de administración) — sin
    // colores ni tarifas, que operación no debe ver.
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la lista de clientes."));
  }, [fetchConSesion]);

  const calleTrim = calleNumero.trim();
  const ubicacionVigente =
    ubicacionResuelta && ubicacionResuelta.calleNumero === calleTrim && ubicacionResuelta.localidadId === localidadId
      ? ubicacionResuelta.ubicacion
      : null;

  async function resolverUbicacion(calle: string, localidad: number, signal?: AbortSignal): Promise<UbicacionResuelta> {
    const resp = await fetchConSesion("/api/ubicaciones", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ calleNumero: calle, localidadId: localidad, referencia: null }),
      signal,
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
    return resp.json();
  }

  // Geocodifica con debounce al terminar de tipear la dirección O al cambiar la localidad
  // (antes: solo `onBlur` del campo dirección — no se re-disparaba si el operador tipeaba la
  // dirección ANTES de elegir la localidad). 600ms, más largo que el debounce de la cotización
  // (400ms): una dirección nueva puede pegarle a Nominatim, con rate limit de ~1 req/s.
  useEffect(() => {
    if (!calleTrim || localidadId === null) return;
    // Ya resuelto (por geocodificación previa o por una sugerencia elegida): nada que hacer.
    if (ubicacionResuelta?.calleNumero === calleTrim && ubicacionResuelta.localidadId === localidadId) return;

    const abort = new AbortController();
    const timeout = setTimeout(async () => {
      setGeocodificando(true);
      setErrorUbicacion(null);
      try {
        const destino = await resolverUbicacion(calleTrim, localidadId, abort.signal);
        setUbicacionResuelta({ calleNumero: calleTrim, localidadId, ubicacion: destino });
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setErrorUbicacion(err instanceof Error ? err.message : "No se pudo geocodificar la dirección.");
      } finally {
        if (!abort.signal.aborted) setGeocodificando(false);
      }
    }, 600);
    return () => {
      clearTimeout(timeout);
      abort.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- resolverUbicacion no es reactivo (solo usa fetchConSesion, ya en deps)
  }, [calleTrim, localidadId, ubicacionResuelta, fetchConSesion]);

  // Al elegir un destinatario ya usado se prellena todo el bloque de entrega y se reusa la
  // ubicación que ese pedido anterior ya tenía resuelta: cero llamadas al geocoder. Si el
  // operador edita el nombre/teléfono DESPUÉS de elegir, no se invalida nada — la asociación es
  // con la dirección, no con el destinatario (ej.: "misma dirección, ajusto a Juan (portería)").
  function aplicarSugerencia(s: DestinatarioFrecuente) {
    setDestinatarioNombre(s.destinatarioNombre);
    setDestinatarioTelefono(s.destinatarioTelefono);
    setLocalidadId(s.localidadId);
    // zonaId placeholder no-null (no viene en DestinatarioFrecuente): un destinatario repetido
    // solo existe porque un pedido anterior ahí cotizó bien, así que esa localidad ya tenía zona
    // — evita el aviso "sin zona" en falso hasta que una búsqueda real la refresque.
    setLocalidadConocida({ id: s.localidadId, nombre: s.localidadNombre, partido: null, zonaId: -1 });
    setCalleNumero(s.destinoCalleNumero);
    setErrorUbicacion(null);
    setUbicacionResuelta({
      calleNumero: s.destinoCalleNumero,
      localidadId: s.localidadId,
      ubicacion: { id: s.destinoUbicacionId, lat: s.lat, lng: s.lng, geoConfianza: s.geoConfianza },
    });
  }

  // Precio en vivo con debounce mientras se completa el formulario.
  const listoParaCotizar = clienteId !== null && localidadId !== null && fechaEntrega !== "";

  useEffect(() => {
    if (!listoParaCotizar) return;

    // AbortController evita que una cotización lenta resuelva después de una más nueva y
    // pise el precio en pantalla con un valor stale (el usuario tipeando rápido dispara varias).
    const abort = new AbortController();
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
  }, [listoParaCotizar, clienteId, localidadId, fechaEntrega, urgente, peajes, fetchConSesion]);

  const cotizacionVisible = listoParaCotizar ? cotizacion : null;

  const direccionDudosa =
    ubicacionVigente !== null && ubicacionVigente.geoConfianza !== "alta" && ubicacionVigente.geoConfianza !== "media";

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!clienteId || !localidadId) return;
    setErrorAlta(null);
    setEnviando(true);
    try {
      // El efecto de arriba mantiene ubicacionVigente sincronizada con calle/localidad; esto es
      // solo una red de seguridad si el submit gana la carrera al debounce de 600ms.
      const destino = ubicacionVigente ?? (await resolverUbicacion(calleTrim, localidadId));

      const resp = await fetchConSesion("/api/pedidos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clienteId,
          referenciaCliente: referenciaCliente.trim() || null,
          destinatarioNombre: destinatarioNombre.trim(),
          destinatarioTelefono: destinatarioTelefono.trim(),
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
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
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
            <CardTitle className="text-base">Destinatario</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="destinatario">Nombre</Label>
              <SugerenciaDestinatario
                id="destinatario"
                clienteId={clienteId}
                nombre={destinatarioNombre}
                onNombreChange={setDestinatarioNombre}
                onElegirSugerencia={aplicarSugerencia}
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
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label>Localidad</Label>
              <SelectorLocalidad
                value={localidadId}
                onValueChange={(l) => {
                  setLocalidadConocida(l);
                  setLocalidadId(l?.id ?? null);
                }}
                conocida={localidadConocida}
                placeholder="Buscar localidad…"
              />
              {localidadConocida?.zonaId === null && (
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
                placeholder="Calle y número"
              />
              {geocodificando && (
                <p className="text-sm text-muted-foreground">Geocodificando…</p>
              )}
              {errorUbicacion && !geocodificando && (
                <p className="text-sm text-destructive">{errorUbicacion}</p>
              )}
              {ubicacionVigente && !geocodificando && !errorUbicacion && (
                <p className={`text-sm ${direccionDudosa ? "text-destructive" : "text-muted-foreground"}`}>
                  {direccionDudosa
                    ? "Dirección sin confirmar: se guarda igual, pero queda marcada como dudosa."
                    : `Ubicación encontrada (confianza ${ubicacionVigente.geoConfianza}).`}
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
              <p className="text-sm text-muted-foreground">Estimado</p>
              {cotizando ? (
                <p className="text-muted-foreground">Calculando…</p>
              ) : errorCotizar ? (
                <p className="text-sm text-destructive">{errorCotizar}</p>
              ) : cotizacionVisible ? (
                <div>
                  <p className="text-lg font-semibold">
                    {cotizacionVisible.camioneta
                      ? `$${cotizacionVisible.camioneta.total.toLocaleString("es-AR")} en camioneta`
                      : "Sin tarifa de camioneta"}
                    {" · "}
                    {cotizacionVisible.moto
                      ? `$${cotizacionVisible.moto.total.toLocaleString("es-AR")} en moto`
                      : "sin tarifa de moto"}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    Estimado, no es el precio final: se fija cuando se arme la ruta y se sepa qué
                    vehículo lo lleva.
                  </p>
                </div>
              ) : (
                <p className="text-muted-foreground">Elegí cliente y localidad para ver un estimado.</p>
              )}
            </div>
            <Button type="submit" disabled={enviando || !clienteId || !localidadId}>
              {enviando ? "Guardando…" : "Crear pedido"}
            </Button>
          </CardContent>
        </Card>

        {errorAlta && <p className="text-sm text-destructive">{errorAlta}</p>}
      </form>
    </div>
  );
}
