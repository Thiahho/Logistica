import { haversine, type Punto } from "./geo";

export interface ParadaParaOrden {
  ubicacionId: number;
  lat: number | null;
  lng: number | null;
  anclada: boolean;
}

/**
 * Nearest-neighbor desde el origen de la ruta (depósito o donde haya quedado el vehículo — el
 * planificador lo elige al armar, acta changelog 3.6) + mejora 2-opt local (construccion_v1.md
 * §1). Las paradas ancladas (RF-13, urgentes) mantienen la posición que ya tenían en `paradas`;
 * lo mismo una parada sin coordenada geocodificada — no se le inventa una posición (antes recibía
 * la del depósito como fallback, lo que la empujaba artificialmente al principio del recorrido).
 * El resto se reordena "alrededor" — ocupa los huecos que dejan las ancladas, en el orden que da
 * la sugerencia geográfica. Reordenamiento manual (RF-12) sigue disponible después: esto solo
 * produce un borrador, no un resultado final.
 *
 * Devuelve un array nuevo del mismo largo; no muta `paradas`.
 */
export function sugerirOrden(origen: Punto, paradas: ParadaParaOrden[]): ParadaParaOrden[] {
  const fijas = (p: ParadaParaOrden) => p.anclada || p.lat === null || p.lng === null;

  const libres = paradas.filter(
    (p): p is ParadaParaOrden & Punto => !fijas(p) && p.lat !== null && p.lng !== null,
  );
  if (libres.length > 1) {
    const orden = nearestNeighbor(origen, libres);
    mejorar2Opt(origen, orden);
    libres.splice(0, libres.length, ...orden);
  }

  const resultado: ParadaParaOrden[] = [];
  let cursor = 0;
  for (const p of paradas) resultado.push(fijas(p) ? p : libres[cursor++]);
  return resultado;
}

function nearestNeighbor<T extends Punto>(origen: Punto, paradas: T[]): T[] {
  const restantes = [...paradas];
  const ordenadas: T[] = [];
  let actual: Punto = origen;

  while (restantes.length > 0) {
    let mejorIndice = 0;
    let mejorDistancia = Infinity;
    for (let i = 0; i < restantes.length; i++) {
      const d = haversine(actual, restantes[i]);
      if (d < mejorDistancia) {
        mejorDistancia = d;
        mejorIndice = i;
      }
    }
    const [siguiente] = restantes.splice(mejorIndice, 1);
    ordenadas.push(siguiente);
    actual = siguiente;
  }
  return ordenadas;
}

/** Mejora local: invierte tramos si acorta el recorrido total, hasta que no mejora más. Muta
 * `orden` in-place (uso interno de sugerirOrden, que ya trabaja sobre una copia). */
function mejorar2Opt<T extends Punto>(origen: Punto, orden: T[]): void {
  if (orden.length < 3) return;

  const distanciaTotal = (ruta: T[]) => {
    let total = haversine(origen, ruta[0]);
    for (let i = 0; i < ruta.length - 1; i++) total += haversine(ruta[i], ruta[i + 1]);
    return total;
  };

  let mejoro = true;
  while (mejoro) {
    mejoro = false;
    for (let i = 0; i < orden.length - 1; i++) {
      for (let j = i + 1; j < orden.length; j++) {
        const candidato = [...orden];
        const tramo = candidato.slice(i, j + 1).reverse();
        candidato.splice(i, tramo.length, ...tramo);
        if (distanciaTotal(candidato) < distanciaTotal(orden) - 1) {
          orden.splice(0, orden.length, ...candidato);
          mejoro = true;
        }
      }
    }
  }
}
