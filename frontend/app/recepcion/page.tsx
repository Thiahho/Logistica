"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { leerError, leerJson } from "@/lib/api/errores";
import type { PedidoRecepcionPendiente } from "@/lib/dominio/tipos";

/**
 * B13 (diseño_b13_recepcion_portal.md §5): cola de trabajo, no el detalle de un pedido a la vez.
 * Los pedidos donde `bultos === bultosDeclaradoCliente` se confirman en lote sin abrir cada uno;
 * los que difieren exigen una nota y se confirman fila por fila (PedidosController.ConfirmarRecepcion
 * la exige cuando difiere) — mismo criterio que el resto del sistema (nota obligatoria si se corrige
 * lo que declaró otro actor).
 */
export default function RecepcionPage() {
  return (
    <RequireRole roles={["administracion", "operacion"]}>
      <ColaRecepcion />
    </RequireRole>
  );
}

function ColaRecepcion() {
  const { fetchConSesion } = useAuth();
  const [filas, setFilas] = useState<PedidoRecepcionPendiente[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [seleccionados, setSeleccionados] = useState<Set<number>>(new Set());
  const [confirmandoLote, setConfirmandoLote] = useState(false);
  const [erroresFila, setErroresFila] = useState<Record<number, string>>({});

  const cargar = useCallback(() => {
    fetchConSesion("/api/pedidos/recepcion-pendiente")
      .then((r) => leerJson<PedidoRecepcionPendiente[]>(r))
      .then(setFilas)
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar la cola de recepción."));
  }, [fetchConSesion]);

  useEffect(() => {
    cargar();
  }, [cargar]);

  const coinciden = (filas ?? []).filter((f) => f.bultos === f.bultosDeclaradoCliente);
  const conDiferencia = (filas ?? []).filter((f) => f.bultos !== f.bultosDeclaradoCliente);
  const todosEnPagina = coinciden.length > 0 && coinciden.every((f) => seleccionados.has(f.id));
  const algunoEnPagina = coinciden.some((f) => seleccionados.has(f.id));

  function alternarFila(id: number) {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function alternarTodos() {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (todosEnPagina) for (const f of coinciden) next.delete(f.id);
      else for (const f of coinciden) next.add(f.id);
      return next;
    });
  }

  async function confirmar(id: number, bultosConfirmados: number, nota?: string) {
    const resp = await fetchConSesion(`/api/pedidos/${id}/recepcion`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ bultosConfirmados, nota: nota || undefined }),
    });
    if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
  }

  async function confirmarLote() {
    setConfirmandoLote(true);
    const idsElegidos = coinciden.filter((f) => seleccionados.has(f.id));
    // Independiente por fila (Promise.allSettled, no Promise.all): un 409 aislado — alguien cerró
    // la planificación de ese pedido mientras la cola estaba abierta — no puede tirar el resto del
    // lote, mismo criterio que el resultado por ítem de AvisoCobranzaDialog.
    const resultados = await Promise.allSettled(
      idsElegidos.map((f) => confirmar(f.id, f.bultosDeclaradoCliente).then(() => f.id)),
    );

    const idsOk = new Set<number>();
    const nuevosErrores: Record<number, string> = {};
    resultados.forEach((r, i) => {
      if (r.status === "fulfilled") idsOk.add(r.value);
      else nuevosErrores[idsElegidos[i].id] = r.reason instanceof Error ? r.reason.message : "No se pudo confirmar.";
    });

    setFilas((prev) => (prev ?? []).filter((f) => !idsOk.has(f.id)));
    setSeleccionados((prev) => {
      const next = new Set(prev);
      for (const id of idsOk) next.delete(id);
      return next;
    });
    setErroresFila((prev) => ({ ...prev, ...nuevosErrores }));
    setConfirmandoLote(false);
  }

  return (
    <div className="p-4 md:p-8">
      <CabeceraSesion titulo="Recepción" />
      {error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : !filas ? (
        <p className="text-muted-foreground">Cargando…</p>
      ) : filas.length === 0 ? (
        <p className="text-muted-foreground">Sin pedidos de portal pendientes de recepción hoy.</p>
      ) : (
        <div className="flex flex-col gap-8">
          <section>
            <div className="flex items-center justify-between mb-3">
              <h2 className="text-sm font-medium text-muted-foreground">
                Coinciden con lo declarado ({coinciden.length})
              </h2>
              {seleccionados.size > 0 && (
                <Button size="sm" disabled={confirmandoLote} onClick={confirmarLote}>
                  {confirmandoLote ? "Confirmando…" : `Confirmar ${seleccionados.size} seleccionado${seleccionados.size === 1 ? "" : "s"}`}
                </Button>
              )}
            </div>
            {coinciden.length === 0 ? (
              <p className="text-sm text-muted-foreground">Ninguno todavía.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-10">
                      <Checkbox
                        checked={todosEnPagina}
                        indeterminate={algunoEnPagina && !todosEnPagina}
                        onCheckedChange={alternarTodos}
                        aria-label="Seleccionar todos los que coinciden"
                      />
                    </TableHead>
                    <TableHead>Cliente</TableHead>
                    <TableHead>Destinatario</TableHead>
                    <TableHead>Bultos</TableHead>
                    <TableHead>Cargado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {coinciden.map((f) => (
                    <TableRow key={f.id}>
                      <TableCell>
                        <Checkbox
                          checked={seleccionados.has(f.id)}
                          onCheckedChange={() => alternarFila(f.id)}
                          aria-label={`Seleccionar pedido ${f.id}`}
                        />
                      </TableCell>
                      <TableCell>
                        <Link href={`/pedidos/${f.id}`} className="hover:underline">
                          {f.clienteRazonSocial}
                        </Link>
                      </TableCell>
                      <TableCell>{f.destinatarioNombre}</TableCell>
                      <TableCell>{f.bultos}</TableCell>
                      <TableCell className="text-muted-foreground">
                        {new Date(f.creadoEn).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" })}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </section>

          <section>
            <h2 className="text-sm font-medium text-muted-foreground mb-3">
              Con diferencia — requieren corrección y nota ({conDiferencia.length})
            </h2>
            {conDiferencia.length === 0 ? (
              <p className="text-sm text-muted-foreground">Ninguno.</p>
            ) : (
              <ul className="flex flex-col gap-3">
                {conDiferencia.map((f) => (
                  <FilaConDiferencia
                    key={f.id}
                    fila={f}
                    errorPrevio={erroresFila[f.id]}
                    onConfirmar={confirmar}
                    onConfirmado={() => setFilas((prev) => (prev ?? []).filter((x) => x.id !== f.id))}
                  />
                ))}
              </ul>
            )}
          </section>
        </div>
      )}
    </div>
  );
}

function FilaConDiferencia({
  fila,
  errorPrevio,
  onConfirmar,
  onConfirmado,
}: {
  fila: PedidoRecepcionPendiente;
  errorPrevio?: string;
  onConfirmar: (id: number, bultosConfirmados: number, nota?: string) => Promise<void>;
  onConfirmado: () => void;
}) {
  const [bultosConfirmados, setBultosConfirmados] = useState(fila.bultos);
  const [nota, setNota] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(errorPrevio ?? null);

  async function confirmarFila() {
    setEnviando(true);
    setError(null);
    try {
      await onConfirmar(fila.id, bultosConfirmados, nota);
      onConfirmado();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo confirmar.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <li className="rounded-lg border p-4 flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <Link href={`/pedidos/${fila.id}`} className="font-medium hover:underline">
          {fila.clienteRazonSocial} — {fila.destinatarioNombre}
        </Link>
        <span className="text-sm text-muted-foreground">
          Declarado: {fila.bultosDeclaradoCliente} · Cargado: {fila.bultos}
        </span>
      </div>
      <div className="grid grid-cols-[auto_1fr_auto] items-end gap-3">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted-foreground" htmlFor={`bultos-${fila.id}`}>
            Bultos confirmados
          </label>
          <Input
            id={`bultos-${fila.id}`}
            type="number"
            min={0}
            className="w-24"
            value={bultosConfirmados}
            onChange={(e) => setBultosConfirmados(Number(e.target.value))}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted-foreground" htmlFor={`nota-${fila.id}`}>
            Nota (obligatoria si difiere de lo cargado)
          </label>
          <Input id={`nota-${fila.id}`} value={nota} onChange={(e) => setNota(e.target.value)} />
        </div>
        <Button disabled={enviando} onClick={confirmarFila}>
          {enviando ? "Confirmando…" : "Confirmar"}
        </Button>
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}
    </li>
  );
}
