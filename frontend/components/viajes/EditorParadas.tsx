"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, Plus, Route, Trash2 } from "lucide-react";
import { ComboboxBusqueda } from "@/components/ComboboxBusqueda";
import { SelectorDireccion, type DireccionResuelta } from "@/components/SelectorDireccion";
import { MapaDinamico } from "@/components/mapa/MapaDinamico";
import type { MarcadorMapa } from "@/components/mapa/Mapa";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { ClienteDestinatarioResumen, ParadaPrevista, ParadaViajeEntrada, PrevisualizacionViaje } from "@/lib/dominio/tipos";

interface Fila {
  clave: number;
  direccion: DireccionResuelta | null;
  /** Para remontar SelectorDireccion al elegir un contacto (no es controlado). */
  inicial: DireccionResuelta | null;
  version: number;
  contactoId: string | null;
  nombre: string;
  telefono: string;
  bultos: number;
  observaciones: string;
  /** Destinatario nuevo: guardarlo en "Mis clientes" al confirmar. */
  guardarContacto: boolean;
}

export interface ResultadoPrevisualizacion {
  recorrido: PrevisualizacionViaje;
  /** Precio por parada, alineado con el orden de carga; null = no se muestran (empleado, BackOffice). */
  precios: (number | null)[] | null;
  total: number | null;
}

let proximaClave = 1;
const filaVacia = (): Fila => ({
  clave: proximaClave++, direccion: null, inicial: null, version: 0, contactoId: null,
  nombre: "", telefono: "", bultos: 1, observaciones: "", guardarContacto: true,
});

const pesos = (n: number) => `$${n.toLocaleString("es-AR", { maximumFractionDigits: 2 })}`;

/**
 * Carga de un envío con una o varias paradas, compartida por el portal y el BackOffice. Cada parada:
 * el destinatario (de "Mis clientes", que trae su dirección, o uno nuevo con su dirección de entrega),
 * bultos y observaciones. El "+" suma otra parada. "Cerrar ruta" pide al servidor el orden (vecino
 * más cercano + 2-opt, Dominio/OrdenParadas.cs), muestra el mapa, los km y el precio, deja ajustar el
 * orden y recién ahí se confirma. Las llamadas a la API las hace la página: sabe a qué puerta pegarle
 * y qué datos comunes (fecha, vehículo, cliente) mandar, y si con una sola parada es un envío común.
 */
export function EditorParadas({
  basePath,
  contactos = [],
  maxParadas = 24,
  permitirGuardarContacto = false,
  previsualizar,
  confirmar,
}: {
  /** Para SelectorDireccion: "/api/mi-cuenta" en el portal, "/api" en el BackOffice. */
  basePath: string;
  contactos?: ClienteDestinatarioResumen[];
  maxParadas?: number;
  /** Portal: ofrece guardar en "Mis clientes" a los destinatarios nuevos. */
  permitirGuardarContacto?: boolean;
  previsualizar: (paradas: ParadaViajeEntrada[]) => Promise<ResultadoPrevisualizacion>;
  /** Recibe las paradas en el orden final (el sugerido o el ajustado a mano) y, aparte, los
   * destinatarios nuevos que hay que guardar en "Mis clientes". */
  confirmar: (paradasEnOrden: ParadaViajeEntrada[], contactosNuevos: ParadaViajeEntrada[]) => Promise<void>;
}) {
  const [filas, setFilas] = useState<Fila[]>(() => [filaVacia()]);
  const [previa, setPrevia] = useState<ResultadoPrevisualizacion | null>(null);
  // Orden en pantalla: arranca en el sugerido y se puede ajustar con las flechas.
  const [orden, setOrden] = useState<ParadaPrevista[]>([]);
  const [ordenManual, setOrdenManual] = useState(false);
  const [calculando, setCalculando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const completas = filas.every((f) => f.direccion && f.nombre.trim() && f.telefono.trim() && f.bultos >= 1);
  const unaSola = filas.length === 1;

  // Cualquier cambio en las paradas invalida la revisión: hay que volver a cerrar la ruta.
  function cambiarFila(clave: number, cambio: Partial<Fila>) {
    setFilas((fs) => fs.map((f) => (f.clave === clave ? { ...f, ...cambio } : f)));
    setPrevia(null);
  }

  function elegirContacto(clave: number, id: string | null) {
    const c = contactos.find((x) => x.id === id);
    if (!c) {
      cambiarFila(clave, { contactoId: null });
      return;
    }
    const direccion: DireccionResuelta = {
      ubicacionId: c.destinoUbicacionId, calleNumero: c.destinoCalleNumero, localidadId: c.localidadId,
      localidadNombre: c.localidadNombre, lat: c.lat, lng: c.lng, geoConfianza: c.geoConfianza,
    };
    setFilas((fs) => fs.map((f) => f.clave === clave
      ? { ...f, contactoId: c.id, nombre: c.nombre, telefono: c.telefono, direccion, inicial: direccion, version: f.version + 1,
          observaciones: f.observaciones || (c.observaciones ?? "") }
      : f));
    setPrevia(null);
  }

  function agregarParada() {
    setFilas((fs) => [...fs, filaVacia()]);
    setPrevia(null);
  }

  const aEntrada = (f: Fila): ParadaViajeEntrada => ({
    destinoUbicacionId: f.direccion!.ubicacionId,
    destinatarioNombre: f.nombre.trim(),
    destinatarioTelefono: f.telefono.trim(),
    bultos: f.bultos,
    observaciones: f.observaciones.trim() || null,
  });

  async function cerrarRuta() {
    setError(null);
    setCalculando(true);
    try {
      const r = await previsualizar(filas.map(aEntrada));
      setPrevia(r);
      setOrden(r.recorrido.paradas);
      setOrdenManual(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo calcular la ruta.");
    } finally {
      setCalculando(false);
    }
  }

  function mover(i: number, sentido: -1 | 1) {
    setOrden((o) => {
      const n = [...o];
      [n[i], n[i + sentido]] = [n[i + sentido], n[i]];
      return n;
    });
    setOrdenManual(true);
  }

  async function confirmarCarga() {
    const paradas = filas.map(aEntrada);
    const nuevos = permitirGuardarContacto
      ? filas.filter((f) => f.contactoId === null && f.guardarContacto).map(aEntrada)
      : [];
    setError(null);
    setConfirmando(true);
    try {
      await confirmar(orden.flatMap((p) => p.indices).map((i) => paradas[i]), nuevos);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el envío.");
    } finally {
      setConfirmando(false);
    }
  }

  const origen = previa?.recorrido.origen;
  const marcadores: MarcadorMapa[] = previa
    ? [
        ...(origen && origen.lat !== null && origen.lng !== null
          ? [{ id: "origen", punto: { lat: origen.lat, lng: origen.lng }, etiqueta: "D", imagen: "/logo-solo.png",
               variante: "origen" as const, titulo: `Salida: ${origen.nombre}` }]
          : []),
        ...orden.flatMap((p, i) => p.lat !== null && p.lng !== null
          ? [{ id: p.ubicacionId, punto: { lat: p.lat, lng: p.lng }, etiqueta: String(i + 1),
               variante: p.direccionApta ? ("pendiente" as const) : ("dudosa" as const),
               titulo: `${i + 1}. ${p.calleNumero}` }]
          : []),
      ]
    : [];
  const dudosas = orden.filter((p) => !p.direccionApta).length;

  return (
    <div className="flex flex-col gap-4">
      {filas.map((f, i) => (
        <Card key={f.clave}>
          <CardHeader className="flex flex-row items-center justify-between gap-2">
            <CardTitle className="text-base">{unaSola ? "Entrega" : `Parada ${i + 1}`}</CardTitle>
            {!unaSola && (
              <Button
                variant="ghost"
                size="sm"
                aria-label={`Quitar parada ${i + 1}`}
                onClick={() => {
                  setFilas((fs) => fs.filter((x) => x.clave !== f.clave));
                  setPrevia(null);
                }}
              >
                <Trash2 className="size-4" /> Quitar
              </Button>
            )}
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {contactos.length > 0 && (
              <div className="flex flex-col gap-2">
                <Label>Cliente de la libreta</Label>
                <ComboboxBusqueda
                  items={contactos.map((c) => ({ value: c.id, label: c.nombre }))}
                  value={f.contactoId}
                  onValueChange={(id) => elegirContacto(f.clave, id)}
                  placeholder="Elegir de Mis clientes, o cargar uno nuevo abajo…"
                />
              </div>
            )}
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <div className="flex flex-col gap-2">
                <Label htmlFor={`nombre-${f.clave}`}>Destinatario</Label>
                <Input id={`nombre-${f.clave}`} value={f.nombre} onChange={(e) => cambiarFila(f.clave, { nombre: e.target.value })} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`tel-${f.clave}`}>Teléfono</Label>
                <Input id={`tel-${f.clave}`} value={f.telefono} onChange={(e) => cambiarFila(f.clave, { telefono: e.target.value })} />
              </div>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Dirección de entrega</Label>
              <SelectorDireccion
                key={`${f.clave}-${f.version}`}
                idPrefijo={`parada-${f.clave}`}
                inicial={f.inicial}
                onCambio={(d) => cambiarFila(f.clave, { direccion: d })}
                basePath={basePath}
                permitirLinkMapa
              />
            </div>
            <div className="grid grid-cols-[6rem_1fr] gap-3">
              <div className="flex flex-col gap-2">
                <Label htmlFor={`bultos-${f.clave}`}>Bultos</Label>
                <Input
                  id={`bultos-${f.clave}`}
                  type="number"
                  min={1}
                  max={999}
                  value={f.bultos}
                  onChange={(e) => cambiarFila(f.clave, { bultos: Number(e.target.value) })}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`obs-${f.clave}`}>Observaciones (opcional)</Label>
                <Input id={`obs-${f.clave}`} value={f.observaciones} onChange={(e) => cambiarFila(f.clave, { observaciones: e.target.value })} />
              </div>
            </div>
            {permitirGuardarContacto && f.contactoId === null && (
              <div className="flex items-center gap-2">
                <Checkbox
                  id={`guardar-${f.clave}`}
                  checked={f.guardarContacto}
                  onCheckedChange={(v) => setFilas((fs) => fs.map((x) => (x.clave === f.clave ? { ...x, guardarContacto: v === true } : x)))}
                />
                <Label htmlFor={`guardar-${f.clave}`} className="font-normal">
                  Guardar este destinatario en Mis clientes
                </Label>
              </div>
            )}
          </CardContent>
        </Card>
      ))}

      <button
        type="button"
        onClick={agregarParada}
        disabled={filas.length >= maxParadas}
        className="flex items-center justify-center gap-2 rounded-xl border-2 border-dashed p-4 text-sm font-medium text-bf-azul transition-colors hover:bg-accent disabled:opacity-50"
      >
        <span className="flex size-8 items-center justify-center rounded-full bg-bf-azul text-white">
          <Plus className="size-5" />
        </span>
        Agregar otra parada
      </button>

      <div className="flex flex-wrap items-center gap-3">
        <Button size="lg" onClick={cerrarRuta} disabled={!completas || calculando}>
          <Route className="size-4" /> {calculando ? "Calculando…" : "Cerrar ruta"}
        </Button>
        <span className="text-xs text-muted-foreground">
          {filas.length} {filas.length === 1 ? "parada" : "paradas"} de {maxParadas}
          {!completas && " · completá destinatario, teléfono y dirección de cada una"}
        </span>
      </div>

      {previa && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{unaSola ? "Revisá el envío" : "Ruta sugerida"}</CardTitle>
            <p className="text-sm text-muted-foreground">
              Sale de {previa.recorrido.origen.nombre} · {previa.recorrido.km.toLocaleString("es-AR")} km
              {previa.recorrido.fuenteKm === "recta" ? " en línea recta (aprox.)" : " por calle"}
              {ordenManual && " · orden ajustado a mano"}
            </p>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <MapaDinamico
              marcadores={marcadores}
              // Con el orden cambiado a mano la línea por calle ya no corresponde: el mapa une en recta.
              recorrido={ordenManual ? null : previa.recorrido.linea}
            />
            {dudosas > 0 && (
              <p className="text-sm text-amber-700">
                {dudosas === 1 ? "Una dirección no se pudo ubicar bien" : `${dudosas} direcciones no se pudieron ubicar bien`}: se
                carga igual y la Empresa la revisa antes de sumarla a la ruta.
              </p>
            )}
            <ol className="flex flex-col divide-y rounded-lg border">
              {orden.map((p, i) => (
                <li key={p.ubicacionId} className="flex items-center gap-3 p-3 text-sm">
                  <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-accent font-semibold text-bf-azul">
                    {i + 1}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{p.indices.map((x) => filas[x]?.nombre).join(" · ")}</p>
                    <p className="truncate text-muted-foreground">
                      {p.calleNumero}{p.localidad ? `, ${p.localidad}` : ""} · {p.indices.reduce((s, x) => s + (filas[x]?.bultos ?? 0), 0)} bultos
                      {!p.direccionApta && <span className="text-amber-700"> · a verificar</span>}
                    </p>
                  </div>
                  {previa.precios && (
                    <span className="shrink-0 tabular-nums">
                      {p.indices.every((x) => previa.precios![x] !== null)
                        ? pesos(p.indices.reduce((s, x) => s + (previa.precios![x] ?? 0), 0))
                        : "A cotizar"}
                    </span>
                  )}
                  {orden.length > 1 && (
                    <div className="flex shrink-0 gap-1">
                      <Button size="icon" variant="ghost" aria-label="Subir" disabled={i === 0} onClick={() => mover(i, -1)}>
                        <ArrowUp className="size-4" />
                      </Button>
                      <Button size="icon" variant="ghost" aria-label="Bajar" disabled={i === orden.length - 1} onClick={() => mover(i, 1)}>
                        <ArrowDown className="size-4" />
                      </Button>
                    </div>
                  )}
                </li>
              ))}
            </ol>
            {previa.precios && (
              <p className="text-right text-sm">
                Total:{" "}
                <span className="font-semibold tabular-nums">
                  {previa.total !== null ? pesos(previa.total) : "parte a cotizar"}
                </span>
                {!unaSola && <span className="block text-xs text-muted-foreground">Cada parada se cotiza como un envío.</span>}
              </p>
            )}
            <Button onClick={confirmarCarga} disabled={confirmando} className="self-start" size="lg">
              {confirmando ? "Cargando…" : unaSola ? "Confirmar envío" : "Confirmar ruta"}
            </Button>
          </CardContent>
        </Card>
      )}

      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  );
}
