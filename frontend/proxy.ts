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
  "/exportar",
  "/depositos",
  "/facturas",
];

export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;

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
    "/pedidos/:path*",
    "/hoy/:path*",
    "/mis-envios/:path*",
    "/clientes/:path*",
    "/usuarios/:path*",
    "/vehiculos/:path*",
    "/tarifas/:path*",
    "/rutas/:path*",
    "/exportar/:path*",
    "/depositos/:path*",
    "/facturas/:path*",
  ],
};
