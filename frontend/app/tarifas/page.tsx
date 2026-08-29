"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { TarifaGeneral } from "@/lib/dominio/tipos";

export default function TarifasPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <ListaTarifas />
    </RequireRole>
  );
}

function ListaTarifas() {
  const { fetchConSesion } = useAuth();
  const [tarifas, setTarifas] = useState<TarifaGeneral[] | null>(null);
  const [valores, setValores] = useState<Record<number, string>>({});
  const [guardandoZona, setGuardandoZona] = useState<number | null>(null);

  const cargar = () => {
    fetchConSesion("/api/tarifas")
      .then((r) => r.json())
      .then(setTarifas);
  };

  useEffect(cargar, [fetchConSesion]);

  function valorZona(t: TarifaGeneral) {
    return valores[t.zonaId] ?? (t.precio !== null ? String(t.precio) : "");
  }

  async function fijar(zonaId: number, precio: number | null) {
    setGuardandoZona(zonaId);
    try {
      await fetchConSesion(`/api/tarifas/${zonaId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ precio }),
      });
      cargar();
    } finally {
      setGuardandoZona(null);
    }
  }

  return (
    <div className="p-8 max-w-2xl">
      <CabeceraSesion titulo="Tarifas — lista general" />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Precio por zona</CardTitle>
        </CardHeader>
        <CardContent>
          {!tarifas ? (
            <p className="text-muted-foreground">Cargando…</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Zona</TableHead>
                  <TableHead>Precio vigente</TableHead>
                  <TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {tarifas.map((t) => (
                  <TableRow key={t.zonaId}>
                    <TableCell>
                      {t.zonaCodigo} — {t.zonaNombre}
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        step="0.01"
                        className="w-32"
                        placeholder="sin tarifa"
                        value={valorZona(t)}
                        onChange={(e) => setValores((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Button
                        size="sm"
                        disabled={guardandoZona === t.zonaId || valorZona(t) === ""}
                        onClick={() => fijar(t.zonaId, Number(valorZona(t)))}
                      >
                        Guardar
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground mt-4">
        Un pedido ya confirmado no se ve afectado por este cambio: el precio se congela al
        confirmar (P1) y solo aplica a pedidos nuevos.
      </p>
    </div>
  );
}
