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
import { borrarTodosLosBorradores, hayCifrado, purgarBorradoresVencidos } from "@/lib/borradores/capturaDeParada";
import { esperarConLimite, fetchConLimite, limitePorDefecto, type OpcionesDePedido } from "./tiempos";
import type { Usuario } from "./types";

// Vacío = mismo origen: en producción el frontend reenvía /api/* al backend (proxy.ts),
// así la cookie de sesión es del mismo sitio que la página y no hay CORS.
const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

interface EstadoAuth {
  usuario: Usuario | null;
  cargando: boolean;
  login: (email: string, password: string) => Promise<Usuario>;
  logout: () => Promise<void>;
  /** fetch autenticado: agrega el access token y reintenta una vez tras refrescar si da 401.
   * Si el servidor no responde dentro del límite, rechaza con `ErrorDeTiempoAgotado` (tiempos.ts). */
  fetchConSesion: (input: string, init?: OpcionesDePedido) => Promise<Response>;
  /** Llave con que se cifra el borrador local de una entrega (lib/borradores/capturaDeParada.ts), o
   * null si ahora no se puede tener (sin sesión, sin señal, navegador sin WebCrypto): en ese caso no
   * hay borrador, y un llamado posterior lo vuelve a intentar. Nunca rechaza. */
  obtenerLlaveDeBorradores: () => Promise<CryptoKey | null>;
}

const AuthContext = createContext<EstadoAuth | null>(null);

// De quién son los borradores de entrega guardados en este teléfono (lib/borradores/capturaDeParada.ts).
// Solo el id del usuario, que no es secreto; la llave que los cifra no se guarda nunca. Va en
// localStorage para que sobreviva a una recarga: sin esto, si a uno se le vence la sesión, la pestaña
// se recarga y entra otro, nadie notaría que cambió la persona.
const CLAVE_DUENO_BORRADORES = "logistica-borradores-dueno";

function leerDuenoDeBorradores(): string | null {
  try {
    return localStorage.getItem(CLAVE_DUENO_BORRADORES);
  } catch {
    return null;
  }
}

function anotarDuenoDeBorradores(id: string) {
  try {
    localStorage.setItem(CLAVE_DUENO_BORRADORES, id);
  } catch {
    // Sin localStorage: queda solo la marca en memoria de esta pestaña.
  }
}

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
  // Respaldo en memoria de la marca de dueño de los borradores, por si localStorage no anda: no se
  // borra al vencer la sesión, para notar que quien entra después es otra persona.
  const duenoDeBorradoresRef = useRef<string | null>(null);
  // Pide repetir el borrado una vez que las pantallas del usuario anterior ya se desmontaron.
  const [borradosPendientes, setBorradosPendientes] = useState(0);
  // La llave de los borradores vive solo acá, en memoria y sin poder exportarse (nunca en localStorage
  // ni en IndexedDB): la da el servidor a la sesión, y con la sesión se va. Se guarda el pedido y no
  // solo el resultado, para que dos pantallas que la piden a la vez compartan un único viaje.
  const llaveDeBorradoresRef = useRef<{ usuarioId: string; pedido: Promise<CryptoKey | null> } | null>(null);
  const setUsuario = useCallback((u: Usuario | null) => {
    const id = u?.id ?? null;
    if (id !== usuarioIdRef.current) llaveDeBorradoresRef.current = null;
    // Se identificó a alguien (al entrar, al recuperar la sesión o por un cambio desde otra pestaña).
    // Si los borradores del teléfono no son suyos, no queda nada de lo que cargó el anterior. Si la
    // sesión solo venció y vuelve el mismo, se conservan (cifrados).
    // Sin marca (primer uso, almacenamiento borrado o que no se puede leer) no se sabe de quién son:
    // se borran igual. Es lo seguro, y lo que se pierde a cambio es poco: en un navegador sin
    // localStorage el borrador no sobrevive a una recarga, que es como andaba antes de existir.
    if (id && id !== usuarioIdRef.current) {
      const dueno = leerDuenoDeBorradores() ?? duenoDeBorradoresRef.current;
      if (dueno !== id) {
        void borrarTodosLosBorradores();
        setBorradosPendientes((n) => n + 1);
      }
      duenoDeBorradoresRef.current = id;
      anotarDuenoDeBorradores(id);
    }
    usuarioIdRef.current = id;
    setUsuarioEstado(u);
  }, []);

  // Segunda pasada del borrado por cambio de persona, ya con la pantalla del anterior desmontada: un
  // guardado suyo que estaba por salir pudo entrar después del primer borrado.
  useEffect(() => {
    if (borradosPendientes > 0) void borrarTodosLosBorradores();
  }, [borradosPendientes]);
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
    // Al arrancar, no recién al abrir una parada: lo vencido se va aunque nadie vuelva a esa pantalla.
    void purgarBorradoresVencidos();
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
    // Antes del pedido y sin esperarlo: quien cierra sesión no deja borradores en el teléfono aunque
    // no haya señal para avisarle al servidor.
    llaveDeBorradoresRef.current = null;
    void borrarTodosLosBorradores();
    // La marca de dueño no se toca: si el pedido de abajo falla la sesión sigue abierta, y sin marca
    // la próxima recarga borraría lo que este mismo usuario cargue desde ahora.
    await fetch(`${API_URL}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });
    accessTokenRef.current = null;
    setUsuario(null);
    // Otra vez: una pantalla todavía abierta pudo guardar algo mientras el pedido estaba en vuelo.
    void borrarTodosLosBorradores();
  }, [setUsuario]);

  const fetchConSesion = useCallback(
    async (input: string, { limiteMs, ...init }: OpcionesDePedido = {}) => {
      const limite = limiteMs ?? limitePorDefecto(init.body);
      const conToken = (token: string | null) => ({
        ...init,
        headers: { ...init.headers, Authorization: `Bearer ${token ?? ""}` },
      });

      // Cada intento arranca su propio reloj: el reintento tras refrescar no hereda uno ya vencido.
      let resp = await fetchConLimite(`${API_URL}${input}`, conToken(accessTokenRef.current), limite);
      if (resp.status === 401) {
        // El refresh se espera con límite pero no se aborta: es compartido entre pedidos, y cortarlo
        // a mitad de camino puede dejar al navegador sin la cookie que el backend ya rotó.
        const nuevoToken = await esperarConLimite(refrescar(), limite);
        if (nuevoToken) resp = await fetchConLimite(`${API_URL}${input}`, conToken(nuevoToken), limite);
      }
      return resp;
    },
    [refrescar],
  );

  const obtenerLlaveDeBorradores = useCallback(async (): Promise<CryptoKey | null> => {
    const usuarioId = usuarioIdRef.current;
    if (!usuarioId || !hayCifrado()) return null;

    let enCurso = llaveDeBorradoresRef.current;
    if (enCurso?.usuarioId !== usuarioId) {
      const pedido = (async () => {
        try {
          const resp = await fetchConSesion("/api/auth/clave-borradores", { cache: "no-store" });
          if (!resp.ok) return null;
          const { clave }: { clave: string } = await resp.json();
          const bytes = Uint8Array.from(atob(clave), (c) => c.charCodeAt(0));
          if (bytes.length !== 32) return null;
          // No extraíble: ni el propio código de la página puede volver a leer los bytes de la llave.
          const llave = await crypto.subtle.importKey("raw", bytes, "AES-GCM", false, ["encrypt", "decrypt"]);
          bytes.fill(0);
          return llave;
        } catch {
          // Sin señal, tiempo agotado o respuesta rara: no hay borrador por ahora.
          return null;
        }
      })();
      enCurso = { usuarioId, pedido };
      llaveDeBorradoresRef.current = enCurso;
    }

    const llave = await enCurso.pedido;
    // Un intento fallido no se recuerda: el próximo llamado vuelve a pedirla.
    if (!llave && llaveDeBorradoresRef.current === enCurso) llaveDeBorradoresRef.current = null;
    // Si mientras tanto cambió la identidad o se cerró la sesión, esa llave ya no es de quien está.
    return usuarioIdRef.current === usuarioId && llaveDeBorradoresRef.current === enCurso ? llave : null;
  }, [fetchConSesion]);

  return (
    <AuthContext.Provider value={{ usuario, cargando, login, logout, fetchConSesion, obtenerLlaveDeBorradores }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): EstadoAuth {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth debe usarse dentro de <AuthProvider>");
  return ctx;
}
