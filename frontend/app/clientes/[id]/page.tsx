"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ClienteUsuarioCuenta } from "@/lib/dominio/tipos";

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
      <div className="p-8">
        <CabeceraSesion titulo="Cliente" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-2xl flex flex-col gap-6">
      <CabeceraSesion titulo={cliente.razonSocial} />
      <Button variant="outline" render={<Link href="/clientes" />} nativeButton={false} className="self-start">
        ← Clientes
      </Button>

      <DatosCliente cliente={cliente} fetchConSesion={fetchConSesion} onGuardado={cargar} />
      <TarifasCliente cliente={cliente} fetchConSesion={fetchConSesion} onCambio={cargar} />
      <UsuariosCliente clienteId={cliente.id} fetchConSesion={fetchConSesion} />
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
              <TableHead>General camioneta</TableHead>
              <TableHead>Cliente camioneta</TableHead>
              <TableHead></TableHead>
              <TableHead>General moto</TableHead>
              <TableHead>Cliente moto</TableHead>
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

/// Login de consulta del cliente (tabla clientes_usuarios, separada de /usuarios de personal
/// interno a propósito — ver Entidades/ClienteUsuario.cs). Gestiona su propia lista: no es parte
/// de ClienteDetalle porque es un recurso aparte, no un dato del cliente.
function UsuariosCliente({
  clienteId,
  fetchConSesion,
}: {
  clienteId: number;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
}) {
  const [usuarios, setUsuarios] = useState<ClienteUsuarioCuenta[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [nombre, setNombre] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [creando, setCreando] = useState(false);

  const [cambiando, setCambiando] = useState<string | null>(null);
  const [reseteando, setReseteando] = useState<string | null>(null);
  const [passwords, setPasswords] = useState<Record<string, string>>({});

  const cargar = useCallback(() => {
    fetchConSesion(`/api/clientes/${clienteId}/usuarios`)
      .then((r) => leerJson<ClienteUsuarioCuenta[]>(r))
      .then(setUsuarios)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los usuarios."));
  }, [fetchConSesion, clienteId]);

  useEffect(cargar, [cargar]);

  async function crear(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setCreando(true);
    try {
      const resp = await fetchConSesion(`/api/clientes/${clienteId}/usuarios`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ nombre, email, password }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setNombre("");
      setEmail("");
      setPassword("");
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear el usuario.");
    } finally {
      setCreando(false);
    }
  }

  async function alternarActivo(u: ClienteUsuarioCuenta) {
    setCambiando(u.id);
    try {
      await fetchConSesion(`/api/clientes/${clienteId}/usuarios/${u.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !u.activo }),
      });
      cargar();
    } finally {
      setCambiando(null);
    }
  }

  async function resetearPassword(id: string) {
    const nuevaPassword = passwords[id];
    if (!nuevaPassword || nuevaPassword.length < 8) {
      setError("La contraseña debe tener al menos 8 caracteres.");
      return;
    }
    setError(null);
    setReseteando(id);
    try {
      const resp = await fetchConSesion(`/api/clientes/${clienteId}/usuarios/${id}/password`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password: nuevaPassword }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setPasswords((p) => ({ ...p, [id]: "" }));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar la contraseña.");
    } finally {
      setReseteando(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Usuarios (login)</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {error && <p className="text-sm text-destructive">{error}</p>}

        {!usuarios ? (
          error ? null : <p className="text-sm text-muted-foreground">Cargando…</p>
        ) : usuarios.length === 0 ? (
          <p className="text-sm text-muted-foreground">Este cliente todavía no tiene login.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Nueva contraseña</TableHead>
                <TableHead></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {usuarios.map((u) => (
                <TableRow key={u.id}>
                  <TableCell>{u.nombre}</TableCell>
                  <TableCell>{u.email}</TableCell>
                  <TableCell>{u.activo ? "Activo" : "Inactivo"}</TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <Input
                        type="password"
                        placeholder="mín. 8 caracteres"
                        className="w-40"
                        value={passwords[u.id] ?? ""}
                        onChange={(e) => setPasswords((p) => ({ ...p, [u.id]: e.target.value }))}
                      />
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={reseteando === u.id || !passwords[u.id]}
                        onClick={() => resetearPassword(u.id)}
                      >
                        Cambiar
                      </Button>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Button size="sm" variant="outline" disabled={cambiando === u.id} onClick={() => alternarActivo(u)}>
                      {u.activo ? "Desactivar" : "Reactivar"}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}

        <form onSubmit={crear} className="flex items-end gap-2 border-t pt-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="nuevo-usuario-nombre">Nombre</Label>
            <Input id="nuevo-usuario-nombre" required value={nombre} onChange={(e) => setNombre(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="nuevo-usuario-email">Email</Label>
            <Input
              id="nuevo-usuario-email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="nuevo-usuario-password">Contraseña</Label>
            <Input
              id="nuevo-usuario-password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <Button type="submit" disabled={creando}>
            {creando ? "Creando…" : "Agregar login"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
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
        <div className="grid grid-cols-3 gap-4 text-center">
          {(["pago", "trato", "operacion"] as const).map((dim) => (
            <div key={dim} className="rounded-lg border p-3">
              <p className="text-2xl font-semibold">{cliente.contadorEventos[dim] ?? 0}</p>
              <p className="text-xs text-muted-foreground">{DIMENSION_LABEL[dim]}</p>
            </div>
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
