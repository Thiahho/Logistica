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
import { leerError } from "@/lib/api/errores";

const hoyISO = () => new Date().toISOString().slice(0, 10);

export default function NuevaRutaPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <FormularioNuevaRuta />
    </RequireRole>
  );
}

function FormularioNuevaRuta() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();
  const [fecha, setFecha] = useState(hoyISO());
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/rutas", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fecha }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const { id } = await resp.json();
      router.push(`/rutas/${id}/armar`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear la ruta.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-sm">
      <CabeceraSesion titulo="Nueva ruta" />
      <form onSubmit={onSubmit} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Fecha</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="fecha">Fecha de la ruta</Label>
              <Input id="fecha" type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} />
            </div>
            <p className="text-sm text-muted-foreground">
              Vehículo, repartidor y paradas se completan en la pantalla siguiente.
            </p>
          </CardContent>
        </Card>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <Button type="submit" disabled={enviando}>
          {enviando ? "Creando…" : "Crear y armar"}
        </Button>
      </form>
    </div>
  );
}
