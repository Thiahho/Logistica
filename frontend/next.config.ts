import type { NextConfig } from "next";

// Cabeceras de seguridad para todas las rutas del frontend. Sin CSP estricta a propósito: Next inyecta
// scripts en línea, el mapa carga tiles de OpenStreetMap y las fotos se previsualizan con blob: — una CSP
// bien hecha necesita nonces y es un cambio aparte; esto cubre lo que no tiene costo de compatibilidad.
const cabecerasDeSeguridad = [
  // El navegador no adivina tipos de contenido (evita ejecutar como script algo servido como texto/imagen).
  { key: "X-Content-Type-Options", value: "nosniff" },
  // La app no se embebe en frames de otros sitios (clickjacking sobre el login y las pantallas de cobro).
  { key: "X-Frame-Options", value: "DENY" },
  // Al salir hacia Maps/OSM no se filtra la URL interna (ids de pedido, de ruta) en el Referer.
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  // La PWA del repartidor usa GPS y cámara (foto de la entrega); el resto de sensores, apagados.
  { key: "Permissions-Policy", value: "geolocation=(self), camera=(self), microphone=(), payment=()" },
  // Solo en producción: por http los navegadores lo ignoran, y así no molesta en desarrollo local.
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" }]
    : []),
];

const nextConfig: NextConfig = {
  // No anunciar "X-Powered-By: Next.js".
  poweredByHeader: false,
  // Producción (Vercel): BACKEND_URL=https://<servicio>.onrender.com y sin NEXT_PUBLIC_API_URL. El navegador
  // habla solo con el dominio del frontend, que reenvía /api/* al backend. Sin BACKEND_URL (desarrollo) no
  // hay reenvío y se usa NEXT_PUBLIC_API_URL directo.
  async rewrites() {
    const backend = process.env.BACKEND_URL?.replace(/\/+$/, "");
    return backend ? [{ source: "/api/:path*", destination: `${backend}/api/:path*` }] : [];
  },
  async headers() {
    return [{ source: "/:path*", headers: cabecerasDeSeguridad }];
  },
};

export default nextConfig;
