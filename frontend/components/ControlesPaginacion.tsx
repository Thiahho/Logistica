"use client";

import type { Dispatch, SetStateAction } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TAMANIOS_PAGINA } from "@/lib/hooks/useListadoPaginado";

interface ControlesPaginacionProps {
  pagina: number;
  setPagina: Dispatch<SetStateAction<number>>;
  totalPaginas: number;
  totalRegistros: number;
  tamanioPagina: number;
  setTamanioPagina: Dispatch<SetStateAction<number>>;
}

/** Pie de listado paginado — extraído de app/pedidos/page.tsx y app/rutas/page.tsx junto con
 * lib/hooks/useListadoPaginado.ts (E1, §6.0 del plan). */
export function ControlesPaginacion({
  pagina,
  setPagina,
  totalPaginas,
  totalRegistros,
  tamanioPagina,
  setTamanioPagina,
}: ControlesPaginacionProps) {
  return (
    <div className="mt-4 flex flex-col gap-3 md:flex-row md:items-center md:justify-between md:gap-4">
      <p className="text-sm text-muted-foreground">
        {totalRegistros === 0
          ? "Sin resultados"
          : `Mostrando ${(pagina - 1) * tamanioPagina + 1}–${Math.min(pagina * tamanioPagina, totalRegistros)} de ${totalRegistros}`}
      </p>
      <div className="flex flex-wrap items-center justify-between gap-3 md:gap-4">
        <div className="flex items-center gap-2">
          <Label htmlFor="tamanio-pagina" className="text-sm text-muted-foreground">
            Por página
          </Label>
          <Select
            items={TAMANIOS_PAGINA.map((n) => ({ value: String(n), label: String(n) }))}
            value={String(tamanioPagina)}
            onValueChange={(v) => {
              setTamanioPagina(Number(v));
              setPagina(1);
            }}
          >
            <SelectTrigger id="tamanio-pagina" className="w-20">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TAMANIOS_PAGINA.map((n) => (
                <SelectItem key={n} value={String(n)}>
                  {n}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" disabled={pagina <= 1} onClick={() => setPagina((p) => Math.max(1, p - 1))}>
            ← Anterior
          </Button>
          <span className="text-sm text-muted-foreground">
            Página {pagina} de {totalPaginas}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={pagina >= totalPaginas}
            onClick={() => setPagina((p) => Math.min(totalPaginas, p + 1))}
          >
            Siguiente →
          </Button>
        </div>
      </div>
    </div>
  );
}
