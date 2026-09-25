"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { AvisoCobranzaDialog } from "@/components/AvisoCobranzaDialog";
import { RangoCliente } from "./RangoCliente";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { UsuariosClienteGestion } from "@/components/UsuariosClienteGestion";
import { PagosInformadosRevision } from "@/components/PagosInformadosRevision";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import {
  etiquetaCiclo,
  etiquetaEstadoFactura,
  etiquetaTipoVehiculo,
  type CicloFacturacion,
  type CuentaCorrienteCliente as CuentaCorrienteClienteDatos,
} from "@/lib/dominio/tipos";

type Color = "verde" | "amarillo" | "rojo";
const COLORES: Color[] = ["verde", "amarillo", "rojo"];

/** Por zona x tipo de vehículo (acta changelog 3.11): camioneta y moto tienen tarifa propia. */
interface TarifaZona {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  precioGeneralCamioneta: number | null;
  precioClienteCamioneta: number | null;
  precioGeneralMoto: number | null;
  precioClienteMoto: number | null;
}

interface EventoResumen {
  id: number;
  tipoCodigo: string;
  tipoDescripcion: string;
  dimension: string;
  valorNum: number | null;
  nota: string | null;
  ocurridoEn: string;
  registradoPorNombre: string | null;
}

interface ClienteDetalle {
  id: number;
  razonSocial: string;
  cuit: string | null;
  contacto: string | null;
  telefono: string | null;
  email: string | null;
  activo: boolean;
  colorPago: Color;
  colorTrato: Color;
  colorOper: Color;
  cicloFacturacion: CicloFacturacion;
  saldoCliente: number;
  deudaVencida: number;
  tarifas: TarifaZona[];
  contadorEventos: Record<string, number>;
  ultimosEventos: EventoResumen[];
}

interface TipoEvento {
  id: number;
  codigo: string;
  dimension: string;
  descripcion: string;
}

const DIMENSION_LABEL: Record<string, string> = {
  pago: "Pago",
  trato: "Trato",
  operacion: "Operación",
};

export default function ClienteDetallePage() {
  return (
    <RequireRole roles={["administracion"]}>
      <DetalleCliente />
    </RequireRole>
  );
}

function DetalleCliente() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();

  const [cliente, setCliente] = useState<ClienteDetalle | null>(null);
  const [tiposEvento, setTiposEvento] = useState<TipoEvento[]>([]);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  // Sube al confirmar un pago informado: remonta la cuenta corriente para que muestre el saldo nuevo.
  const [versionCuenta, setVersionCuenta] = useState(0);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/clientes/${id}`)
      .then((r) => leerJson<ClienteDetalle>(r))
      .then(setCliente)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el cliente."));
  }, [fetchConSesion, id]);

  useEffect(() => {
    cargar();
    fetchConSesion("/api/tipos-evento-cliente")
      .then((r) => leerJson<TipoEvento[]>(r))
      .then(setTiposEvento)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar los tipos de evento."));
  }, [cargar, fetchConSesion]);

  if (!cliente) {
    return (
      <div className="p-4 md:p-8">
        <CabeceraSesion titulo="Cliente" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-8 max-w-2xl flex flex-col gap-6">
      <CabeceraSesion titulo={cliente.razonSocial} />
      <Button variant="outline" render={<Link href="/clientes" />} nativeButton={false} className="self-start">
        ← Clientes
      </Button>

      <DatosCliente cliente={cliente} fetchConSesion={fetchConSesion} onGuardado={cargar} />
      <TarifasCliente cliente={cliente} fetchConSesion={fetchConSesion} onCambio={cargar} />
      <RangoCliente clienteId={cliente.id} />
      <CuentaCorrienteCliente
        key={versionCuenta}
        clienteId={cliente.id}
        fetchConSesion={fetchConSesion}
        onAvisoEnviado={cargar}
      />
      <PagosInformadosRevision
        ruta={`/api/clientes/${cliente.id}/pagos-informados`}
        onCambio={() => setVersionCuenta((v) => v + 1)}
        ocultarSiVacio
      />
      <UsuariosClienteGestion
        basePath={`/api/clientes/${cliente.id}/usuarios`}
        titulo="Usuarios del portal"
        elegirRol
        textoVacio="Este cliente todavía no tiene login."
      />
      <EventosCliente
        cliente={cliente}
        tiposEvento={tiposEvento}
        fetchConSesion={fetchConSesion}
        onRegistrado={cargar}
      />
      <ZonaPeligro cliente={cliente} fetchConSesion={fetchConSesion} onCambio={cargar} />
    </div>
  );
}

function ZonaPeligro({
  cliente,
  fetchConSesion,
  onCambio,
}: {
  cliente: ClienteDetalle;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onCambio: () => void;
}) {
  const router = useRouter();
  const [cambiandoActivo, setCambiandoActivo] = useState(false);
  const [eliminando, setEliminando] = useState(false);
  const [errorEliminar, setErrorEliminar] = useState<string | null>(null);

  async function alternarActivo() {
    setCambiandoActivo(true);
    try {
      await fetchConSesion(`/api/clientes/${cliente.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !cliente.activo }),
      });
      onCambio();
    } finally {
      setCambiandoActivo(false);
    }
  }

  async function eliminar() {
    setErrorEliminar(null);
    setEliminando(true);
    try {
      const resp = await fetchConSesion(`/api/clientes/${cliente.id}`, { method: "DELETE" });
      if (!resp.ok) throw new Error(await resp.text());
      router.push("/clientes");
    } catch (err) {
      setErrorEliminar(err instanceof Error ? err.message : "No se pudo eliminar el cliente.");
    } finally {
      setEliminando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Zona de riesgo</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <p className="text-sm text-muted-foreground">
            {cliente.activo
              ? "Deja de aparecer como opción para pedidos nuevos; su historial queda intacto."
              : "Este cliente está inactivo."}
          </p>
          <Button variant="outline" disabled={cambiandoActivo} onClick={alternarActivo}>
            {cliente.activo ? "Desactivar" : "Reactivar"}
          </Button>
        </div>
        <div className="flex items-center justify-between border-t pt-3">
          <p className="text-sm text-muted-foreground">
            Solo se puede eliminar si no tiene pedidos, tarifas ni eventos cargados.
          </p>
          <Button variant="destructive" disabled={eliminando} onClick={eliminar}>
            {eliminando ? "Eliminando…" : "Eliminar cliente"}
          </Button>
        </div>
        {errorEliminar && <p className="text-sm text-destructive">{errorEliminar}</p>}
      </CardContent>
    </Card>
  );
}

function DatosCliente({
  cliente,
  fetchConSesion,
  onGuardado,
}: {
  cliente: ClienteDetalle;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onGuardado: () => void;
}) {
  const [razonSocial, setRazonSocial] = useState(cliente.razonSocial);
  const [cuit, setCuit] = useState(cliente.cuit ?? "");
  const [contacto, setContacto] = useState(cliente.contacto ?? "");
  const [telefono, setTelefono] = useState(cliente.telefono ?? "");
  const [email, setEmail] = useState(cliente.email ?? "");
  const [activo, setActivo] = useState(cliente.activo);
  const [colorPago, setColorPago] = useState<Color>(cliente.colorPago);
  const [colorTrato, setColorTrato] = useState<Color>(cliente.colorTrato);
  const [colorOper, setColorOper] = useState<Color>(cliente.colorOper);
  const [cicloFacturacion, setCicloFacturacion] = useState<CicloFacturacion>(cliente.cicloFacturacion);
  const [guardando, setGuardando] = useState(false);

  async function guardar() {
    setGuardando(true);
    try {
      await fetchConSesion(`/api/clientes/${cliente.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          razonSocial,
          cuit: cuit || null,
          contacto: contacto || null,
          telefono: telefono || null,
          email: email || null,
          activo,
          colorPago,
          colorTrato,
          colorOper,
          cicloFacturacion,
        }),
      });
      onGuardado();
    } finally {
      setGuardando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Datos</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="razonSocial">Razón social</Label>
          <Input id="razonSocial" value={razonSocial} onChange={(e) => setRazonSocial(e.target.value)} />
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="cuit">CUIT</Label>
            <Input id="cuit" value={cuit} onChange={(e) => setCuit(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="contacto">Contacto</Label>
            <Input id="contacto" value={contacto} onChange={(e) => setContacto(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="telefono">Teléfono</Label>
            <Input id="telefono" value={telefono} onChange={(e) => setTelefono(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="email">Email</Label>
            <Input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Checkbox id="activo" checked={activo} onCheckedChange={(v) => setActivo(v === true)} />
          <Label htmlFor="activo">Activo</Label>
        </div>

        <div className="flex flex-col gap-2 pt-2 border-t max-w-48">
          <Label>Ciclo de facturación</Label>
          <Select
            items={[
              { value: "mensual", label: etiquetaCiclo("mensual") },
              { value: "quincenal", label: etiquetaCiclo("quincenal") },
            ]}
            value={cicloFacturacion}
            onValueChange={(v) => v && setCicloFacturacion(v as CicloFacturacion)}
          >
            <SelectTrigger className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="mensual">{etiquetaCiclo("mensual")}</SelectItem>
              <SelectItem value="quincenal">{etiquetaCiclo("quincenal")}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="grid grid-cols-3 gap-4 pt-2 border-t">
          <SelectorColor label="Pago" value={colorPago} onChange={setColorPago} />
          <SelectorColor label="Trato" value={colorTrato} onChange={setColorTrato} />
          <SelectorColor label="Operación" value={colorOper} onChange={setColorOper} />
        </div>

        <Button onClick={guardar} disabled={guardando} className="self-start mt-2">
          {guardando ? "Guardando…" : "Guardar"}
        </Button>
      </CardContent>
    </Card>
  );
}

function SelectorColor({
  label,
  value,
  onChange,
}: {
  label: string;
  value: Color;
  onChange: (c: Color) => void;
}) {
  return (
    <div className="flex flex-col gap-2">
      <Label>{label}</Label>
      <Select
        items={COLORES.map((c) => ({ value: c, label: c }))}
        value={value}
        onValueChange={(v) => v && onChange(v as Color)}
      >
        <SelectTrigger className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {COLORES.map((c) => (
            <SelectItem key={c} value={c}>
              {c}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

function TarifasCliente({
  cliente,
  fetchConSesion,
  onCambio,
}: {
  cliente: ClienteDetalle;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onCambio: () => void;
}) {
  const [valores, setValores] = useState<Record<string, string>>({});
  const [guardando, setGuardando] = useState<string | null>(null);

  const clave = (zonaId: number, tipo: "camioneta" | "moto") => `${zonaId}:${tipo}`;

  function valorZona(t: TarifaZona, tipo: "camioneta" | "moto") {
    const precioCliente = tipo === "camioneta" ? t.precioClienteCamioneta : t.precioClienteMoto;
    return valores[clave(t.zonaId, tipo)] ?? (precioCliente !== null ? String(precioCliente) : "");
  }

  async function fijar(zonaId: number, tipo: "camioneta" | "moto", precio: number | null) {
    setGuardando(clave(zonaId, tipo));
    try {
      await fetchConSesion(`/api/clientes/${cliente.id}/tarifas/${zonaId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tipoVehiculo: tipo, precio }),
      });
      onCambio();
    } finally {
      setGuardando(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Tarifas por zona</CardTitle>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Zona</TableHead>
              <TableHead>General {etiquetaTipoVehiculo("camioneta")}</TableHead>
              <TableHead>Cliente {etiquetaTipoVehiculo("camioneta")}</TableHead>
              <TableHead></TableHead>
              <TableHead>General {etiquetaTipoVehiculo("moto")}</TableHead>
              <TableHead>Cliente {etiquetaTipoVehiculo("moto")}</TableHead>
              <TableHead></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {cliente.tarifas.map((t) => (
              <TableRow key={t.zonaId}>
                <TableCell>
                  {t.zonaCodigo} — {t.zonaNombre}
                </TableCell>
                <TableCell>
                  {t.precioGeneralCamioneta !== null ? `$${t.precioGeneralCamioneta.toLocaleString("es-AR")}` : "—"}
                </TableCell>
                <TableCell>
                  <Input
                    type="number"
                    step="0.01"
                    className="w-28"
                    placeholder="—"
                    value={valorZona(t, "camioneta")}
                    onChange={(e) => setValores((v) => ({ ...v, [clave(t.zonaId, "camioneta")]: e.target.value }))}
                  />
                </TableCell>
                <TableCell className="flex gap-2">
                  <Button
                    size="sm"
                    disabled={guardando === clave(t.zonaId, "camioneta") || valorZona(t, "camioneta") === ""}
                    onClick={() => fijar(t.zonaId, "camioneta", Number(valorZona(t, "camioneta")))}
                  >
                    Fijar
                  </Button>
                  {t.precioClienteCamioneta !== null && (
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={guardando === clave(t.zonaId, "camioneta")}
                      onClick={() => fijar(t.zonaId, "camioneta", null)}
                    >
                      Quitar
                    </Button>
                  )}
                </TableCell>
                <TableCell>
                  {t.precioGeneralMoto !== null ? `$${t.precioGeneralMoto.toLocaleString("es-AR")}` : "—"}
                </TableCell>
                <TableCell>
                  <Input
                    type="number"
                    step="0.01"
                    className="w-28"
                    placeholder="—"
                    value={valorZona(t, "moto")}
                    onChange={(e) => setValores((v) => ({ ...v, [clave(t.zonaId, "moto")]: e.target.value }))}
                  />
                </TableCell>
                <TableCell className="flex gap-2">
                  <Button
                    size="sm"
                    disabled={guardando === clave(t.zonaId, "moto") || valorZona(t, "moto") === ""}
                    onClick={() => fijar(t.zonaId, "moto", Number(valorZona(t, "moto")))}
                  >
                    Fijar
                  </Button>
                  {t.precioClienteMoto !== null && (
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={guardando === clave(t.zonaId, "moto")}
                      onClick={() => fijar(t.zonaId, "moto", null)}
                    >
                      Quitar
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

const MEDIOS_PAGO = [
  { value: "transferencia", label: "Transferencia" },
  { value: "efectivo", label: "Efectivo" },
  { value: "cheque", label: "Cheque" },
  { value: "otro", label: "Otro" },
] as const;

/// E1 (Anexo I §5, B1). Gestiona su propio fetch — mismo criterio que UsuariosCliente: es un
/// recurso aparte (facturas, pagos), no un dato plano del cliente.
function CuentaCorrienteCliente({
  clienteId,
  fetchConSesion,
  onAvisoEnviado,
}: {
  clienteId: number;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  /** Para que "Últimos eventos" (ClienteDetalle, fuera de esta Card) refleje el aviso recién
   * registrado — el evento vive en el padre, esta Card solo dispara el envío. */
  onAvisoEnviado?: () => void;
}) {
  const [cuenta, setCuenta] = useState<CuentaCorrienteClienteDatos | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [idsAviso, setIdsAviso] = useState<number[] | null>(null);

  const [monto, setMonto] = useState("");
  const [fechaPago, setFechaPago] = useState("");
  const [medio, setMedio] = useState<string>("transferencia");
  const [nota, setNota] = useState("");
  const [registrando, setRegistrando] = useState(false);

  const [suspenderHasta, setSuspenderHasta] = useState("");
  const [suspenderMotivo, setSuspenderMotivo] = useState("");
  const [guardandoSuspension, setGuardandoSuspension] = useState(false);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/clientes/${clienteId}/cuenta-corriente`)
      .then((r) => leerJson<CuentaCorrienteClienteDatos>(r))
      .then(setCuenta)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la cuenta corriente."));
  }, [fetchConSesion, clienteId]);

  useEffect(cargar, [cargar]);

  async function registrarPago() {
    if (!monto || Number(monto) <= 0) {
      setError("Ingresá un monto mayor a cero.");
      return;
    }
    setError(null);
    setRegistrando(true);
    try {
      const resp = await fetchConSesion(`/api/clientes/${clienteId}/pagos`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ monto: Number(monto), fechaPago: fechaPago || null, medio, nota: nota || null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setMonto("");
      setFechaPago("");
      setNota("");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo registrar el pago.");
    } finally {
      setRegistrando(false);
    }
  }

  async function suspenderCorte() {
    if (!suspenderHasta || !suspenderMotivo.trim()) {
      setError("Un plan de cuotas necesita fecha y motivo.");
      return;
    }
    setError(null);
    setGuardandoSuspension(true);
    try {
      const resp = await fetchConSesion(`/api/clientes/${clienteId}/corte-suspendido`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ hasta: suspenderHasta, motivo: suspenderMotivo }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setSuspenderHasta("");
      setSuspenderMotivo("");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo suspender el corte.");
    } finally {
      setGuardandoSuspension(false);
    }
  }

  async function levantarSuspension() {
    setError(null);
    setGuardandoSuspension(true);
    try {
      const resp = await fetchConSesion(`/api/clientes/${clienteId}/corte-suspendido`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ hasta: null, motivo: null }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo levantar la suspensión.");
    } finally {
      setGuardandoSuspension(false);
    }
  }

  return (
    <>
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Cuenta corriente</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {error && <p className="text-sm text-destructive">{error}</p>}

        {!cuenta ? (
          error ? null : <p className="text-sm text-muted-foreground">Cargando…</p>
        ) : (
          <>
            <div className="grid grid-cols-3 gap-4">
              <TarjetaMetrica valor={`$${cuenta.saldo.toLocaleString("es-AR")}`} etiqueta="Saldo" />
              <TarjetaMetrica
                valor={`$${cuenta.deudaVencida.toLocaleString("es-AR")}`}
                etiqueta="Deuda vencida"
                tono={cuenta.deudaVencida > 0 ? "alerta" : "normal"}
              />
              <TarjetaMetrica
                chico
                valor={cuenta.servicioCortado ? "Servicio cortado" : "Al día"}
                tono={cuenta.servicioCortado ? "alerta" : "normal"}
                etiqueta={cuenta.corteSuspendidoHasta ? `Plan de cuotas hasta ${cuenta.corteSuspendidoHasta}` : undefined}
              />
            </div>

            {esClienteCritico(cuenta) && (
              <Button variant="outline" size="sm" className="self-start" onClick={() => setIdsAviso([clienteId])}>
                Enviar aviso de cobranza
              </Button>
            )}

            {cuenta.pendienteDeFacturar > 0 && (
              <p className="text-xs text-muted-foreground">
                Pendiente de facturar: ${cuenta.pendienteDeFacturar.toLocaleString("es-AR")}
                {cuenta.ajustesPendientes > 0 &&
                  ` · ${cuenta.ajustesPendientes} ajuste${cuenta.ajustesPendientes === 1 ? "" : "s"} sin aprobar`}
                {" — "}
                <Link href={`/facturas?clienteId=${clienteId}`} className="hover:underline">
                  ver facturas
                </Link>
              </p>
            )}

            {cuenta.facturas.length > 0 && (
              <div className="border-t pt-4">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Período</TableHead>
                      <TableHead>Vencimiento</TableHead>
                      <TableHead>Total</TableHead>
                      <TableHead>Saldo</TableHead>
                      <TableHead>Estado</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {cuenta.facturas.map((f) => (
                      <TableRow key={f.id} className={f.estado === "vencida" ? "bg-destructive/10" : undefined}>
                        <TableCell>
                          {f.periodoDesde} al {f.periodoHasta}
                        </TableCell>
                        <TableCell>{f.fechaVencimiento}</TableCell>
                        <TableCell>${f.total.toLocaleString("es-AR")}</TableCell>
                        <TableCell>${f.saldo.toLocaleString("es-AR")}</TableCell>
                        <TableCell>
                          <span className={f.estado === "vencida" ? "text-destructive font-medium" : undefined}>
                            {etiquetaEstadoFactura(f.estado)}
                          </span>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}

            <div className="flex flex-col gap-2 border-t pt-4">
              <Label>Registrar pago</Label>
              <div className="flex flex-wrap items-end gap-2">
                <Input
                  type="number"
                  step="0.01"
                  placeholder="Monto"
                  className="w-32"
                  value={monto}
                  onChange={(e) => setMonto(e.target.value)}
                />
                <Input type="date" className="w-40" value={fechaPago} onChange={(e) => setFechaPago(e.target.value)} />
                <Select items={MEDIOS_PAGO.map((m) => m)} value={medio} onValueChange={(v) => v && setMedio(v)}>
                  <SelectTrigger className="w-36">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MEDIOS_PAGO.map((m) => (
                      <SelectItem key={m.value} value={m.value}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Input placeholder="Nota (opcional)" className="w-40" value={nota} onChange={(e) => setNota(e.target.value)} />
                <Button onClick={registrarPago} disabled={registrando || !monto}>
                  {registrando ? "Registrando…" : "Registrar"}
                </Button>
              </div>
            </div>

            <div className="flex flex-col gap-2 border-t pt-4">
              <Label>Plan de cuotas (§10.2-L4)</Label>
              <p className="text-xs text-muted-foreground">
                Mientras esté activo, el corte por deuda vencida no bloquea altas nuevas. Si no se
                extiende con la próxima cuota, el corte vuelve solo.
              </p>
              <div className="flex flex-wrap items-end gap-2">
                <Input
                  type="date"
                  className="w-40"
                  value={suspenderHasta}
                  onChange={(e) => setSuspenderHasta(e.target.value)}
                />
                <Input
                  placeholder="Motivo"
                  className="w-56"
                  value={suspenderMotivo}
                  onChange={(e) => setSuspenderMotivo(e.target.value)}
                />
                <Button
                  variant="outline"
                  disabled={guardandoSuspension || !suspenderHasta || !suspenderMotivo}
                  onClick={suspenderCorte}
                >
                  {guardandoSuspension ? "Guardando…" : "Suspender corte"}
                </Button>
                {cuenta.corteSuspendidoHasta && (
                  <Button variant="outline" disabled={guardandoSuspension} onClick={levantarSuspension}>
                    Levantar suspensión
                  </Button>
                )}
              </div>
              {cuenta.corteSuspendidoMotivo && (
                <p className="text-xs text-muted-foreground">
                  {cuenta.corteSuspendidoMotivo} — {cuenta.corteSuspendidoPorNombre ?? "—"}
                  {cuenta.corteSuspendidoEn && ` · ${new Date(cuenta.corteSuspendidoEn).toLocaleString("es-AR")}`}
                </p>
              )}
            </div>

            {cuenta.pagos.length > 0 && (
              <div className="flex flex-col gap-2 border-t pt-4">
                <Label>Pagos registrados</Label>
                <ul className="flex flex-col gap-2">
                  {cuenta.pagos.map((p) => (
                    <li key={p.id} className="text-sm border-b pb-2 flex justify-between gap-4">
                      <span>
                        {MEDIOS_PAGO.find((m) => m.value === p.medio)?.label ?? p.medio}
                        {p.nota && <span className="text-muted-foreground"> · {p.nota}</span>}
                        <div className="text-xs text-muted-foreground">
                          {p.fechaPago} · {p.registradoPorNombre ?? "—"}
                        </div>
                      </span>
                      <span className="shrink-0 font-medium">${p.monto.toLocaleString("es-AR")}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>

    <AvisoCobranzaDialog
      clienteIds={idsAviso}
      onOpenChange={(open) => !open && setIdsAviso(null)}
      onEnviado={() => {
        cargar();
        onAvisoEnviado?.();
      }}
    />
    </>
  );
}

/** Heurística de UI para mostrar el botón de aviso — no autoritativa: la previsualización del
 * propio diálogo (server-side, CuentaCorrienteService.RiesgoAsync) es la que de verdad decide
 * si el cliente tiene algo para avisar; acá solo evita mostrar el botón cuando obviamente no
 * hace falta. */
function esClienteCritico(cuenta: CuentaCorrienteClienteDatos): boolean {
  if (cuenta.deudaVencida > 0) return true;
  const limite = Date.now() + 15 * 24 * 60 * 60 * 1000;
  return cuenta.facturas.some((f) => f.saldo > 0 && new Date(f.fechaVencimiento).getTime() <= limite);
}

function EventosCliente({
  cliente,
  tiposEvento,
  fetchConSesion,
  onRegistrado,
}: {
  cliente: ClienteDetalle;
  tiposEvento: TipoEvento[];
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onRegistrado: () => void;
}) {
  const [tipoId, setTipoId] = useState<number | null>(null);
  const [valorNum, setValorNum] = useState("");
  const [nota, setNota] = useState("");
  const [enviando, setEnviando] = useState(false);

  async function registrar() {
    if (!tipoId) return;
    setEnviando(true);
    try {
      await fetchConSesion(`/api/clientes/${cliente.id}/eventos`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          tipoId,
          valorNum: valorNum ? Number(valorNum) : null,
          nota: nota || null,
          pedidoId: null,
        }),
      });
      setTipoId(null);
      setValorNum("");
      setNota("");
      onRegistrado();
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Eventos</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="grid grid-cols-3 gap-4">
          {(["pago", "trato", "operacion"] as const).map((dim) => (
            <TarjetaMetrica key={dim} valor={cliente.contadorEventos[dim] ?? 0} etiqueta={DIMENSION_LABEL[dim]} />
          ))}
        </div>

        <div className="flex flex-col gap-2 border-t pt-4">
          <Label>Registrar evento</Label>
          <div className="flex gap-2">
            <Select
              items={tiposEvento.map((t) => ({
                value: String(t.id),
                label: `${DIMENSION_LABEL[t.dimension] ?? t.dimension} — ${t.descripcion}`,
              }))}
              value={tipoId !== null ? String(tipoId) : null}
              onValueChange={(v) => setTipoId(v ? Number(v) : null)}
            >
              <SelectTrigger className="flex-1">
                <SelectValue placeholder="Elegir tipo de evento" />
              </SelectTrigger>
              <SelectContent>
                {tiposEvento.map((t) => (
                  <SelectItem key={t.id} value={String(t.id)}>
                    {DIMENSION_LABEL[t.dimension] ?? t.dimension} — {t.descripcion}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input
              type="number"
              placeholder="Valor (opcional)"
              className="w-36"
              value={valorNum}
              onChange={(e) => setValorNum(e.target.value)}
            />
          </div>
          <Input placeholder="Nota (opcional)" value={nota} onChange={(e) => setNota(e.target.value)} />
          <Button onClick={registrar} disabled={!tipoId || enviando} className="self-start">
            {enviando ? "Registrando…" : "Registrar"}
          </Button>
        </div>

        <div className="flex flex-col gap-2 border-t pt-4">
          <Label>Últimos eventos</Label>
          {cliente.ultimosEventos.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no hay eventos registrados.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {cliente.ultimosEventos.map((e) => (
                <li key={e.id} className="text-sm border-b pb-2">
                  <span className="font-medium">{DIMENSION_LABEL[e.dimension] ?? e.dimension}</span>
                  {" — "}
                  {e.tipoDescripcion}
                  {e.valorNum !== null && ` (${e.valorNum})`}
                  {e.nota && <span className="text-muted-foreground"> · {e.nota}</span>}
                  <div className="text-xs text-muted-foreground">
                    {new Date(e.ocurridoEn).toLocaleString("es-AR")}
                    {e.registradoPorNombre && ` · ${e.registradoPorNombre}`}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
