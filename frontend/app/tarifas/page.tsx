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
  const [precios, setPrecios] = useState<Record<number, string>>({});
  const [kmDesdes, setKmDesdes] = useState<Record<number, string>>({});
  const [kmHastas, setKmHastas] = useState<Record<number, string>>({});
  const [guardandoZona, setGuardandoZona] = useState<number | null>(null);

  const cargar = () => {
    fetchConSesion("/api/tarifas")
      .then((r) => r.json())
      .then(setTarifas);
  };

  useEffect(cargar, [fetchConSesion]);

  function valorPrecio(t: TarifaGeneral) {
    return precios[t.zonaId] ?? (t.precio !== null ? String(t.precio) : "");
  }

  function valorKmDesde(t: TarifaGeneral) {
    return kmDesdes[t.zonaId] ?? (t.kmDesde !== null ? String(t.kmDesde) : "");
  }

  function valorKmHasta(t: TarifaGeneral) {
    return kmHastas[t.zonaId] ?? (t.kmHasta !== null ? String(t.kmHasta) : "");
  }

  async function guardar(t: TarifaGeneral) {
    setGuardandoZona(t.zonaId);
    try {
      const precioTexto = valorPrecio(t);
      const kmDesdeTexto = valorKmDesde(t);
      const kmHastaTexto = valorKmHasta(t);
      await Promise.all([
        fetchConSesion(`/api/tarifas/${t.zonaId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ precio: precioTexto === "" ? null : Number(precioTexto) }),
        }),
        fetchConSesion(`/api/zonas/${t.zonaId}/km`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            kmDesde: kmDesdeTexto === "" ? null : Number(kmDesdeTexto),
            kmHasta: kmHastaTexto === "" ? null : Number(kmHastaTexto),
          }),
        }),
      ]);
      cargar();
    } finally {
      setGuardandoZona(null);
    }
  }

  return (
    <div className="p-8 max-w-3xl">
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
                  <TableHead>Km desde</TableHead>
                  <TableHead>Km hasta</TableHead>
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
                        min={0}
                        className="w-24"
                        placeholder="—"
                        value={valorKmDesde(t)}
                        onChange={(e) => setKmDesdes((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        min={0}
                        className="w-24"
                        placeholder="sin límite"
                        value={valorKmHasta(t)}
                        onChange={(e) => setKmHastas((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        step="0.01"
                        className="w-32"
                        placeholder="sin tarifa"
                        value={valorPrecio(t)}
                        onChange={(e) => setPrecios((v) => ({ ...v, [t.zonaId]: e.target.value }))}
                      />
                    </TableCell>
                    <TableCell>
                      <Button size="sm" disabled={guardandoZona === t.zonaId} onClick={() => guardar(t)}>
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
