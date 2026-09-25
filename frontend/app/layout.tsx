import type { Metadata, Viewport } from "next";
import { Geist_Mono, Inter, Montserrat } from "next/font/google";
import { connection } from "next/server";
import { AuthProvider } from "@/lib/auth/AuthProvider";
import { Shell } from "@/components/Shell";
import "./globals.css";

// Inter para todo el texto de la app (menús, botones, datos, estados); Montserrat solo para títulos
// grandes de marca (clase font-display).
const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

const montserrat = Montserrat({
  variable: "--font-montserrat",
  subsets: ["latin"],
  weight: ["600", "700"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "BF Transportes",
  description: "Logística y transporte",
};

export const viewport: Viewport = {
  themeColor: "#0057d9",
  viewportFit: "cover",
};

// CSP con nonce (proxy.ts, auditoria_seguridad.md hallazgo 9): Next aplica el nonce al renderizar, así que
// ninguna página puede quedar prerenderizada en el build — connection() fuerza el render por request.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  await connection();
  return (
    <html
      lang="es"
      className={`${inter.variable} ${montserrat.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <AuthProvider>
          <Shell>{children}</Shell>
        </AuthProvider>
      </body>
    </html>
  );
}
