"use client";

import { ArrowDown, ArrowUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

interface OrdenMovilProps {
  columnas: { campo: string; etiqueta: string }[];
  /** `orden` de useListadoPaginado: "campo" (ascendente) o "-campo" (descendente). */
  orden: string;
  alternarOrden: (campo: string) => void;
}

/**
 * Orden de un listado en pantalla chica. Ahí la tabla se muestra como tarjetas y el encabezado con los
 * botones de orden queda oculto (components/ui/table.tsx); esto expone el mismo orden —mismas columnas,
 * mismo alternarOrden de useListadoPaginado— con un selector de columna y un botón de sentido. No se
 * muestra desde `md`.
 */
export function OrdenMovil({ columnas, orden, alternarOrden }: OrdenMovilProps) {
  const descendente = orden.startsWith("-");
  const campo = descendente ? orden.slice(1) : orden;

  return (
    <div className="mb-3 flex items-center gap-2 md:hidden">
      <span className="text-sm text-muted-foreground">Ordenar por</span>
      <Select
        items={columnas.map((c) => ({ value: c.campo, label: c.etiqueta }))}
        value={campo}
        onValueChange={(v) => v && v !== campo && alternarOrden(v)}
      >
        <SelectTrigger className="flex-1">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {columnas.map((c) => (
            <SelectItem key={c.campo} value={c.campo}>
              {c.etiqueta}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        variant="outline"
        size="icon"
        onClick={() => alternarOrden(campo)}
        aria-label={descendente ? "Orden descendente, tocar para ascendente" : "Orden ascendente, tocar para descendente"}
      >
        {descendente ? <ArrowDown className="size-4" /> : <ArrowUp className="size-4" />}
      </Button>
    </div>
  );
}
