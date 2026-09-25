export type Rol = "administracion" | "operacion" | "repartidor" | "cliente";

/** Rol dentro de una empresa cliente: el dueño ve la cuenta y administra a su equipo; el usuario
 * (empleado) solo carga y sigue envíos. */
export type ClienteRol = "dueno" | "usuario";

export interface Usuario {
  id: string;
  nombre: string;
  rol: Rol;
  clienteId: string | null;
  /** Solo para rol "cliente"; null para el personal interno. */
  clienteRol: ClienteRol | null;
  /** Si la API le manda el precio de los envíos (acta changelog 4.30: los clientes del portal son
   * suscriptores; Portal:MostrarPrecios). El personal interno siempre lo ve. */
  verPrecios: boolean;
}

/** Si esta sesión ve el precio de los envíos. Lo decide el backend (/api/auth/yo); esto solo evita
 * mostrar tarjetas de precio vacías. Facturas, saldo y pagos no dependen de esto. */
export function vePrecios(usuario: Usuario | null): boolean {
  return usuario?.verPrecios === true;
}

/** Dueño de una empresa cliente. El backend es quien decide (policy "ClienteDueno"); esto solo
 * evita mostrar pantallas y montos que igual responderían 403 o vendrían vacíos. */
export function esClienteDueno(usuario: Usuario | null): boolean {
  return usuario?.rol === "cliente" && usuario.clienteRol === "dueno";
}

export function rutaPorRol(rol: Rol): string {
  switch (rol) {
    case "administracion":
    case "operacion":
      return "/pedidos";
    case "repartidor":
      return "/hoy";
    case "cliente":
      return "/mis-envios";
  }
}
