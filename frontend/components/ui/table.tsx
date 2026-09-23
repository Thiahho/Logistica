"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/** Flechas del indicador de orden que las pantallas ordenables agregan dentro del <th>. */
const INDICADOR_ORDEN = /\s*[▲▼↑↓]\s*$/

/**
 * Tabla responsive. Desde `md` es una tabla normal dentro de una tarjeta. En pantalla chica cada fila
 * pasa a ser una tarjeta con pares "etiqueta: valor" (por CSS, con `max-md:`), porque una tabla de 8–10
 * columnas no entra en un teléfono. La etiqueta de cada celda sale de su <th> (mismo índice de columna):
 * se estampa como `data-rotulo` después de cada render, y otra vez con un MutationObserver cuando las
 * filas cambian por sondeo o paginación — así ninguna página tiene que rotular sus <TableCell>.
 * Una celda puede fijar su etiqueta con `data-label`; `tarjetas={false}` deja la tabla con scroll
 * horizontal (para tablas de 2–3 columnas que no ganan nada apiladas).
 */
function Table({
  className,
  tarjetas = true,
  ...props
}: React.ComponentProps<"table"> & { tarjetas?: boolean }) {
  const ref = React.useRef<HTMLTableElement>(null)

  React.useLayoutEffect(() => {
    const tabla = ref.current
    if (!tabla || !tarjetas) return

    const rotular = () => {
      const rotulos = Array.from(tabla.querySelectorAll("thead th")).map(
        (th) => th.getAttribute("data-label") ?? (th.textContent ?? "").replace(INDICADOR_ORDEN, "").trim()
      )
      for (const fila of tabla.querySelectorAll("tbody tr")) {
        let columna = 0
        for (const celda of Array.from(fila.children) as HTMLTableCellElement[]) {
          const rotulo = celda.colSpan > 1 ? "" : (celda.getAttribute("data-label") ?? rotulos[columna] ?? "")
          if (celda.getAttribute("data-rotulo") !== rotulo) celda.setAttribute("data-rotulo", rotulo)
          columna += celda.colSpan || 1
        }
      }
    }

    rotular()
    const observador = new MutationObserver(rotular)
    observador.observe(tabla, { childList: true, subtree: true, characterData: true })
    return () => observador.disconnect()
  }, [tarjetas])

  return (
    <div
      data-slot="table-container"
      className="relative w-full overflow-x-auto md:rounded-2xl md:border md:bg-card"
    >
      <table
        ref={ref}
        data-slot="table"
        data-tarjetas={tarjetas ? "" : undefined}
        className={cn("w-full caption-bottom text-sm", tarjetas && "max-md:block", className)}
        {...props}
      />
    </div>
  )
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return (
    <thead
      data-slot="table-header"
      className={cn("bg-muted/60 [&_tr]:border-b max-md:[[data-tarjetas]_&]:sr-only", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn(
        "[&_tr:last-child]:border-0 max-md:[[data-tarjetas]_&]:flex max-md:[[data-tarjetas]_&]:flex-col max-md:[[data-tarjetas]_&]:gap-3",
        className
      )}
      {...props}
    />
  )
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn(
        "border-t bg-muted/50 font-medium [&>tr]:last:border-b-0",
        className
      )}
      {...props}
    />
  )
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        "border-b transition-colors hover:bg-accent/60 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted",
        "max-md:[[data-tarjetas]_&]:flex max-md:[[data-tarjetas]_&]:flex-col max-md:[[data-tarjetas]_&]:gap-0.5 max-md:[[data-tarjetas]_&]:rounded-2xl max-md:[[data-tarjetas]_&]:border max-md:[[data-tarjetas]_&]:bg-card max-md:[[data-tarjetas]_&]:p-3 max-md:[[data-tarjetas]_&]:shadow-sm",
        className
      )}
      {...props}
    />
  )
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-10 px-3 text-left align-middle text-xs font-semibold tracking-wide whitespace-nowrap text-muted-foreground uppercase [&_button]:uppercase [&_button]:tracking-wide [&:has([role=checkbox])]:pr-0",
        className
      )}
      {...props}
    />
  )
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return (
    <td
      data-slot="table-cell"
      className={cn(
        "p-2 px-3 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0",
        "max-md:[[data-tarjetas]_&]:flex max-md:[[data-tarjetas]_&]:items-start max-md:[[data-tarjetas]_&]:justify-between max-md:[[data-tarjetas]_&]:gap-3 max-md:[[data-tarjetas]_&]:whitespace-normal max-md:[[data-tarjetas]_&]:px-0 max-md:[[data-tarjetas]_&]:py-1 max-md:[[data-tarjetas]_&]:text-right max-md:[[data-tarjetas]_&]:before:shrink-0 max-md:[[data-tarjetas]_&]:before:text-left max-md:[[data-tarjetas]_&]:before:text-xs max-md:[[data-tarjetas]_&]:before:font-medium max-md:[[data-tarjetas]_&]:before:text-muted-foreground max-md:[[data-tarjetas]_&]:before:content-[attr(data-rotulo)] max-md:[[data-tarjetas]_&]:first:font-semibold max-md:[[data-tarjetas]_&]:empty:hidden",
        className
      )}
      {...props}
    />
  )
}

function TableCaption({
  className,
  ...props
}: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("mt-4 text-sm text-muted-foreground", className)}
      {...props}
    />
  )
}

export {
  Table,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
}
