"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerError, leerJson } from "@/lib/api/errores";
import { etiquetaRolCliente, type ClienteUsuarioCuenta } from "@/lib/dominio/tipos";

/**
 * Logins del portal de una empresa cliente (tabla clientes_usuarios). La usan dos puertas con la
 * misma forma de API: el BackOffice desde la ficha del cliente (`/api/clientes/{id}/usuarios`,
 * Administración, elige el rol) y el dueño desde "Mi equipo" (`/api/mi-cuenta/usuarios`, solo da de
 * alta empleados y solo los administra a ellos: el backend rechaza tocarse a sí mismo o a otro dueño).
 */
export function UsuariosClienteGestion({
  basePath,
  titulo,
  elegirRol = false,
  gestionable = () => true,
  textoVacio = "Todavía no hay usuarios.",
}: {
  /** Ruta de la colección, sin barra final: GET/POST ahí, PUT `{basePath}/{id}/activo|password`. */
  basePath: string;
  titulo: string;
  /** true en el BackOffice: permite crear dueños además de empleados. */
  elegirRol?: boolean;
  /** Si se muestran las acciones de activar/contraseña para un login (el dueño no se gestiona a sí mismo). */
  gestionable?: (u: ClienteUsuarioCuenta) => boolean;
  textoVacio?: string;
}) {
  const { fetchConSesion } = useAuth();
  const [usuarios, setUsuarios] = useState<ClienteUsuarioCuenta[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [nombre, setNombre] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  // En el BackOffice el primer login de una empresa suele ser el dueño.
  const [rol, setRol] = useState<ClienteUsuarioCuenta["rol"]>(elegirRol ? "dueno" : "usuario");
  const [creando, setCreando] = useState(false);

  const [cambiando, setCambiando] = useState<string | null>(null);
  const [reseteando, setReseteando] = useState<string | null>(null);
  const [passwords, setPasswords] = useState<Record<string, string>>({});

  const cargar = useCallback(() => {
    fetchConSesion(basePath)
      .then((r) => leerJson<ClienteUsuarioCuenta[]>(r))
      .then(setUsuarios)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudieron cargar los usuarios."));
  }, [fetchConSesion, basePath]);

  useEffect(cargar, [cargar]);

  async function crear(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setCreando(true);
    try {
      const resp = await fetchConSesion(basePath, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(elegirRol ? { nombre, email, password, rol } : { nombre, email, password }),
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
    setError(null);
    setCambiando(u.id);
    try {
      const resp = await fetchConSesion(`${basePath}/${u.id}/activo`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activo: !u.activo }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar el estado.");
    } finally {
      setCambiando(null);
    }
  }

  async function resetearPassword(id: string) {
    const nuevaPassword = passwords[id];
    if (!nuevaPassword || nuevaPassword.length < 10) {
      setError("La contraseña debe tener al menos 10 caracteres, con letras y números.");
      return;
    }
    setError(null);
    setReseteando(id);
    try {
      const resp = await fetchConSesion(`${basePath}/${id}/password`, {
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
        <CardTitle className="text-base">{titulo}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {error && <p className="text-sm text-destructive">{error}</p>}

        {!usuarios ? (
          error ? null : <p className="text-sm text-muted-foreground">Cargando…</p>
        ) : usuarios.length === 0 ? (
          <p className="text-sm text-muted-foreground">{textoVacio}</p>
        ) : (
          <ul className="flex flex-col divide-y">
            {usuarios.map((u) => (
              <li key={u.id} className="flex flex-col gap-3 py-3 md:flex-row md:items-center md:justify-between">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-medium">
                    <span className="truncate">{u.nombre}</span>
                    <span className="rounded-full bg-accent px-2 py-0.5 text-xs font-medium text-bf-azul">
                      {etiquetaRolCliente(u.rol)}
                    </span>
                    {!u.activo && (
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">Inactivo</span>
                    )}
                  </p>
                  <p className="truncate text-sm text-muted-foreground">{u.email}</p>
                </div>
                {gestionable(u) && (
                  <div className="flex flex-wrap items-center gap-2">
                    <Input
                      type="password"
                      aria-label={`Nueva contraseña para ${u.nombre}`}
                      placeholder="Nueva contraseña"
                      className="w-44"
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
                    <Button size="sm" variant="outline" disabled={cambiando === u.id} onClick={() => alternarActivo(u)}>
                      {u.activo ? "Desactivar" : "Reactivar"}
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}

        <form onSubmit={crear} className="flex flex-col gap-3 border-t pt-4 md:flex-row md:flex-wrap md:items-end">
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
              minLength={10}
              placeholder="mín. 10, letras y números"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          {elegirRol && (
            <div className="flex flex-col gap-2">
              <Label>Rol</Label>
              <div className="flex gap-2">
                {(["dueno", "usuario"] as const).map((r) => (
                  <Button key={r} type="button" variant={rol === r ? "default" : "outline"} onClick={() => setRol(r)}>
                    {etiquetaRolCliente(r)}
                  </Button>
                ))}
              </div>
            </div>
          )}
          <Button type="submit" disabled={creando}>
            {creando ? "Creando…" : elegirRol ? "Agregar login" : "Agregar empleado"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
