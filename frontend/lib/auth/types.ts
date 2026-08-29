export type Rol = "administracion" | "operacion" | "repartidor" | "cliente";

export interface Usuario {
  id: string;
  nombre: string;
  rol: Rol;
  clienteId: string | null;
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
