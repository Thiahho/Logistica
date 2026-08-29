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

type Color = "verde" | "amarillo" | "rojo";
const COLORES: Color[] = ["verde", "amarillo", "rojo"];

interface TarifaZona {
  zonaId: number;
  zonaCodigo: string;
  zonaNombre: string;
  precioGeneral: number | null;
  precioCliente: number | null;
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

  const cargar = useCallback(() => {
    fetchConSesion(`/api/clientes/${id}`)
      .then((r) => r.json())
      .then(setCliente);
  }, [fetchConSesion, id]);

  useEffect(() => {
    cargar();
    fetchConSesion("/api/tipos-evento-cliente")
      .then((r) => r.json())
      .then(setTiposEvento);
  }, [cargar, fetchConSesion]);

  if (!cliente) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Cliente" />
        <p className="text-muted-foreground">Cargando…</p>
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
  const [valores, setValores] = useState<Record<number, string>>({});
  const [guardandoZona, setGuardandoZona] = useState<number | null>(null);

  function valorZona(t: TarifaZona) {
    return valores[t.zonaId] ?? (t.precioCliente !== null ? String(t.precioCliente) : "");
  }

  async function fijar(zonaId: number, precio: number | null) {
    setGuardandoZona(zonaId);
    try {
      await fetchConSesion(`/api/clientes/${cliente.id}/tarifas/${zonaId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ precio }),
      });
      onCambio();
    } finally {
      setGuardandoZona(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Tarifas por zona</CardTitle>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Zona</TableHead>
              <TableHead>Precio general</TableHead>
              <TableHead>Precio del cliente</TableHead>
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
                  {t.precioGeneral !== null ? `$${t.precioGeneral.toLocaleString("es-AR")}` : "—"}
                </TableCell>
                <TableCell>
                  <Input
                    type="number"
                    step="0.01"
                    className="w-32"
                    placeholder="—"
                    value={valorZona(t)}
                    onChange={(e) => setValores((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                  />
                </TableCell>
                <TableCell className="flex gap-2">
                  <Button
                    size="sm"
                    disabled={guardandoZona === t.zonaId || valorZona(t) === ""}
                    onClick={() => fijar(t.zonaId, Number(valorZona(t)))}
                  >
                    Fijar
                  </Button>
                  {t.precioCliente !== null && (
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={guardandoZona === t.zonaId}
                      onClick={() => fijar(t.zonaId, null)}
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
