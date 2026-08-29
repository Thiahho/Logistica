"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerError } from "@/lib/api/errores";
import type { UsuarioCuenta } from "@/lib/dominio/tipos";

export default function UsuariosPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <ListaUsuarios />
    </RequireRole>
  );
}

function ListaUsuarios() {
  const { fetchConSesion } = useAuth();
  const [usuarios, setUsuarios] = useState<UsuarioCuenta[] | null>(null);
  const [cambiando, setCambiando] = useState<string | null>(null);
  const [reseteando, setReseteando] = useState<string | null>(null);
  const [passwords, setPasswords] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  const cargar = () => {
    fetchConSesion("/api/usuarios")
      .then((r) => r.json())
      .then(setUsuarios);
  };

  useEffect(cargar, [fetchConSesion]);

  async function alternarActivo(u: UsuarioCuenta) {
    setCambiando(u.id);
    try {
      await fetchConSesion(`/api/usuarios/${u.id}/activo`, {
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
    const password = passwords[id];
    if (!password || password.length < 8) {
      setError("La contraseña debe tener al menos 8 caracteres.");
      return;
    }
    setError(null);
    setReseteando(id);
    try {
      const resp = await fetchConSesion(`/api/usuarios/${id}/password`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password }),
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
    <div className="p-8">
      <CabeceraSesion titulo="Usuarios" />

      <div className="flex items-center justify-end mb-4">
        <Button render={<Link href="/usuarios/nuevo" />} nativeButton={false}>
          Nuevo usuario
        </Button>
      </div>

      {error && <p className="text-sm text-destructive mb-4">{error}</p>}

      {!usuarios ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Nombre</TableHead>
              <TableHead>Email</TableHead>
              <TableHead>Rol</TableHead>
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
                <TableCell>{u.rol}</TableCell>
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
    </div>
  );
}
