"use client";

import Link from "next/link";
import { Plus } from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { Button } from "@/components/ui/button";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import { etiquetaEstadoViaje, type ViajeResumen } from "@/lib/dominio/tipos";

/** Viajes (envíos de varias paradas) de todos los clientes, cargados por el portal o por Operación. */
export default function ViajesPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <Viajes />
    </RequireRole>
  );
}

function Viajes() {
  const { items, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina, totalPaginas } =
    useListadoPaginado<ViajeResumen>({ ruta: "/api/viajes", filtros: {}, ordenInicial: "-fecha" });

  return (
    <div className="p-4 md:p-8 flex flex-col gap-4">
      <CabeceraSesion titulo="Viajes" />
      <Button className="self-start gap-1" render={<Link href="/viajes/nuevo" />} nativeButton={false}>
        <Plus className="size-4" /> Nuevo viaje
      </Button>

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !items ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : items.length === 0 ? (
        <p className="text-muted-foreground">Todavía no hay viajes.</p>
      ) : (
        <>
          <ul className="flex flex-col gap-2">
            {items.map((v) => (
              <li key={v.id}>
                <Link href={`/viajes/${v.id}`} className="flex items-center gap-3 rounded-xl border bg-card p-3 text-sm hover:bg-muted/30">
                  <div className="min-w-0 flex-1">
                    <p className="font-medium">
                      Viaje #{v.id} · {v.clienteRazonSocial}
                    </p>
                    <p className="text-muted-foreground">
                      Entrega {v.fechaEntrega.split("-").reverse().join("/")} · {v.paradas} paradas
                      {v.kmEstimados !== null && ` · ${v.kmEstimados.toLocaleString("es-AR")} km`} · cargó {v.creadoPor}
                    </p>
                  </div>
                  <span className="shrink-0 tabular-nums text-muted-foreground">
                    {v.entregadas}/{v.paradas}
                  </span>
                  <span className="shrink-0 rounded-full bg-accent px-2 py-0.5 text-xs font-medium text-bf-azul">
                    {etiquetaEstadoViaje(v.estado)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          <ControlesPaginacion
            pagina={pagina}
            setPagina={setPagina}
            totalPaginas={totalPaginas}
            totalRegistros={totalRegistros}
            tamanioPagina={tamanioPagina}
            setTamanioPagina={setTamanioPagina}
          />
        </>
      )}
    </div>
  );
}
