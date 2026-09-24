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

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/api/:path*",
    "/pedidos/:path*",
    "/hoy/:path*",
    "/mis-envios/:path*",
    "/clientes/:path*",
    "/usuarios/:path*",
    "/vehiculos/:path*",
    "/tarifas/:path*",
    "/rutas/:path*",
    "/jornada/:path*",
    "/exportar/:path*",
    "/depositos/:path*",
    "/facturas/:path*",
    "/cobranza/:path*",
    "/deliverys/:path*",
    "/liquidaciones/:path*",
    "/rentabilidad/:path*",
    "/tablero/:path*",
  ],
};
