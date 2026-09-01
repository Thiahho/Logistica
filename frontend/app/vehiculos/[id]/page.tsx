"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { leerJson } from "@/lib/api/errores";
import type { TipoVehiculo, Vehiculo } from "@/lib/dominio/tipos";

export default function VehiculoDetallePage() {
  return (
    <RequireRole roles={["administracion"]}>
      <DetalleVehiculo />
    </RequireRole>
  );
}

function DetalleVehiculo() {
  const { id } = useParams<{ id: string }>();
  const { fetchConSesion } = useAuth();

  const [vehiculo, setVehiculo] = useState<Vehiculo | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const cargar = useCallback(() => {
    fetchConSesion(`/api/vehiculos/${id}`)
      .then((r) => leerJson<Vehiculo>(r))
      .then(setVehiculo)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar el vehículo."));
  }, [fetchConSesion, id]);

  useEffect(cargar, [cargar]);

  if (!vehiculo) {
    return (
      <div className="p-8">
        <CabeceraSesion titulo="Vehículo" />
        <p className={errorCarga ? "text-sm text-destructive" : "text-muted-foreground"}>
          {errorCarga ?? "Cargando…"}
        </p>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-md flex flex-col gap-6">
      <CabeceraSesion titulo={vehiculo.patente} />
      <Button variant="outline" render={<Link href="/vehiculos" />} nativeButton={false} className="self-start">
        ← Vehículos
      </Button>

      <DatosVehiculo vehiculo={vehiculo} fetchConSesion={fetchConSesion} onGuardado={cargar} />
      <ZonaPeligro vehiculo={vehiculo} fetchConSesion={fetchConSesion} onCambio={cargar} />
    </div>
  );
}

function DatosVehiculo({
  vehiculo,
  fetchConSesion,
  onGuardado,
}: {
  vehiculo: Vehiculo;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onGuardado: () => void;
}) {
  const [patente, setPatente] = useState(vehiculo.patente);
  const [tipo, setTipo] = useState<TipoVehiculo>(vehiculo.tipo);
  const [descripcion, setDescripcion] = useState(vehiculo.descripcion ?? "");
  const [marca, setMarca] = useState(vehiculo.marca ?? "");
  const [modelo, setModelo] = useState(vehiculo.modelo ?? "");
  const [anio, setAnio] = useState(vehiculo.anio !== null ? String(vehiculo.anio) : "");
  const [kmActual, setKmActual] = useState(vehiculo.kmActual !== null ? String(vehiculo.kmActual) : "");
  const [venceVtv, setVenceVtv] = useState(vehiculo.venceVtv ?? "");
  const [venceSeguro, setVenceSeguro] = useState(vehiculo.venceSeguro ?? "");
  const [costoKm, setCostoKm] = useState(vehiculo.costoKm !== null ? String(vehiculo.costoKm) : "");
  const [capacidadParadas, setCapacidadParadas] = useState(vehiculo.capacidadParadas);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function guardar() {
    setError(null);
    setGuardando(true);
    try {
      const resp = await fetchConSesion(`/api/vehiculos/${vehiculo.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          patente,
          tipo,
          descripcion: descripcion || null,
          marca: marca || null,
          modelo: modelo || null,
          anio: anio ? Number(anio) : null,
          kmActual: kmActual ? Number(kmActual) : null,
          venceVtv: venceVtv || null,
          venceSeguro: venceSeguro || null,
          costoKm: costoKm ? Number(costoKm) : null,
          capacidadParadas,
          activo: vehiculo.activo,
        }),
      });
      if (!resp.ok) throw new Error(await resp.text());
      onGuardado();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el vehículo.");
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
          <Label htmlFor="patente">Patente</Label>
          <Input id="patente" value={patente} onChange={(e) => setPatente(e.target.value)} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="descripcion">Descripción</Label>
          <Input id="descripcion" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="tipo">Tipo</Label>
          <Select value={tipo} onValueChange={(v) => setTipo(v as TipoVehiculo)}>
            <SelectTrigger id="tipo">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="camioneta">Camioneta</SelectItem>
              <SelectItem value="moto">Moto</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="marca">Marca</Label>
            <Input id="marca" value={marca} onChange={(e) => setMarca(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="modelo">Modelo</Label>
            <Input id="modelo" value={modelo} onChange={(e) => setModelo(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="anio">Año</Label>
            <Input id="anio" type="number" value={anio} onChange={(e) => setAnio(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="kmActual">Km actual</Label>
            <Input id="kmActual" type="number" value={kmActual} onChange={(e) => setKmActual(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="venceVtv">Vence VTV</Label>
            <Input id="venceVtv" type="date" value={venceVtv} onChange={(e) => setVenceVtv(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="venceSeguro">Vence seguro</Label>
            <Input
              id="venceSeguro"
              type="date"
              value={venceSeguro}
              onChange={(e) => setVenceSeguro(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="costoKm">Costo por km</Label>
            <Input
              id="costoKm"
              type="number"
              step="0.01"
              value={costoKm}
              onChange={(e) => setCostoKm(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="capacidad">Capacidad de paradas</Label>
            <Input
              id="capacidad"
              type="number"
              min={1}
              value={capacidadParadas}
              onChange={(e) => setCapacidadParadas(Number(e.target.value))}
            />
          </div>
        </div>

        {error && <p className="text-sm text-destructive">{error}</p>}
        <Button onClick={guardar} disabled={guardando} className="self-start mt-2">
          {guardando ? "Guardando…" : "Guardar"}
        </Button>
      </CardContent>
    </Card>
  );
}

function ZonaPeligro({
  vehiculo,
  fetchConSesion,
  onCambio,
}: {
  vehiculo: Vehiculo;
  fetchConSesion: ReturnType<typeof useAuth>["fetchConSesion"];
  onCambio: () => void;
}) {
  const [cambiando, setCambiando] = useState(false);

  async function alternarActivo() {
    setCambiando(true);
    try {
      await fetchConSesion(`/api/vehiculos/${vehiculo.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !vehiculo.activo }),
      });
      onCambio();
    } finally {
      setCambiando(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Zona de riesgo</CardTitle>
      </CardHeader>
      <CardContent className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {vehiculo.activo
            ? "Deja de aparecer como opción al armar rutas nuevas; el historial queda intacto."
            : "Este vehículo está inactivo."}
        </p>
        <Button variant="outline" disabled={cambiando} onClick={alternarActivo}>
          {vehiculo.activo ? "Desactivar" : "Reactivar"}
        </Button>
      </CardContent>
    </Card>
  );
}
