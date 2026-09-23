import type { EstadoRuta, OrigenRuta, ParadaDelDia } from "@/lib/dominio/tipos";

/** Maps admite hasta 9 puntos intermedios por link (menos en algunas apps): un tramo lleva como mucho
 * 10 paradas (9 intermedias + el destino). Una ruta más larga se parte en varios tramos encadenados. */
export const MAX_PARADAS_POR_TRAMO = 10;

type PuntoMaps = Pick<ParadaDelDia, "lat" | "lng" | "calleNumero" | "localidad">;

export interface TramoGoogleMaps {
  url: string;
  /** Número de orden de la primera y la última parada del tramo (el de la ruta, no la posición). */
  desde: number;
  hasta: number;
  /** El tramo arranca en el punto de salida de la ruta (`false`: en la última parada del tramo anterior
   * o, sin punto de salida, en la ubicación actual del teléfono). */
  saleDelOrigen: boolean;
}

/** `lat,lng` si hay coordenadas; si no, la dirección como texto (Maps la geocodifica): ninguna parada
 * se pierde por no estar geolocalizada. */
function comoPunto(p: PuntoMaps): string {
  if (p.lat !== null && p.lng !== null) return `${p.lat},${p.lng}`;
  return p.localidad ? `${p.calleNumero}, ${p.localidad}` : p.calleNumero;
}

function comoPuntoOrigen(o: OrigenRuta | null): string | null {
  if (!o) return null;
  if (o.lat !== null && o.lng !== null) return `${o.lat},${o.lng}`;
  const texto = o.localidad ? `${o.calleNumero}, ${o.localidad}` : o.calleNumero;
  return texto.trim() ? texto : null;
}

function armarUrl(destino: string, origen: string | null, intermedios: string[]): string {
  const params = new URLSearchParams({ api: "1", destination: destino, travelmode: "driving" });
  if (origen) params.set("origin", origen);
  if (intermedios.length > 0) params.set("waypoints", intermedios.join("|"));
  return `https://www.google.com/maps/dir/?${params.toString()}`;
}

/** Paradas que hay que recorrer todavía: todas mientras se planifica, solo las pendientes con la ruta
 * en curso, ninguna con la ruta cerrada. */
export function paradasParaNavegar(paradas: ParadaDelDia[], estado: EstadoRuta): ParadaDelDia[] {
  if (estado === "cerrada") return [];
  const ordenadas = [...paradas].sort((a, b) => a.orden - b.orden);
  return estado === "planificada" ? ordenadas : ordenadas.filter((p) => p.estado === "pendiente");
}

/**
 * Links de Google Maps para recorrer la ruta en orden: punto de salida → paradas. Si la ruta ya
 * empezó y hay paradas resueltas, el primer tramo no fija el origen (Maps parte de la ubicación actual:
 * el punto de salida ya quedó atrás). Cada tramo siguiente arranca donde terminó el anterior.
 */
export function enlacesRutaGoogleMaps({
  origen,
  paradas,
  estado,
}: {
  origen: OrigenRuta | null;
  paradas: ParadaDelDia[];
  estado: EstadoRuta;
}): TramoGoogleMaps[] {
  const porRecorrer = paradasParaNavegar(paradas, estado);
  if (porRecorrer.length === 0) return [];

  const yaHayResueltas = paradas.some((p) => p.estado !== "pendiente");
  const origenInicial = yaHayResueltas ? null : comoPuntoOrigen(origen);

  const tramos: TramoGoogleMaps[] = [];
  for (let inicio = 0; inicio < porRecorrer.length; inicio += MAX_PARADAS_POR_TRAMO) {
    const grupo = porRecorrer.slice(inicio, inicio + MAX_PARADAS_POR_TRAMO);
    const puntos = grupo.map(comoPunto);
    const previa = inicio === 0 ? null : porRecorrer[inicio - 1];
    const desdePunto = previa ? comoPunto(previa) : origenInicial;

    tramos.push({
      url: armarUrl(puntos[puntos.length - 1], desdePunto, puntos.slice(0, -1)),
      desde: grupo[0].orden,
      hasta: grupo[grupo.length - 1].orden,
      saleDelOrigen: inicio === 0 && origenInicial !== null,
    });
  }
  return tramos;
}

/** "Cómo llegar" a una sola parada, desde donde esté la persona. */
export function enlaceParadaGoogleMaps(p: PuntoMaps): string {
  return armarUrl(comoPunto(p), null, []);
}
