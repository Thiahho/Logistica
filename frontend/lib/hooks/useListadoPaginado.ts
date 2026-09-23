"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/lib/auth/AuthProvider";
import { leerJson } from "@/lib/api/errores";
import type { ListaPaginada } from "@/lib/dominio/tipos";

export const TAMANIOS_PAGINA = [10, 15, 20] as const;

interface OpcionesListadoPaginado {
  /** Ruta de la API, ej. "/api/pedidos" — sin query string. */
  ruta: string;
  /** Filtros ya resueltos a string; una clave con valor vacío/falsy no se manda. */
  filtros: Record<string, string>;
  ordenInicial: string;
  tamanioPaginaInicial?: number;
  /** false para deshabilitar el fetch (ej. mientras un id todavía no se conoce). */
  habilitado?: boolean;
}

/**
 * Extraído de app/pedidos/page.tsx y app/rutas/page.tsx (E1, §6.0 del plan): ambas pantallas
 * duplicaban ~120 líneas literales (mismos nombres de función, mismo pie de "Mostrando X–Y de
 * Z") — con /facturas sumando una tercera copia, se cruza la regla de tres. Lo que se extrae es
 * mecánico y estable (armado de query params, estado de página/orden, paginación); los filtros y
 * el <TableBody> quedan afuera, en cada página, porque ahí sí varían.
 */
export function useListadoPaginado<T>({
  ruta,
  filtros,
  ordenInicial,
  tamanioPaginaInicial = 15,
  habilitado = true,
}: OpcionesListadoPaginado) {
  const { fetchConSesion } = useAuth();
  const [items, setItems] = useState<T[] | null>(null);
  const [totalRegistros, setTotalRegistros] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [orden, setOrden] = useState(ordenInicial);
  const [pagina, setPagina] = useState(1);
  const [tamanioPagina, setTamanioPagina] = useState(tamanioPaginaInicial);

  // Los filtros llegan como objeto plano, nuevo en cada render del caller — se serializan para
  // que la dependencia del useCallback compare por valor, no por identidad del objeto.
  const filtrosKey = JSON.stringify(filtros);

  const cargar = useCallback(() => {
    if (!habilitado) return;
    const params = new URLSearchParams();
    for (const [clave, valor] of Object.entries(filtros)) {
      if (valor) params.set(clave, valor);
    }
    params.set("orden", orden);
    params.set("pagina", String(pagina));
    params.set("tamanioPagina", String(tamanioPagina));
    fetchConSesion(`${ruta}?${params.toString()}`)
      .then((r) => leerJson<ListaPaginada<T>>(r))
      .then((r) => {
        setItems(r.items);
        setTotalRegistros(r.total);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "No se pudo cargar el listado."));
    // filtrosKey representa a `filtros` por valor — deps exhaustivas romperían el punto de esta
    // extracción (evitar que un objeto literal nuevo por render dispare un fetch de más).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fetchConSesion, ruta, filtrosKey, orden, pagina, tamanioPagina, habilitado]);

  useEffect(() => {
    cargar();
  }, [cargar]);

  // Cualquier cambio de filtro/orden/tamaño vuelve a la página 1 — quedarse en una página que ya
  // no existe (ej. filtrar y tener menos resultados que antes) mostraría una lista vacía confusa.
  function conReinicioDePagina<V>(setter: (v: V) => void) {
    return (v: V) => {
      setter(v);
      setPagina(1);
    };
  }

  const totalPaginas = Math.max(1, Math.ceil(totalRegistros / tamanioPagina));

  function alternarOrden(campo: string) {
    const desc = `-${campo}`;
    setOrden((actual) => (actual === desc ? campo : desc));
    setPagina(1);
  }

  function indicadorOrden(campo: string): string | null {
    if (orden === campo) return "▲";
    if (orden === `-${campo}`) return "▼";
    return null;
  }

  return {
    items,
    totalRegistros,
    error,
    orden,
    pagina,
    setPagina,
    tamanioPagina,
    setTamanioPagina,
    totalPaginas,
    alternarOrden,
    indicadorOrden,
    conReinicioDePagina,
    recargar: cargar,
  };
}
