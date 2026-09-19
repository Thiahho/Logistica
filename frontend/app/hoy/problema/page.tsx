"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { AlertTriangle, ChevronLeft, PackageX } from "lucide-react";
import { RequireRole } from "@/lib/auth/RequireRole";
import { useAuth } from "@/lib/auth/AuthProvider";
import { CabeceraSesion } from "@/components/CabeceraSesion";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { leerError, leerJson } from "@/lib/api/errores";
import { comprimirFoto } from "@/lib/captura/foto";
import { etiquetaCategoria, type JornadaDelDia, type NovedadResultado } from "@/lib/dominio/tipos";

export default function ProblemaPage() {
  return (
    <RequireRole roles={["repartidor"]}>
      <Problema />
    </RequireRole>
  );
}

type Tipo = "incidencia_ruta" | "problema_carga";

function Problema() {
  const { fetchConSesion } = useAuth();
  const router = useRouter();

  const [jornada, setJornada] = useState<JornadaDelDia | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [tipo, setTipo] = useState<Tipo | null>(null);
  const [categoria, setCategoria] = useState<string | null>(null);
  const [pedidoId, setPedidoId] = useState("");
  const [descripcion, setDescripcion] = useState("");
  const [foto, setFoto] = useState<File | null>(null);

  // Generado al abrir el formulario, no al enviar: es lo que hace idempotente el reintento (RNF-02).
  // Un segundo tap, o un reintento tras perder señal, reusa el mismo y el servidor lo reconoce.
  const [deviceUuid] = useState(() => crypto.randomUUID());

  const [enviando, setEnviando] = useState(false);
  const [envioError, setEnvioError] = useState<string | null>(null);
  const [enviado, setEnviado] = useState(false);

  useEffect(() => {
    fetchConSesion("/api/mis-paradas/dia")
      .then((r) => leerJson<JornadaDelDia>(r))
      .then(setJornada)
      .catch((err) => setErrorCarga(err instanceof Error ? err.message : "No se pudo cargar la jornada."));
  }, [fetchConSesion]);

  const previewFoto = useMemo(() => (foto ? URL.createObjectURL(foto) : null), [foto]);
  useEffect(() => {
    if (!previewFoto) return;
    return () => URL.revokeObjectURL(previewFoto);
  }, [previewFoto]);

  if (errorCarga) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Reportar un problema" />
        <p className="text-sm text-destructive">{errorCarga}</p>
      </div>
    );
  }
  if (!jornada) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Reportar un problema" />
        <p className="text-muted-foreground">Cargando…</p>
      </div>
    );
  }
  if (jornada.rutaId === null) {
    return (
      <div className="p-4">
        <CabeceraSesion titulo="Reportar un problema" />
        <p className="text-muted-foreground">No tenés una ruta en curso.</p>
      </div>
    );
  }

  // Solo pedidos que siguen vivos: uno cancelado por operación no tiene "problema con la carga".
  const pedidos = jornada.paradas.flatMap((p) =>
    p.pedidos
      .filter((ped) => ped.estado !== "Cancelado")
      .map((ped) => ({ pedidoId: ped.pedidoId, etiqueta: `Parada ${p.orden} · ${ped.destinatarioNombre}` })),
  );

  const categorias = tipo === "incidencia_ruta" ? jornada.categoriasIncidencia : jornada.categoriasCarga;
  const listo =
    tipo !== null &&
    categoria !== null &&
    (tipo === "incidencia_ruta" ? descripcion.trim().length > 0 : pedidoId !== "" && (descripcion.trim().length > 0 || foto !== null));

  function elegirTipo(t: Tipo) {
    setTipo(t);
    setCategoria(null);
    setEnvioError(null);
  }

  async function enviar() {
    setEnviando(true);
    setEnvioError(null);
    try {
      const form = new FormData();
      form.append("deviceUuid", deviceUuid);
      form.append("tipo", tipo!);
      form.append("categoria", categoria!);
      if (descripcion.trim()) form.append("descripcion", descripcion.trim());
      if (tipo === "problema_carga") form.append("pedidoId", pedidoId);
      if (foto) form.append("foto", await comprimirFoto(foto), "novedad.jpg");

      const resp = await fetchConSesion("/api/mi-jornada/novedades", { method: "POST", body: form });
      if (!resp.ok) throw new Error((await leerError(resp)).mensaje);
      await leerJson<NovedadResultado>(resp);
      setEnviado(true);
    } catch (err) {
      setEnvioError(err instanceof Error ? err.message : "No se pudo enviar el reporte.");
    } finally {
      setEnviando(false);
    }
  }

  if (enviado) {
    return (
      <div className="p-4 flex flex-col gap-4">
        <CabeceraSesion titulo="Reportar un problema" />
        <div className="rounded-lg border-2 border-green-600 bg-green-50/60 p-4">
          <p className="font-semibold">Reporte enviado</p>
          <p className="text-sm text-muted-foreground">
            Operación lo ve ahora. Cuando lo responda te aparece un aviso en Hoy.
          </p>
        </div>
        <Button className="h-12 text-base" onClick={() => router.replace("/hoy")}>
          Volver a Hoy
        </Button>
      </div>
    );
  }

  return (
    <div className="p-4 flex flex-col gap-4 pb-8">
      <div className="flex items-center justify-between">
        <Button variant="outline" render={<Link href="/hoy" />} nativeButton={false} className="h-11 text-base">
          <ChevronLeft className="size-4" />
          Hoy
        </Button>
      </div>
      <CabeceraSesion titulo="Reportar un problema" />

      <div className="grid grid-cols-1 gap-2">
        <Button
          variant={tipo === "incidencia_ruta" ? "default" : "outline"}
          className="h-14 justify-start text-base"
          onClick={() => elegirTipo("incidencia_ruta")}
        >
          <AlertTriangle className="size-5" />
          No puedo continuar (vehículo, accidente…)
        </Button>
        <Button
          variant={tipo === "problema_carga" ? "default" : "outline"}
          className="h-14 justify-start text-base"
          onClick={() => elegirTipo("problema_carga")}
        >
          <PackageX className="size-5" />
          Problema con la carga
        </Button>
      </div>

      {tipo && (
        <>
          <div className="flex flex-col gap-2">
            <Label>Qué pasó</Label>
            <div className="grid grid-cols-2 gap-2">
              {categorias.map((c) => (
                <Button
                  key={c}
                  variant={categoria === c ? "default" : "outline"}
                  className="h-12 text-base"
                  onClick={() => setCategoria(c)}
                >
                  {etiquetaCategoria(c)}
                </Button>
              ))}
            </div>
          </div>

          {tipo === "problema_carga" && (
            <div className="flex flex-col gap-2">
              <Label htmlFor="pedido">En qué pedido</Label>
              <select
                id="pedido"
                className="h-12 rounded-lg border bg-background px-2.5 text-base"
                value={pedidoId}
                onChange={(e) => setPedidoId(e.target.value)}
              >
                <option value="">Elegí un pedido…</option>
                {pedidos.map((p) => (
                  <option key={p.pedidoId} value={p.pedidoId}>
                    {p.etiqueta}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="flex flex-col gap-2">
            <Label htmlFor="descripcion">
              {tipo === "incidencia_ruta" ? "Contá qué pasó" : "Nota (o mandá una foto)"}
            </Label>
            <Textarea
              id="descripcion"
              className="text-base"
              value={descripcion}
              onChange={(e) => setDescripcion(e.target.value)}
            />
          </div>

          {tipo === "problema_carga" && (
            <div className="flex flex-col gap-2">
              <Label htmlFor="foto">Foto (opcional)</Label>
              <Input
                id="foto"
                type="file"
                accept="image/*"
                capture="environment"
                onChange={(e) => setFoto(e.target.files?.[0] ?? null)}
              />
              {previewFoto && (
                // eslint-disable-next-line @next/next/no-img-element -- objectURL local, no pasa por el optimizador de next/image
                <img src={previewFoto} alt="Foto del problema" className="h-40 w-full rounded-lg border object-cover" />
              )}
            </div>
          )}

          {envioError && <p className="text-sm text-destructive">{envioError}</p>}

          <Button className="h-12 w-full text-base" disabled={!listo || enviando} onClick={enviar}>
            {enviando ? "Enviando…" : "Enviar a operación"}
          </Button>
        </>
      )}
    </div>
  );
}
