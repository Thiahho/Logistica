"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { PuntoColor } from "@/components/PuntoColor";
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
import type { Cliente } from "@/lib/dominio/tipos";

export default function ClientesPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <ListaClientes />
    </RequireRole>
  );
}

function ListaClientes() {
  const { fetchConSesion } = useAuth();
  const [clientes, setClientes] = useState<Cliente[] | null>(null);
  const [cambiando, setCambiando] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = () => {
    fetchConSesion("/api/clientes")
      .then((r) => leerJson<Cliente[]>(r))
      .then(setClientes)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los clientes."));
  };

  useEffect(cargar, [fetchConSesion]);

  async function alternarActivo(c: Cliente) {
    setCambiando(c.id);
    try {
      await fetchConSesion(`/api/clientes/${c.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !c.activo }),
      });
      cargar();
    } finally {
      setCambiando(null);
    }
  }

  return (
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Clientes" />

      <div className="flex items-center justify-end mb-4">
        <Button render={<Link href="/clientes/nuevo" />} nativeButton={false}>
          Nuevo cliente
        </Button>
      </div>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !clientes ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : clientes.length === 0 ? (
        <p className="text-muted-foreground">No hay clientes cargados.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Razón social</TableHead>
              <TableHead>CUIT</TableHead>
              <TableHead>Pago</TableHead>
              <TableHead>Trato</TableHead>
              <TableHead>Operación</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {clientes.map((c) => (
              <TableRow key={c.id}>
                <TableCell>
                  <Link href={`/clientes/${c.id}`} className="font-medium hover:underline">
                    {c.razonSocial}
                  </Link>
                </TableCell>
                <TableCell>{c.cuit ?? "—"}</TableCell>
                <TableCell><PuntoColor color={c.colorPago} /></TableCell>
                <TableCell><PuntoColor color={c.colorTrato} /></TableCell>
                <TableCell><PuntoColor color={c.colorOper} /></TableCell>
                <TableCell>{c.activo ? "Activo" : "Inactivo"}</TableCell>
                <TableCell>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={cambiando === c.id}
                    onClick={() => alternarActivo(c)}
                  >
                    {c.activo ? "Desactivar" : "Reactivar"}
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
