"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerJson } from "@/lib/api/errores";
import type { Vehiculo } from "@/lib/dominio/tipos";

export default function VehiculosPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <ListaVehiculos />
    </RequireRole>
  );
}

// Vencido = ya pasó; el listado solo lo resalta, no bloquea nada (no hay regla de negocio que
// impida asignar un vehículo con VTV o seguro vencidos, solo visibilidad para administración).
function vencido(fecha: string | null): boolean {
  if (!fecha) return false;
  return fecha < new Date().toISOString().slice(0, 10);
}

function ListaVehiculos() {
  const { fetchConSesion } = useAuth();
  const [vehiculos, setVehiculos] = useState<Vehiculo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cambiando, setCambiando] = useState<number | null>(null);

  const cargar = () => {
    fetchConSesion("/api/vehiculos")
      .then((r) => leerJson<Vehiculo[]>(r))
      .then(setVehiculos)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los vehículos."));
  };

  useEffect(cargar, [fetchConSesion]);

  async function alternarActivo(v: Vehiculo) {
    setCambiando(v.id);
    try {
      await fetchConSesion(`/api/vehiculos/${v.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !v.activo }),
      });
      cargar();
    } finally {
      setCambiando(null);
    }
  }

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Vehículos" />

      <div className="flex items-center justify-end mb-4">
        <Button render={<Link href="/vehiculos/nuevo" />} nativeButton={false}>
          Nuevo vehículo
        </Button>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !vehiculos ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Patente</TableHead>
              <TableHead>Descripción</TableHead>
              <TableHead>Marca / modelo</TableHead>
              <TableHead>Capacidad</TableHead>
              <TableHead>Vence VTV</TableHead>
              <TableHead>Vence seguro</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {vehiculos.map((v) => (
              <TableRow key={v.id}>
                <TableCell>
                  <Link href={`/vehiculos/${v.id}`} className="font-medium hover:underline">
                    {v.patente}
                  </Link>
                </TableCell>
                <TableCell>{v.descripcion ?? "—"}</TableCell>
                <TableCell>
                  {v.marca || v.modelo ? [v.marca, v.modelo].filter(Boolean).join(" ") : "—"}
                </TableCell>
                <TableCell>{v.capacidadParadas}</TableCell>
                <TableCell className={vencido(v.venceVtv) ? "text-destructive" : undefined}>
                  {v.venceVtv ?? "—"}
                </TableCell>
                <TableCell className={vencido(v.venceSeguro) ? "text-destructive" : undefined}>
                  {v.venceSeguro ?? "—"}
                </TableCell>
                <TableCell>{v.activo ? "Activo" : "Inactivo"}</TableCell>
                <TableCell>
                  <Button size="sm" variant="outline" disabled={cambiando === v.id} onClick={() => alternarActivo(v)}>
                    {v.activo ? "Desactivar" : "Reactivar"}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
