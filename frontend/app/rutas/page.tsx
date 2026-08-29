"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { RutaResumen } from "@/lib/dominio/tipos";

export default function RutasPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ListaRutas />
    </RequireRole>
  );
}

function ListaRutas() {
  const { usuario, fetchConSesion } = useAuth();
  const [rutas, setRutas] = useState<RutaResumen[] | null>(null);
  const [fecha, setFecha] = useState("");

  const cargar = useCallback(() => {
    const params = new URLSearchParams();
    if (fecha) params.set("fecha", fecha);
    const query = params.toString();
    fetchConSesion(`/api/rutas${query ? `?${query}` : ""}`)
      .then((r) => r.json())
      .then(setRutas);
  }, [fetchConSesion, fecha]);

  useEffect(cargar, [cargar]);

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Rutas" />

      <div className="flex items-end justify-between gap-4 mb-4">
        <div className="flex items-end gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtro-fecha">Fecha</Label>
            <Input
              id="filtro-fecha"
              type="date"
              value={fecha}
              onChange={(e) => setFecha(e.target.value)}
              className="w-40"
            />
          </div>
          {fecha && (
            <Button variant="outline" onClick={() => setFecha("")}>
              Limpiar filtro
            </Button>
          )}
        </div>
        <Button render={<Link href="/rutas/nueva" />} nativeButton={false}>
          Nueva ruta
        </Button>
      </div>

      {!rutas ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : rutas.length === 0 ? (
        <p className="text-muted-foreground">No hay rutas cargadas.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ID</TableHead>
              <TableHead>Fecha</TableHead>
              <TableHead>Vehículo</TableHead>
              <TableHead>Repartidor</TableHead>
              <TableHead>Paradas</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rutas.map((r) => (
              <TableRow key={r.id}>
                <TableCell>{r.id}</TableCell>
                <TableCell>{r.fecha}</TableCell>
                <TableCell>{r.vehiculo ?? "—"}</TableCell>
                <TableCell>{r.repartidorNombre ?? "—"}</TableCell>
                <TableCell>{r.cantidadParadas}</TableCell>
                <TableCell>{r.estado}</TableCell>
                <TableCell>
                  {r.estado === "planificada" && (
                    <Button
                      size="sm"
                      variant="outline"
                      render={<Link href={`/rutas/${r.id}/armar`} />}
                      nativeButton={false}
                    >
                      Armar
                    </Button>
                  )}
                  {r.estado !== "planificada" && usuario?.rol === "administracion" && (
                    <Button
                      size="sm"
                      variant="outline"
                      render={<Link href={`/rutas/${r.id}/cierre`} />}
                      nativeButton={false}
                    >
                      {r.estado === "cerrada" ? "Ver cierre" : "Cerrar"}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
