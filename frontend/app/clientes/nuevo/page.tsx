"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function NuevoClientePage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <FormularioNuevoCliente />
    </RequireRole>
  );
}

function FormularioNuevoCliente() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [razonSocial, setRazonSocial] = useState("");
  const [cuit, setCuit] = useState("");
  const [contacto, setContacto] = useState("");
  const [telefono, setTelefono] = useState("");
  const [email, setEmail] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      const resp = await fetchConSesion("/api/clientes", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          razonSocial,
          cuit: cuit || null,
          contacto: contacto || null,
          telefono: telefono || null,
          email: email || null,
        }),
      });
      if (!resp.ok) throw new Error(await resp.text());
      const { id } = await resp.json();
      router.push(`/clientes/${id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo crear el cliente.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="p-8 max-w-lg">
      <CabeceraSesion titulo="Nuevo cliente" />
      <Button variant="outline" render={<Link href="/clientes" />} nativeButton={false} className="mb-6">
        ← Clientes
      </Button>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Datos</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="razonSocial">Razón social</Label>
              <Input
                id="razonSocial"
                required
                value={razonSocial}
                onChange={(e) => setRazonSocial(e.target.value)}
              />
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
            <p className="text-xs text-muted-foreground">
              Los tres indicadores (pago, trato, operación) arrancan en rojo/amarillo/amarillo —
              un cliente sin historial no es neutro, es desconocido (RF-32). Se ajustan a mano
              desde la pantalla del cliente.
            </p>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" disabled={enviando} className="self-start">
              {enviando ? "Creando…" : "Crear cliente"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
