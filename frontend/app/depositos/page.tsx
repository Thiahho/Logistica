"use client";

import { useEffect, useState } from "react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";
import { leerError, leerJson } from "@/lib/api/errores";
import type { Deposito } from "@/lib/dominio/tipos";

export default function DepositosPage() {
  return (
    <RequireRole roles={["administracion"]}>
      <CatalogoDepositos />
    </RequireRole>
  );
}

function CatalogoDepositos() {
  const { fetchConSesion } = useAuth();

  const [depositos, setDepositos] = useState<Deposito[]>([]);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [nombreNuevo, setNombreNuevo] = useState("");
  const [direccionNueva, setDireccionNueva] = useState<DireccionResuelta | null>(null);
  const [creando, setCreando] = useState(false);
  const [errorCreacion, setErrorCreacion] = useState<string | null>(null);
  // Fuerza el remonte del SelectorDireccion tras crear: es un componente no controlado, así que
  // limpiar `direccionNueva` acá no le borra los campos visibles (ver docblock del componente).
  const [formularioKey, setFormularioKey] = useState(0);

  const [editandoId, setEditandoId] = useState<number | null>(null);
  const [nombreEditado, setNombreEditado] = useState("");
  const [guardandoId, setGuardandoId] = useState<number | null>(null);
  const [desactivandoId, setDesactivandoId] = useState<number | null>(null);
  const [errorFila, setErrorFila] = useState<string | null>(null);

  useEffect(() => {
    cargar();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchConSesion no es reactivo
  }, []);

  async function cargar() {
    try {
      const resp = await fetchConSesion("/api/ubicaciones/depositos");
      setDepositos(await leerJson<Deposito[]>(resp));
    } catch (err) {
      setErrorCarga(err instanceof Error ? err.message : "No se pudieron cargar los depósitos.");
    }
  }

  async function onCrear() {
    if (!nombreNuevo.trim() || !direccionNueva) return;
    setCreando(true);
    setErrorCreacion(null);
    try {
      const resp = await fetchConSesion("/api/ubicaciones/depositos", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          nombre: nombreNuevo.trim(),
          calleNumero: direccionNueva.calleNumero,
          localidadId: direccionNueva.localidadId,
        }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setNombreNuevo("");
      setDireccionNueva(null);
      setFormularioKey((k) => k + 1);
      await cargar();
    } catch (err) {
      setErrorCreacion(err instanceof Error ? err.message : "No se pudo crear el depósito.");
    } finally {
      setCreando(false);
    }
  }

  function empezarEdicion(d: Deposito) {
    setEditandoId(d.ubicacionId);
    setNombreEditado(d.nombre);
    setErrorFila(null);
  }

  async function onRenombrar(id: number) {
    if (!nombreEditado.trim()) return;
    setGuardandoId(id);
    setErrorFila(null);
    try {
      const resp = await fetchConSesion(`/api/ubicaciones/depositos/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ nombre: nombreEditado.trim() }),
      });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      setEditandoId(null);
      await cargar();
    } catch (err) {
      setErrorFila(err instanceof Error ? err.message : "No se pudo renombrar el depósito.");
    } finally {
      setGuardandoId(null);
    }
  }

  async function onDesactivar(id: number) {
    if (!confirm("¿Sacar este depósito del catálogo? Las rutas que ya lo usaron como origen conservan esa dirección.")) return;
    setDesactivandoId(id);
    setErrorFila(null);
    try {
      const resp = await fetchConSesion(`/api/ubicaciones/depositos/${id}`, { method: "DELETE" });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      await cargar();
    } catch (err) {
      setErrorFila(err instanceof Error ? err.message : "No se pudo desactivar el depósito.");
    } finally {
      setDesactivandoId(null);
    }
  }

  return (
    <div className="p-4 md:p-8 max-w-lg flex flex-col gap-6">
      <CabeceraSesion titulo="Depósitos" />
      {errorCarga && <p className="text-sm text-destructive">{errorCarga}</p>}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Catálogo</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <p className="text-sm text-muted-foreground">
            Cada uno es un punto de partida elegible al armar una ruta. Una ruta ya cerrada conserva la
            dirección que tenía el depósito el día que cerró — renombrarlo o sacarlo del catálogo no
            reescribe rutas pasadas.
          </p>
          {depositos.length === 0 && !errorCarga && (
            <p className="text-muted-foreground">Todavía no hay ningún depósito cargado.</p>
          )}
          {depositos.map((d) => (
            <div key={d.ubicacionId} className="flex flex-col gap-1 rounded-lg border p-3">
              {editandoId === d.ubicacionId ? (
                <div className="flex gap-2">
                  <Input
                    value={nombreEditado}
                    onChange={(e) => setNombreEditado(e.target.value)}
                    className="w-40"
                  />
                  <Button size="sm" disabled={guardandoId === d.ubicacionId} onClick={() => onRenombrar(d.ubicacionId)}>
                    {guardandoId === d.ubicacionId ? "Guardando…" : "Guardar"}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => setEditandoId(null)}>
                    Cancelar
                  </Button>
                </div>
              ) : (
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium">{d.nombre}</span>
                  <div className="flex gap-2">
                    <Button size="sm" variant="outline" onClick={() => empezarEdicion(d)}>
                      Renombrar
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={desactivandoId === d.ubicacionId}
                      onClick={() => onDesactivar(d.ubicacionId)}
                    >
                      {desactivandoId === d.ubicacionId ? "Sacando…" : "Sacar del catálogo"}
                    </Button>
                  </div>
                </div>
              )}
              <p className="text-sm text-muted-foreground">
                {d.calleNumero}
                {d.localidad ? `, ${d.localidad}` : ""}
              </p>
              {errorFila && (editandoId === d.ubicacionId || desactivandoId === d.ubicacionId) && (
                <p className="text-sm text-destructive">{errorFila}</p>
              )}
            </div>
          ))}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Agregar depósito</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="deposito-nombre">Nombre</Label>
            <Input
              id="deposito-nombre"
              value={nombreNuevo}
              onChange={(e) => setNombreNuevo(e.target.value)}
              placeholder="Ej: Depósito Sur"
            />
          </div>
          <SelectorDireccion key={formularioKey} inicial={null} onCambio={setDireccionNueva} idPrefijo="deposito-nuevo" />
          {errorCreacion && <p className="text-sm text-destructive">{errorCreacion}</p>}
          <Button disabled={!nombreNuevo.trim() || !direccionNueva || creando} onClick={onCrear} className="self-start">
            {creando ? "Creando…" : "Crear depósito"}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
