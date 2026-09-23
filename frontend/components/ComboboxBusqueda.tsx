"use client";

import { useMemo } from "react";
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox";
import { cn } from "@/lib/utils";

export interface OpcionCombobox {
  value: string;
  label: string;
  /** Texto secundario en gris (ej. el partido de una localidad). */
  detalle?: string;
}

interface ComboboxBusquedaProps {
  items: OpcionCombobox[];
  value: string | null;
  onValueChange: (value: string | null) => void;
  placeholder?: string;
  /** Con estos dos, el filtrado pasa a ser responsabilidad del padre (server-side): `items` ya
   * viene filtrado y acá se desactiva el filtro local. Omitidos, filtra en memoria sobre `items`. */
  textoBusqueda?: string;
  onTextoBusquedaChange?: (texto: string) => void;
  cargando?: boolean;
  mensajeVacio?: string;
  disabled?: boolean;
  id?: string;
  className?: string;
}

/**
 * Combobox con búsqueda por texto — reemplaza al `Select` de lista fija para Cliente y Localidad
 * (hoy sin filtro de texto). El `value` que expone es siempre el string id, igual que el `Select`
 * que reemplaza (`items={[{value,label}]}`), para que los call sites se reescriban casi línea por
 * línea.
 *
 * Filtrado en memoria por default: Cliente y Localidad ya llegan completos del backend en el
 * mount, así que filtrar un array en memoria es gratis. El punto de quiebre real está en el
 * orden de las 300-500 filas, no en la cantidad de hoy — cuando el área de cobertura de
 * localidades se defina (decisión comercial pendiente), la migración a server-side es agregar
 * `?q=` a `GET /api/localidades` y pasarle `textoBusqueda`/`onTextoBusquedaChange` acá, sin tocar
 * el resto del call site.
 */
export function ComboboxBusqueda({
  items,
  value,
  onValueChange,
  placeholder,
  textoBusqueda,
  onTextoBusquedaChange,
  cargando,
  mensajeVacio = "Sin resultados.",
  disabled,
  id,
  className,
}: ComboboxBusquedaProps) {
  const filtradoServidor = onTextoBusquedaChange !== undefined;
  const seleccionado = useMemo(() => items.find((i) => i.value === value) ?? null, [items, value]);

  return (
    <Combobox
      items={items}
      value={seleccionado}
      onValueChange={(item) => onValueChange(item?.value ?? null)}
      inputValue={filtradoServidor ? textoBusqueda : undefined}
      onInputValueChange={(texto) => onTextoBusquedaChange?.(texto)}
      itemToStringLabel={(item: OpcionCombobox) => item.label}
      itemToStringValue={(item: OpcionCombobox) => item.value}
      isItemEqualToValue={(a: OpcionCombobox, b: OpcionCombobox) => a.value === b.value}
      filter={filtradoServidor ? null : undefined}
      disabled={disabled}
    >
      <ComboboxInput id={id} placeholder={placeholder} className={cn("w-full", className)} />
      <ComboboxContent>
        {cargando && <p className="px-2 py-1.5 text-xs text-muted-foreground">Buscando…</p>}
        <ComboboxEmpty>{mensajeVacio}</ComboboxEmpty>
        <ComboboxList>
          {(item: OpcionCombobox) => (
            <ComboboxItem key={item.value} value={item}>
              <div className="flex flex-col">
                <span>{item.label}</span>
                {item.detalle && <span className="text-xs text-muted-foreground">{item.detalle}</span>}
              </div>
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  );
}
