"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";

interface ParadaRepartidor {
  paradaId: number;
  rutaId: number;
  orden: number;
  tipo: string;
  estado: string;
  pedidoId: number;
  destinatarioNombre: string;
  destinatarioTelefono: string;
  bultos: number;
  calleNumero: string;
  localidad: string | null;
}

export default function HoyPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <ListaParadas />
    </RequireRole>
  );
}

function ListaParadas() {
  const { fetchConSesion } = useAuth();
  const [paradas, setParadas] = useState<ParadaRepartidor[] | null>(null);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas")
      .then((r) => r.json())
      .then(setParadas);
  }, [fetchConSesion]);

  return (
    <div className="p-8">
      <CabeceraSesion titulo="Hoy" />
      {!paradas ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : paradas.length === 0 ? (
        <p className="text-muted-foreground">No tenés paradas asignadas hoy.</p>
      ) : (
        <ul className="flex flex-col gap-3">
          {paradas.map((p) => (
            <li key={p.paradaId} className="rounded-lg border p-4">
              <div className="flex items-center justify-between">
                <span className="font-medium">
                  {p.orden}. {p.destinatarioNombre}
                </span>
                <span className="text-xs uppercase text-muted-foreground">{p.estado}</span>
              </div>
              <p className="text-sm text-muted-foreground">
                {p.calleNumero}
                {p.localidad ? `, ${p.localidad}` : ""}
              </p>
              <p className="text-sm text-muted-foreground">{p.bultos} bulto(s)</p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
