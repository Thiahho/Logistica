"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
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
import { leerError } from "@/lib/api/errores";
import { etiquetaTipoVehiculo, type TipoVehiculo } from "@/lib/dominio/tipos";

export default function NuevoVehiculoPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <FormularioAlta />
    </RequireRole>
  );
}

function FormularioAlta() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [patente, setPatente] = useState("");
  const [tipo, setTipo] = useState<TipoVehiculo>("camioneta");
  const [descripcion, setDescripcion] = useState("");
  const [marca, setMarca] = useState("");
  const [modelo, setModelo] = useState("");
  const [anio, setAnio] = useState("");
  const [kmActual, setKmActual] = useState("");
  const [venceVtv, setVenceVtv] = useState("");
  const [venceSeguro, setVenceSeguro] = useState("");
  const [costoKm, setCostoKm] = useState("");
  const [capacidadParadas, setCapacidadParadas] = useState(24);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/vehiculos", {
        method: "POST",
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
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      router.push("/vehiculos");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear el vehículo.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-md">
      <CabeceraSesion titulo="Nuevo vehículo" />
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Identificación</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="patente">Patente</Label>
              <Input id="patente" required value={patente} onChange={(e) => setPatente(e.target.value)} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="descripcion">Descripción</Label>
              <Input
                id="descripcion"
                placeholder="Utilitario 1"
                value={descripcion}
                onChange={(e) => setDescripcion(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="tipo">Tipo</Label>
              <Select value={tipo} onValueChange={(v) => setTipo(v as TipoVehiculo)}>
                <SelectTrigger id="tipo">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="camioneta">{etiquetaTipoVehiculo("camioneta")}</SelectItem>
                  <SelectItem value="moto">{etiquetaTipoVehiculo("moto")}</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Determina qué tarifa aplica a los pedidos de una ruta armada con este vehículo.
              </p>
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
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Operación</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-4">
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
                  required
                  value={capacidadParadas}
                  onChange={(e) => setCapacidadParadas(Number(e.target.value))}
                />
              </div>
            </div>
          </CardContent>
        </Card>

        {error && <p className="text-sm text-destructive">{error}</p>}
        <Button type="submit" disabled={enviando}>
          {enviando ? "Creando…" : "Crear vehículo"}
        </Button>
      </form>
    </div>
  );
}
