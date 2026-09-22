"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";
import { leerError, leerJson } from "@/lib/api/errores";
import type { ClienteDestinatarioResumen } from "@/lib/dominio/tipos";

/**
 * "Mis clientes" — libreta de destinatarios que el propio cliente registra a mano (reversión de
 * la decisión de acta 3.5, a pedido del cliente — ver acta_sistema.md changelog 4.10). Mismo
 * lenguaje visual que /depositos (ABM simple: lista + formulario, sin modal).
 */
export default function MisClientesPage() {
  return (
    <RequireRole roles={["cliente"]}>
      <LibretaDestinatarios />
    </RequireRole>
  );
}

function LibretaDestinatarios() {
  const { fetchConSesion } = useAuth();

  const [contactos, setContactos] = useState<ClienteDestinatarioResumen[]>([]);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [nombreNuevo, setNombreNuevo] = useState("");
  const [telefonoNuevo, setTelefonoNuevo] = useState("");
  const [direccionNueva, setDireccionNueva] = useState<DireccionResuelta | null>(null);
  const [observacionesNuevo, setObservacionesNuevo] = useState("");
  const [creando, setCreando] = useState(false);
  const [errorCreacion, setErrorCreacion] = useState<string | null>(null);
  // Fuerza el remonte del SelectorDireccion tras crear: es un componente no controlado (mismo
  // criterio que /depositos).
  const [formularioKey, setFormularioKey] = useState(0);

  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [nombreEditado, setNombreEditado] = useState("");
  const [telefonoEditado, setTelefonoEditado] = useState("");
  const [observacionesEditado, setObservacionesEditado] = useState("");
  const [guardandoId, setGuardandoId] = useState<string | null>(null);
  const [borrandoId, setBorrandoId] = useState<string | null>(null);
  const [errorFila, setErrorFila] = useState<string | null>(null);

  useEffect(() => {
    cargar();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchConSesion no es reactivo
  }, []);

  async function cargar() {
    try {
      const resp = await fetchConSesion("/api/mi-cuenta/destinatarios");
      setContactos(await leerJson<ClienteDestinatarioResumen[]>(resp));
    } catch (err) {
      setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar tus clientes.");
    }
  }

  async function onCrear() {
    if (!nombreNuevo.trim() || !telefonoNuevo.trim() || !direccionNueva) return;
    setCreando(true);
    setErrorCreacion(null);
    try {
      const resp = await fetchConSesion("/api/mi-cuenta/destinatarios", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          nombre: nombreNuevo.trim(),
          telefono: telefonoNuevo.trim(),
          destinoUbicacionId: direccionNueva.ubicacionId,
          observaciones: observacionesNuevo.trim() || null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setNombreNuevo("");
      setTelefonoNuevo("");
      setDireccionNueva(null);
      setObservacionesNuevo("");
      setFormularioKey((k) => k + 1);
      await cargar();
    } catch (err) {
      setErrorCreacion(err instanceof Error ? err.message : "No se pudo guardar el cliente.");
    } finally {
      setCreando(false);
    }
  }

  function empezarEdicion(c: ClienteDestinatarioResumen) {
    setEditandoId(c.id);
    setNombreEditado(c.nombre);
    setTelefonoEditado(c.telefono);
    setObservacionesEditado(c.observaciones ?? "");
    setErrorFila(null);
  }

  async function onGuardarEdicion(c: ClienteDestinatarioResumen) {
    if (!nombreEditado.trim() || !telefonoEditado.trim()) return;
    setGuardandoId(c.id);
    setErrorFila(null);
    try {
      // La dirección no se edita acá a propósito: cambiar de dirección es un cliente distinto en
      // los hechos — se borra y se crea de nuevo. Esto solo corrige nombre/teléfono/observaciones.
      const resp = await fetchConSesion(`/api/mi-cuenta/destinatarios/${c.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          nombre: nombreEditado.trim(),
          telefono: telefonoEditado.trim(),
          destinoUbicacionId: c.destinoUbicacionId,
          observaciones: observacionesEditado.trim() || null,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setEditandoId(null);
      await cargar();
    } catch (err) {
      setErrorFila(err instanceof Error ? err.message : "No se pudo guardar el cambio.");
    } finally {
      setGuardandoId(null);
    }
  }

  async function onBorrar(id: string) {
    if (!confirm("¿Borrar este cliente de tu libreta? No afecta a los envíos ya cargados.")) return;
    setBorrandoId(id);
    setErrorFila(null);
    try {
      const resp = await fetchConSesion(`/api/mi-cuenta/destinatarios/${id}`, { method: "DELETE" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      await cargar();
    } catch (err) {
      setErrorFila(err instanceof Error ? err.message : "No se pudo borrar.");
    } finally {
      setBorrandoId(null);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-lg flex flex-col gap-6">
      <Button variant="outline" render={<Link href="/mis-envios" />} nativeButton={false} className="self-start">
        ← Mis envíos
      </Button>
      <CabeceraSesion titulo="Mis clientes" />
      <p className="text-sm text-muted-foreground -mt-4">
        Guardá los destinatarios a los que les envíás seguido: al cargar un envío nuevo vas a poder
        elegirlos directo, sin volver a tipear nada.
      </p>
      {errorCarga && <p className="text-sm text-destructive">{errorCarga}</p>}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Guardados</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {contactos.length === 0 && !errorCarga && (
            <p className="text-muted-foreground">Todavía no guardaste ningún cliente.</p>
          )}
          {contactos.map((c) => (
            <div key={c.id} className="flex flex-col gap-1 rounded-lg border p-3">
              {editandoId === c.id ? (
                <div className="flex flex-col gap-2">
                  <Input value={nombreEditado} onChange={(e) => setNombreEditado(e.target.value)} placeholder="Nombre" />
                  <Input value={telefonoEditado} onChange={(e) => setTelefonoEditado(e.target.value)} placeholder="Teléfono" />
                  <Input
                    value={observacionesEditado}
                    onChange={(e) => setObservacionesEditado(e.target.value)}
                    placeholder="Observaciones (opcional)"
                  />
                  <div className="flex gap-2">
                    <Button size="sm" disabled={guardandoId === c.id} onClick={() => onGuardarEdicion(c)}>
                      {guardandoId === c.id ? "Guardando…" : "Guardar"}
                    </Button>
                    <Button size="sm" variant="outline" onClick={() => setEditandoId(null)}>
                      Cancelar
                    </Button>
                  </div>
                </div>
              ) : (
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium">{c.nombre}</span>
                  <div className="flex gap-2">
                    <Button size="sm" variant="outline" onClick={() => empezarEdicion(c)}>
                      Editar
                    </Button>
                    <Button size="sm" variant="outline" disabled={borrandoId === c.id} onClick={() => onBorrar(c.id)}>
                      {borrandoId === c.id ? "Borrando…" : "Borrar"}
                    </Button>
                  </div>
                </div>
              )}
              <p className="text-sm text-muted-foreground">{c.telefono}</p>
              <p className="text-sm text-muted-foreground">
                {c.destinoCalleNumero}
                {c.localidadNombre ? `, ${c.localidadNombre}` : ""}
              </p>
              {c.observaciones && editandoId !== c.id && (
                <p className="text-sm text-muted-foreground">{c.observaciones}</p>
              )}
              {errorFila && (editandoId === c.id || borrandoId === c.id) && (
                <p className="text-sm text-destructive">{errorFila}</p>
              )}
            </div>
          ))}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Agregar cliente</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="contacto-nombre">Nombre</Label>
            <Input id="contacto-nombre" value={nombreNuevo} onChange={(e) => setNombreNuevo(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="contacto-telefono">Teléfono</Label>
            <Input id="contacto-telefono" value={telefonoNuevo} onChange={(e) => setTelefonoNuevo(e.target.value)} />
          </div>
          <SelectorDireccion
            key={formularioKey}
            inicial={null}
            onCambio={setDireccionNueva}
            idPrefijo="contacto-nuevo"
            basePath="/api/mi-cuenta"
          />
          <div className="flex flex-col gap-2">
            <Label htmlFor="contacto-observaciones">Observaciones (opcional)</Label>
            <Input
              id="contacto-observaciones"
              value={observacionesNuevo}
              onChange={(e) => setObservacionesNuevo(e.target.value)}
              placeholder="Ej: portero eléctrico, dejar en portería"
            />
          </div>
          {errorCreacion && <p className="text-sm text-destructive">{errorCreacion}</p>}
          <Button
            disabled={!nombreNuevo.trim() || !telefonoNuevo.trim() || !direccionNueva || creando}
            onClick={onCrear}
            className="self-start"
          >
            {creando ? "Guardando…" : "Guardar cliente"}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
