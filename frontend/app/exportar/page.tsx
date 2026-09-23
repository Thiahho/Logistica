"use client";

import { useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { leerError } from "@/lib/api/errores";

const hoyISO = () => new Date().toISOString().slice(0, 10);

const REPORTES = [
  { clave: "pedidos", etiqueta: "Pedidos" },
  { clave: "rutas", etiqueta: "Rutas" },
  { clave: "resultados", etiqueta: "Resultados (márgenes)" },
] as const;

export default function ExportarPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <FormularioExportar />
    </RequireRole>
  );
}

function FormularioExportar() {
  const { fetchConSesion } = useAuth();
  const [desde, setDesde] = useState(hoyISO());
  const [hasta, setHasta] = useState(hoyISO());
  const [descargando, setDescargando] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function descargar(clave: string) {
    setError(null);
    setDescargando(clave);
    try {
      const resp = await fetchConSesion(`/api/exportar/${clave}?desde=${desde}&hasta=${hasta}`);
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const blob = await resp.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${clave}_${desde}_${hasta}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo exportar.");
    } finally {
      setDescargando(null);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-md">
      <CabeceraSesion titulo="Exportar" />
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Rango de fechas</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="desde">Desde</Label>
              <Input id="desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="hasta">Hasta</Label>
              <Input id="hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
            </div>
          </div>
          <div className="flex flex-col gap-2 pt-2 border-t">
            {REPORTES.map((r) => (
              <Button
                key={r.clave}
                variant="outline"
                disabled={descargando === r.clave}
                onClick={() => descargar(r.clave)}
                className="justify-start"
              >
                {descargando === r.clave ? "Descargando…" : `Descargar ${r.etiqueta} (CSV)`}
              </Button>
            ))}
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>
    </div>
  );
}
