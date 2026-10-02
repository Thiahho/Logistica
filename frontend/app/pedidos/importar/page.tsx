"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";

/** Una fila tal como vino en la planilla. Se manda de vuelta al importar: el servidor la valida otra vez. */
interface FilaPlanilla {
  numero: number;
  destinatario: string | null;
  telefono: string | null;
  calleNumero: string | null;
  localidad: string | null;
  fechaEntrega: string | null;
  bultos: string | null;
}

interface FilaPrevista {
  numero: number;
  estado: "ok" | "aviso" | "error";
  mensajes: string[];
  original: FilaPlanilla;
  pedido: {
    destinatarioNombre: string;
    calleNumero: string;
    localidadNombre: string;
    bultos: number;
    fechaEntrega: string;
  } | null;
}

interface FilaImportada {
  numero: number;
  pedidoId: number | null;
  error: string | null;
  direccionDudosa: boolean;
}

interface ClienteSeleccion {
  id: number;
  razonSocial: string;
}

/** El mismo tope que ImportacionPedidosController.MaxFilasPorTanda: cada dirección nueva consulta al
 * geocoder, así que la planilla se importa de a tandas cortas y la pantalla muestra el avance. */
const FILAS_POR_TANDA = 10;

const ESTILO_ESTADO: Record<string, string> = {
  ok: "bg-green-100 text-green-700",
  aviso: "bg-amber-100 text-amber-800",
  error: "bg-red-100 text-red-700",
  cargado: "bg-bf-celeste/20 text-bf-profundo",
};

export default function ImportarPedidosPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ImportarPedidos />
    </RequireRole>
  );
}

function ImportarPedidos() {
  const { fetchConSesion } = useAuth();
  const [clientes, setClientes] = useState<ClienteSeleccion[]>([]);
  const [clienteId, setClienteId] = useState<number | null>(null);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [filas, setFilas] = useState<FilaPrevista[] | null>(null);
  const [incluirAvisos, setIncluirAvisos] = useState(false);
  const [resultados, setResultados] = useState<Record<number, FilaImportada>>({});
  const [leyendo, setLeyendo] = useState(false);
  const [avance, setAvance] = useState<{ hechas: number; total: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConSesion("/api/clientes/seleccion")
      .then((r) => leerJson<ClienteSeleccion[]>(r))
      .then(setClientes)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la lista de clientes."));
  }, [fetchConSesion]);

  function reiniciar() {
    setFilas(null);
    setResultados({});
    setIncluirAvisos(false);
    setError(null);
  }

  async function descargarPlantilla() {
    setError(null);
    try {
      const resp = await fetchConSesion("/api/pedidos/importar/plantilla");
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      const url = URL.createObjectURL(await resp.blob());
      const a = document.createElement("a");
      a.href = url;
      a.download = "plantilla_pedidos.xlsx";
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo descargar la plantilla.");
    }
  }

  async function leerPlanilla() {
    if (clienteId === null || !archivo) return;
    reiniciar();
    setLeyendo(true);
    try {
      const cuerpo = new FormData();
      cuerpo.append("clienteId", String(clienteId));
      cuerpo.append("archivo", archivo);
      const resp = await fetchConSesion("/api/pedidos/importar/leer", { method: "POST", body: cuerpo });
      setFilas(await leerJson<FilaPrevista[]>(resp));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo leer la planilla.");
    } finally {
      setLeyendo(false);
    }
  }

  const pendientes = (filas ?? []).filter(
    (f) => !resultados[f.numero] && (f.estado === "ok" || (f.estado === "aviso" && incluirAvisos)),
  );

  async function importar() {
    if (clienteId === null || pendientes.length === 0) return;
    setError(null);
    setAvance({ hechas: 0, total: pendientes.length });
    try {
      for (let i = 0; i < pendientes.length; i += FILAS_POR_TANDA) {
        const tanda = pendientes.slice(i, i + FILAS_POR_TANDA);
        const resp = await fetchConSesion("/api/pedidos/importar", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ clienteId, filas: tanda.map((f) => f.original) }),
        });
        const importadas = await leerJson<FilaImportada[]>(resp);
        setResultados((previos) => ({ ...previos, ...Object.fromEntries(importadas.map((r) => [r.numero, r])) }));
        setAvance({ hechas: Math.min(i + FILAS_POR_TANDA, pendientes.length), total: pendientes.length });
      }
    } catch (err) {
      // Lo que ya se cargó queda cargado y marcado en la tabla; el botón retoma con lo que falta.
      setError(err instanceof Error ? err.message : "La importación se interrumpió.");
    } finally {
      setAvance(null);
    }
  }

  const importando = avance !== null;
  const hechas = Object.values(resultados);
  const cargadas = hechas.filter((r) => r.pedidoId !== null);
  const fallidas = hechas.filter((r) => r.pedidoId === null);
  const dudosas = cargadas.filter((r) => r.direccionDudosa);
  const conError = (filas ?? []).filter((f) => f.estado === "error").length;
  const conAviso = (filas ?? []).filter((f) => f.estado === "aviso").length;

  return (
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Importar pedidos" />

      <Card className="mb-4 max-w-3xl">
        <CardHeader>
          <CardTitle className="text-base">Planilla</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            Un pedido por fila, en un Excel (.xlsx) con las columnas de la plantilla. Primero se muestra qué se
            va a cargar; nada se guarda hasta que confirmes.
          </p>
          <div className="flex flex-col gap-2">
            <Label>Cliente</Label>
            <ComboboxBusqueda
              items={clientes.map((c) => ({ value: String(c.id), label: c.razonSocial }))}
              value={clienteId !== null ? String(clienteId) : null}
              onValueChange={(v) => {
                setClienteId(v ? Number(v) : null);
                reiniciar();
              }}
              placeholder="Elegir cliente"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="planilla">Archivo</Label>
            <Input
              id="planilla"
              type="file"
              accept=".xlsx"
              disabled={importando}
              onChange={(e) => {
                setArchivo(e.target.files?.[0] ?? null);
                reiniciar();
              }}
            />
          </div>
          <div className="flex flex-wrap gap-2">
            <Button onClick={leerPlanilla} disabled={clienteId === null || !archivo || leyendo || importando}>
              {leyendo ? "Leyendo…" : "Leer planilla"}
            </Button>
            <Button variant="outline" onClick={descargarPlantilla}>
              Descargar plantilla
            </Button>
            <Button variant="ghost" render={<Link href="/pedidos" />} nativeButton={false}>
              Volver a pedidos
            </Button>
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      {filas && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">
              {filas.length} fila{filas.length === 1 ? "" : "s"} leída{filas.length === 1 ? "" : "s"}
              {conError > 0 && ` · ${conError} con errores`}
              {conAviso > 0 && ` · ${conAviso} posiblemente repetida${conAviso === 1 ? "" : "s"}`}
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {conAviso > 0 && (
              <div className="flex items-center gap-2">
                <Checkbox
                  id="incluir-avisos"
                  checked={incluirAvisos}
                  disabled={importando}
                  onCheckedChange={(v) => setIncluirAvisos(v === true)}
                />
                <Label htmlFor="incluir-avisos">Cargar también las posiblemente repetidas</Label>
              </div>
            )}

            <div className="flex flex-wrap items-center gap-3">
              <Button onClick={importar} disabled={importando || pendientes.length === 0}>
                {avance
                  ? `Cargando ${avance.hechas} de ${avance.total}…`
                  : `Cargar ${pendientes.length} pedido${pendientes.length === 1 ? "" : "s"}`}
              </Button>
              {hechas.length > 0 && !importando && (
                <p className="text-sm">
                  {cargadas.length} cargado{cargadas.length === 1 ? "" : "s"}
                  {fallidas.length > 0 && ` · ${fallidas.length} no se pudo cargar`}
                  {dudosas.length > 0 && ` · ${dudosas.length} con dirección para revisar antes de rutear`}
                </p>
              )}
            </div>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Fila</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Destinatario</TableHead>
                  <TableHead>Dirección</TableHead>
                  <TableHead>Entrega</TableHead>
                  <TableHead>Bultos</TableHead>
                  <TableHead>Detalle</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filas.map((f) => {
                  const resultado = resultados[f.numero];
                  const estado = resultado ? (resultado.pedidoId !== null ? "cargado" : "error") : f.estado;
                  const mensajes = resultado?.error ? [resultado.error] : f.mensajes;
                  return (
                    <TableRow key={f.numero}>
                      <TableCell>{f.numero}</TableCell>
                      <TableCell>
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${ESTILO_ESTADO[estado]}`}>
                          {estado === "ok"
                            ? "Lista"
                            : estado === "aviso"
                              ? "Repetida"
                              : estado === "cargado"
                                ? "Cargada"
                                : "Con error"}
                        </span>
                      </TableCell>
                      <TableCell>{f.pedido?.destinatarioNombre ?? f.original.destinatario ?? "—"}</TableCell>
                      <TableCell>
                        {f.pedido
                          ? `${f.pedido.calleNumero}, ${f.pedido.localidadNombre}`
                          : [f.original.calleNumero, f.original.localidad].filter(Boolean).join(", ") || "—"}
                      </TableCell>
                      <TableCell>{f.pedido?.fechaEntrega ?? f.original.fechaEntrega ?? "—"}</TableCell>
                      <TableCell>{f.pedido?.bultos ?? f.original.bultos ?? "—"}</TableCell>
                      <TableCell className="whitespace-normal">
                        {resultado?.pedidoId != null && (
                          <Link href={`/pedidos/${resultado.pedidoId}`} className="hover:underline">
                            Pedido #{resultado.pedidoId}
                            {resultado.direccionDudosa && " · revisar dirección"}
                          </Link>
                        )}
                        {mensajes.length > 0 && (
                          <span className={estado === "error" ? "text-destructive" : "text-muted-foreground"}>
                            {mensajes.join(" ")}
                          </span>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
