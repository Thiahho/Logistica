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
import type { Rol } from "@/lib/auth/types";

// Solo personal interno: los logins de cliente se dan de alta desde la ficha del cliente
// (clientes/[id]), tabla separada a propósito — ver Entidades/ClienteUsuario.cs en el backend.
type RolStaff = Exclude<Rol, "cliente">;

const ROLES: { value: RolStaff; label: string }[] = [
  { value: "administracion", label: "Administración" },
  { value: "operacion", label: "Operación" },
  { value: "repartidor", label: "Repartidor" },
];

export default function NuevoUsuarioPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <FormularioAlta />
    </RequireRole>
  );
}

function FormularioAlta() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [nombre, setNombre] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [rol, setRol] = useState<RolStaff>("operacion");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/usuarios", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ nombre, email, password, rol }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      router.push("/usuarios");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear el usuario.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-md">
      <CabeceraSesion titulo="Nuevo usuario" />
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Cuenta</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="nombre">Nombre</Label>
              <Input id="nombre" required value={nombre} onChange={(e) => setNombre(e.target.value)} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="password">Contraseña</Label>
              <Input
                id="password"
                type="password"
                required
                minLength={8}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label>Rol</Label>
              <Select items={ROLES} value={rol} onValueChange={(v) => v && setRol(v as RolStaff)}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {ROLES.map((r) => (
                    <SelectItem key={r.value} value={r.value}>
                      {r.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </CardContent>
        </Card>

        {error && <p className="text-sm text-destructive">{error}</p>}
        <Button type="submit" disabled={enviando}>
          {enviando ? "Creando…" : "Crear usuario"}
        </Button>
      </form>
    </div>
  );
}
