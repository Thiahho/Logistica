"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { ControlesPaginacion } from "@/components/ControlesPaginacion";
import { TarjetaMetrica } from "@/components/TarjetaMetrica";
import { UsuariosClienteGestion } from "@/components/UsuariosClienteGestion";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { leerJson } from "@/lib/api/errores";
import { useListadoPaginado } from "@/lib/hooks/useListadoPaginado";
import {
  etiquetaAccionPortal,
  etiquetaRolCliente,
  type ActividadEquipo,
  type ResumenEquipo,
  type UsuarioEquipo,
} from "@/lib/dominio/tipos";

type Seccion = "resumen" | "actividad" | "usuarios";

const SECCIONES: { valor: Seccion; label: string }[] = [
  { valor: "resumen", label: "Resumen" },
  { valor: "actividad", label: "Actividad" },
  { valor: "usuarios", label: "Usuarios" },
];

/** YYYY-MM-DD de hoy menos `dias`, en hora local del navegador (mismo criterio que el alta). */
function haceDias(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  return d.toLocaleDateString("en-CA");
}

/** "Mi equipo": solo el dueño de la empresa cliente. Quién de su equipo cargó qué, cómo terminaron
 * esos envíos, qué hizo cada uno en el portal, y el alta/baja de sus empleados. */
export default function EquipoPage() {
  return (
    <RequireRole roles={["cliente"]} soloDueno>
      <Equipo />
    </RequireRole>
  );
}

function Equipo() {
  const { usuario } = useAuth();
  const [seccion, setSeccion] = useState<Seccion>("resumen");
  const [desde, setDesde] = useState(haceDias(29));
  const [hasta, setHasta] = useState(haceDias(0));

  return (
    <div className="flex flex-col gap-6 p-4 md:p-8">
      <CabeceraSesion titulo="Mi equipo" />

      <div className="flex gap-2" role="tablist" aria-label="Secciones de Mi equipo">
        {SECCIONES.map((s) => (
          <Button
            key={s.valor}
            role="tab"
            aria-selected={seccion === s.valor}
            variant={seccion === s.valor ? "default" : "outline"}
            size="sm"
            onClick={() => setSeccion(s.valor)}
          >
            {s.label}
          </Button>
        ))}
      </div>

      {seccion !== "usuarios" && (
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="equipo-desde">Desde</Label>
            <Input id="equipo-desde" type="date" value={desde} max={hasta} onChange={(e) => setDesde(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="equipo-hasta">Hasta</Label>
            <Input id="equipo-hasta" type="date" value={hasta} min={desde} onChange={(e) => setHasta(e.target.value)} />
          </div>
        </div>
      )}

      {seccion === "resumen" && <Resumen desde={desde} hasta={hasta} />}
      {seccion === "actividad" && <Actividad desde={desde} hasta={hasta} />}
      {seccion === "usuarios" && (
        <UsuariosClienteGestion
          basePath="/api/mi-cuenta/usuarios"
          titulo="Usuarios de tu empresa"
          // El dueño administra solo a sus empleados: a sí mismo y a otro dueño los ve, no los toca.
          gestionable={(u) => u.rol === "usuario" && u.id !== usuario?.id}
          textoVacio="Todavía no agregaste empleados."
        />
      )}
    </div>
  );
}

function Resumen({ desde, hasta }: { desde: string; hasta: string }) {
  const { fetchConSesion } = useAuth();
  const [datos, setDatos] = useState<{ clave: string; resumen: ResumenEquipo } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const clave = `${desde}|${hasta}`;

  useEffect(() => {
    if (!desde || !hasta) return;
    fetchConSesion(`/api/mi-cuenta/equipo/resumen?desde=${desde}&hasta=${hasta}`)
      .then((r) => leerJson<ResumenEquipo>(r))
      .then((resumen) => {
        setError(null);
        setDatos({ clave: `${desde}|${hasta}`, resumen });
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el resumen."));
  }, [fetchConSesion, desde, hasta]);

  if (error) return <p className="text-sm text-destructive">{error}</p>;
  if (!datos || datos.clave !== clave) return <div className="h-40 animate-pulse rounded-xl border bg-muted" />;

  const filas = datos.resumen.filas;
  const total = filas.reduce((s, f) => s + f.cargados, 0);

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <TarjetaMetrica valor={total} etiqueta="Envíos cargados" />
        <TarjetaMetrica valor={filas.reduce((s, f) => s + f.entregados, 0)} etiqueta="Entregados" />
        <TarjetaMetrica
          valor={filas.reduce((s, f) => s + f.fallidos, 0)}
          etiqueta="Fallidos o devueltos"
          tono={filas.some((f) => f.fallidos > 0) ? "alerta" : "normal"}
        />
        <TarjetaMetrica valor={filas.reduce((s, f) => s + f.pendientes, 0)} etiqueta="En curso" />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Por persona</CardTitle>
        </CardHeader>
        <CardContent>
          <ul className="flex flex-col divide-y">
            {filas.map((f) => (
              <li key={f.usuarioId} className="flex flex-col gap-2 py-3 md:flex-row md:items-center md:justify-between">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-medium">
                    <span className="truncate">{f.nombre}</span>
                    <span className="rounded-full bg-accent px-2 py-0.5 text-xs font-medium text-bf-azul">
                      {etiquetaRolCliente(f.rol)}
                    </span>
                    {!f.activo && (
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">Inactivo</span>
                    )}
                  </p>
                  {total > 0 && (
                    <div className="mt-2 h-1.5 w-40 overflow-hidden rounded-full bg-muted" aria-hidden>
                      <div className="h-full rounded-full bg-bf-azul" style={{ width: `${(f.cargados / total) * 100}%` }} />
                    </div>
                  )}
                </div>
                <dl className="grid grid-cols-5 gap-3 text-center text-sm md:w-[26rem]">
                  <Dato etiqueta="Cargados" valor={f.cargados} destacado />
                  <Dato etiqueta="Entregados" valor={f.entregados} />
                  <Dato etiqueta="Fallidos" valor={f.fallidos} alerta={f.fallidos > 0} />
                  <Dato etiqueta="Cancelados" valor={f.cancelados} />
                  <Dato etiqueta="En curso" valor={f.pendientes} />
                </dl>
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>
    </div>
  );
}

function Dato({ etiqueta, valor, destacado = false, alerta = false }: {
  etiqueta: string; valor: number; destacado?: boolean; alerta?: boolean;
}) {
  return (
    <div>
      <dd className={`tabular-nums ${destacado ? "text-lg font-semibold" : "font-medium"} ${alerta ? "text-destructive" : ""}`}>
        {valor}
      </dd>
      <dt className="text-xs text-muted-foreground">{etiqueta}</dt>
    </div>
  );
}

function Actividad({ desde, hasta }: { desde: string; hasta: string }) {
  const { fetchConSesion } = useAuth();
  const [equipo, setEquipo] = useState<UsuarioEquipo[]>([]);
  const [usuarioId, setUsuarioId] = useState<string | null>(null);
  const {
    items, totalRegistros, error, pagina, setPagina, tamanioPagina, setTamanioPagina, totalPaginas,
    conReinicioDePagina,
  } = useListadoPaginado<ActividadEquipo>({
    ruta: "/api/mi-cuenta/equipo/actividad",
    filtros: { usuarioId: usuarioId ?? "", desde, hasta },
    ordenInicial: "-fecha",
    tamanioPaginaInicial: 20,
  });

  useEffect(() => {
    fetchConSesion("/api/mi-cuenta/usuarios")
      .then((r) => leerJson<UsuarioEquipo[]>(r))
      .then(setEquipo)
      .catch(() => setEquipo([]));
  }, [fetchConSesion]);

  return (
    <div className="flex flex-col gap-3">
      {equipo.length > 1 && (
        <div className="md:max-w-xs">
          <ComboboxBusqueda
            items={equipo.map((u) => ({ value: u.id, label: u.nombre }))}
            value={usuarioId}
            onValueChange={conReinicioDePagina(setUsuarioId)}
            placeholder="Todas las personas"
          />
        </div>
      )}

      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !items ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : items.length === 0 ? (
        <p className="text-muted-foreground">No hay actividad en estas fechas.</p>
      ) : (
        <>
          <ul className="flex flex-col gap-2">
            {items.map((a) => (
              <li key={a.id} className="flex flex-col gap-1 rounded-xl border bg-card p-3">
                <div className="flex items-baseline justify-between gap-3">
                  <p className="font-medium">
                    {a.usuarioNombre} <span className="font-normal text-muted-foreground">· {etiquetaAccionPortal(a.accion)}</span>
                  </p>
                  <time dateTime={a.ocurridoEn} className="shrink-0 text-xs text-muted-foreground tabular-nums">
                    {new Date(a.ocurridoEn).toLocaleString("es-AR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}
                  </time>
                </div>
                {(a.detalle || a.entidadTipo === "pedido") && (
                  <p className="text-sm text-muted-foreground">
                    {a.entidadTipo === "pedido" && `Envío #${a.entidadId}`}
                    {a.entidadTipo === "pedido" && a.detalle && " · "}
                    {a.detalle}
                  </p>
                )}
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
