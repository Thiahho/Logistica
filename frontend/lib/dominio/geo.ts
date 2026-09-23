export interface Punto {
  lat: number;
  lng: number;
}

const RADIO_TIERRA_M = 6371000;

/**
 * Distancia en metros entre dos puntos (fórmula de haversine). Sin API de matriz de distancias
 * (construccion_v1.md §1): con 8 a 12 paradas, nearest-neighbor + 2-opt sobre distancia en línea
 * recta queda a pocos puntos del óptimo.
 */
export function haversine(a: Punto, b: Punto): number {
  const rad = (deg: number) => (deg * Math.PI) / 180;
  const dLat = rad(b.lat - a.lat);
  const dLng = rad(b.lng - a.lng);
  const senoLat = Math.sin(dLat / 2);
  const senoLng = Math.sin(dLng / 2);
  const h = senoLat * senoLat + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * senoLng * senoLng;
  return 2 * RADIO_TIERRA_M * Math.asin(Math.sqrt(h));
}
