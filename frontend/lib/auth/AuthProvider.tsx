"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import { leerError } from "@/lib/api/errores";
import type { Usuario } from "./types";

// Vacío = mismo origen: en producción el frontend reenvía /api/* al backend (proxy.ts),
// así la cookie de sesión es del mismo sitio que la página y no hay CORS.
const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

interface EstadoAuth {
  usuario: Usuario | null;
  cargando: boolean;
  login: (email: string, password: string) => Promise<Usuario>;
  logout: () => Promise<void>;
  /** fetch autenticado: agrega el access token y reintenta una vez tras refrescar si da 401 */
  fetchConSesion: (input: string, init?: RequestInit) => Promise<Response>;
}

const AuthContext = createContext<EstadoAuth | null>(null);

/** Id del usuario dentro del access token (claim `sub`, el mismo `id` que devuelve /api/auth/yo).
 * Solo para comparar identidades: la firma la valida el backend, acá no se confía en nada más. */
function idDelToken(token: string): string | null {
  try {
    const cuerpo = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const sub: unknown = JSON.parse(atob(cuerpo)).sub;
    return typeof sub === "string" ? sub : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [usuario, setUsuarioEstado] = useState<Usuario | null>(null);
  const [cargando, setCargando] = useState(true);
  // De quién es la identidad que esta pestaña tiene cargada, para notar cuando la sesión cambió.
  const usuarioIdRef = useRef<string | null>(null);
  const setUsuario = useCallback((u: Usuario | null) => {
    usuarioIdRef.current = u?.id ?? null;
    setUsuarioEstado(u);
  }, []);
  // El access token vive solo en memoria (nunca localStorage): un refresh de página lo pierde
  // a propósito y se recupera vía el refresh token en cookie httpOnly.
  const accessTokenRef = useRef<string | null>(null);
  // Un refresh ya en vuelo se comparte: sin esto, N pedidos en paralelo que reciben 401 disparan
  // N refreshes simultáneos contra el mismo refresh token (y el backend lo rota en cada uno).
  const refrescoEnCursoRef = useRef<Promise<string | null> | null>(null);

  const obtenerUsuario = useCallback(async (token: string): Promise<Usuario> => {
    const resp = await fetch(`${API_URL}/api/auth/yo`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!resp.ok) throw new Error("No se pudo obtener la identidad");
    return resp.json();
  }, []);

  const refrescar = useCallback(async (): Promise<string | null> => {
    if (refrescoEnCursoRef.current) return refrescoEnCursoRef.current;

    const promesa = (async () => {
      const resp = await fetch(`${API_URL}/api/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });
      if (!resp.ok) {
        // La sesión se cerró desde otra pestaña (o venció): esta deja de mostrarse como logueada.
        if (resp.status === 401 && usuarioIdRef.current) setUsuario(null);
        return null;
      }
      const { accessToken } = await resp.json();
      accessTokenRef.current = accessToken;
      // La sesión es una sola por navegador (la cookie del refresh token). Si en otra pestaña entró
      // otro usuario, el token que vuelve ya es de él: esta pestaña pasa a mostrar esa identidad en vez
      // de seguir dibujando las pantallas del anterior con un token que la API va a rechazar.
      const id = idDelToken(accessToken);
      if (usuarioIdRef.current && id && id !== usuarioIdRef.current) {
        try {
          setUsuario(await obtenerUsuario(accessToken));
        } catch {
          // Sin red: queda la identidad anterior; el próximo refresh lo vuelve a intentar.
        }
      }
      return accessToken as string;
    })();

    refrescoEnCursoRef.current = promesa;
    try {
      return await promesa;
    } finally {
      refrescoEnCursoRef.current = null;
    }
  }, [obtenerUsuario, setUsuario]);

  useEffect(() => {
    (async () => {
      try {
        const token = await refrescar();
        if (token) setUsuario(await obtenerUsuario(token));
      } catch {
        // Sin red, CORS o backend caído: se trata como "sin sesión" para no dejar la pantalla en blanco.
        accessTokenRef.current = null;
      }
      setCargando(false);
    })();
  }, [refrescar, obtenerUsuario, setUsuario]);

  const login = useCallback(
    async (email: string, password: string) => {
      const resp = await fetch(`${API_URL}/api/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ email, password }),
      });
      // 429 (rate limiting de /api/auth/login, Program.cs) trae su propio mensaje — cualquier
      // otro !ok se trata como credenciales inválidas, sin filtrar detalle (no hay por qué
      // distinguir "no existe el email" de "contraseña incorrecta" de cara al usuario).
      if (!resp.ok) {
        if (resp.status === 429) throw new Error((await leerError(resp)).mensaje);
        throw new Error("Email o contraseña incorrectos");
      }
      const { accessToken } = await resp.json();
      accessTokenRef.current = accessToken;
      const u = await obtenerUsuario(accessToken);
      setUsuario(u);
      return u;
    },
    [obtenerUsuario, setUsuario],
  );

  const logout = useCallback(async () => {
    await fetch(`${API_URL}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });
    accessTokenRef.current = null;
    setUsuario(null);
  }, [setUsuario]);

  const fetchConSesion = useCallback(
    async (input: string, init: RequestInit = {}) => {
      const conToken = (token: string | null) => ({
        ...init,
        headers: { ...init.headers, Authorization: `Bearer ${token ?? ""}` },
      });

      let resp = await fetch(`${API_URL}${input}`, conToken(accessTokenRef.current));
      if (resp.status === 401) {
        const nuevoToken = await refrescar();
        if (nuevoToken) resp = await fetch(`${API_URL}${input}`, conToken(nuevoToken));
      }
      return resp;
    },
    [refrescar],
  );

  return (
    <AuthContext.Provider value={{ usuario, cargando, login, logout, fetchConSesion }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): EstadoAuth {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth debe usarse dentro de <AuthProvider>");
  return ctx;
}
