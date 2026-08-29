"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import type { Usuario } from "./types";

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

interface EstadoAuth {
  usuario: Usuario | null;
  cargando: boolean;
  login: (email: string, password: string) => Promise<Usuario>;
  logout: () => Promise<void>;
  /** fetch autenticado: agrega el access token y reintenta una vez tras refrescar si da 401 */
  fetchConSesion: (input: string, init?: RequestInit) => Promise<Response>;
}

const AuthContext = createContext<EstadoAuth | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [usuario, setUsuario] = useState<Usuario | null>(null);
  const [cargando, setCargando] = useState(true);
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
      if (!resp.ok) return null;
      const { accessToken } = await resp.json();
      accessTokenRef.current = accessToken;
      return accessToken as string;
    })();

    refrescoEnCursoRef.current = promesa;
    try {
      return await promesa;
    } finally {
      refrescoEnCursoRef.current = null;
    }
  }, []);

  useEffect(() => {
    (async () => {
      const token = await refrescar();
      if (token) {
        try {
          setUsuario(await obtenerUsuario(token));
        } catch {
          accessTokenRef.current = null;
        }
      }
      setCargando(false);
    })();
  }, [refrescar, obtenerUsuario]);

  const login = useCallback(
    async (email: string, password: string) => {
      const resp = await fetch(`${API_URL}/api/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ email, password }),
      });
      if (!resp.ok) throw new Error("Email o contraseña incorrectos");
      const { accessToken } = await resp.json();
      accessTokenRef.current = accessToken;
      const u = await obtenerUsuario(accessToken);
      setUsuario(u);
      return u;
    },
    [obtenerUsuario],
  );

  const logout = useCallback(async () => {
    await fetch(`${API_URL}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });
    accessTokenRef.current = null;
    setUsuario(null);
  }, []);

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
