import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

// El access token vive solo en memoria del browser (nunca en cookie), así que el proxy no puede
// leer el rol acá. Lo único que puede gatear a nivel de borde es si existe una sesión (la cookie
// httpOnly del refresh token); el gate fino por rol vive en <RequireRole> (cliente), después de
// resolver la identidad contra /api/auth/yo.
const RUTAS_PROTEGIDAS = [
  "/pedidos",
  "/hoy",
  "/mis-envios",
  "/clientes",
  "/usuarios",
  "/vehiculos",
  "/tarifas",
  "/rutas",
  "/jornada",
  "/exportar",
  "/depositos",
  "/facturas",
  // Faltaba desde el changelog 4.3: /cobranza está en el NAV del Shell (administracion) pero
  // nunca se agregó acá, así que hasta ahora no tenía gate de borde.
  "/cobranza",
  "/deliverys",
  "/liquidaciones",
  "/rentabilidad",
  "/tablero",
  // Faltaban desde sus changelogs (4.6 y 4.9); aparecieron al ampliar el matcher para la CSP.
  "/repartidores",
  "/recepcion",
];

// Producción (Vercel): BACKEND_URL=https://<servicio>.onrender.com y sin NEXT_PUBLIC_API_URL. El navegador
// habla solo con este dominio, que reenvía /api/* al backend — así la cookie de sesión es del mismo sitio
// que la página y no hay CORS. Se reenvía acá y no con rewrites() de next.config.ts porque hace falta
// agregar dos cabeceras: la IP real del usuario y el secreto que prueba que el request pasó por acá
// (backend Web/IpClienteDesdeProxy.cs; sin ellas, el límite de intentos de login veía la IP del proxy —
// auditoria_seguridad.md hallazgo 14). Sin BACKEND_URL (desarrollo) no hay reenvío: el navegador llama
// directo a NEXT_PUBLIC_API_URL.
function reenviarAlBackend(request: NextRequest, backend: string) {
  const { pathname, search } = request.nextUrl;
  const cabeceras = new Headers(request.headers);
  // Vercel pisa x-real-ip / x-forwarded-for con la IP del cliente, así que no se pueden falsificar desde
  // el navegador. set() y no append(): lo que mande el navegador en estas dos cabeceras se descarta.
  const ip = request.headers.get("x-real-ip") ?? request.headers.get("x-forwarded-for")?.split(",")[0]?.trim();
  cabeceras.delete("x-cliente-ip");
  if (ip) cabeceras.set("x-cliente-ip", ip);
  cabeceras.set("x-proxy-secreto", process.env.PROXY_SECRETO ?? "");
  return NextResponse.rewrite(new URL(`${backend}${pathname}${search}`), { request: { headers: cabeceras } });
}

/**
 * CSP estricta con nonce (auditoria_seguridad.md hallazgo 9; guía de Next en
 * node_modules/next/dist/docs/01-app/02-guides/content-security-policy.md). Un nonce nuevo por request:
 * solo corren los scripts que Next marca con él, más lo que esos cargan ('strict-dynamic'). Por eso las
 * páginas se renderizan por request (app/layout.tsx llama a connection()).
 *
 * style-src admite 'unsafe-inline' a propósito: Leaflet, recharts y algunos componentes ponen estilos en
 * línea en tiempo de ejecución, que no llevan nonce. Un estilo inyectado no ejecuta código.
 */
function politicaDeSeguridad(nonce: string): string {
  const desarrollo = process.env.NODE_ENV === "development";
  // En desarrollo sin BACKEND_URL el navegador llama directo a la API en otro puerto, y Next usa un
  // websocket para recargar en caliente.
  const api = process.env.NEXT_PUBLIC_API_URL ? new URL(process.env.NEXT_PUBLIC_API_URL).origin : "";
  const conexiones = ["'self'", api, desarrollo ? "ws: wss:" : ""].filter(Boolean).join(" ");
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${desarrollo ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    // Teselas del mapa (Mapa.tsx), vistas previas de fotos (blob:) y los data: que generan Leaflet y recharts.
    "img-src 'self' data: blob: https://*.tile.openstreetmap.org",
    "font-src 'self'",
    `connect-src ${conexiones}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    ...(desarrollo ? [] : ["upgrade-insecure-requests"]),
  ].join("; ");
}

export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (pathname.startsWith("/api/")) {
    const backend = process.env.BACKEND_URL?.replace(/\/+$/, "");
    return backend ? reenviarAlBackend(request, backend) : NextResponse.next();
  }

  if (RUTAS_PROTEGIDAS.some((ruta) => pathname.startsWith(ruta))) {
    const tieneSesion = request.cookies.has("refresh_token");
    if (!tieneSesion) {
      const login = new URL("/login", request.url);
      login.searchParams.set("volver", pathname);
      return NextResponse.redirect(login);
    }
  }

  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const csp = politicaDeSeguridad(nonce);
  // En el request para que Next lea el nonce al renderizar y lo aplique a sus scripts; en la respuesta
  // para que el navegador haga cumplir la política.
  const cabeceras = new Headers(request.headers);
  cabeceras.set("x-nonce", nonce);
  cabeceras.set("Content-Security-Policy", csp);
  const respuesta = NextResponse.next({ request: { headers: cabeceras } });
  respuesta.headers.set("Content-Security-Policy", csp);
  return respuesta;
}

export const config = {
  matcher: [
    // Todo menos los estáticos de Next y el favicon (no son documentos) y los prefetch de next/link.
    // /api/* entra: es el reenvío al backend.
    {
      source: "/((?!_next/static|_next/image|favicon.ico).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
