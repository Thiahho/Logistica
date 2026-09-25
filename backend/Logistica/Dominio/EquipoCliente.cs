using Logistica.Entidades;

namespace Logistica.Dominio;

/// <summary>
/// Reglas del portal con dueño + empleados: qué puede hacer el dueño con los logins de su empresa
/// y cómo se arma el resumen de trabajo por empleado. Sin base de datos, para testearlo solo.
/// </summary>
public static class EquipoCliente
{
    /// <summary>El login que se intenta modificar, tal como lo ve el dueño.</summary>
    public record LoginObjetivo(Guid Id, int ClienteId, string Rol);

    /// <summary>null si el dueño `actorId` (de la empresa `clienteId`) puede activar, desactivar o
    /// cambiarle la contraseña a `objetivo`; si no, el motivo. El dueño administra solo a los
    /// empleados de su propia empresa: nunca a sí mismo ni a otro dueño (eso queda para
    /// Administración, desde el BackOffice).</summary>
    public static string? ValidarGestion(Guid actorId, int clienteId, LoginObjetivo objetivo)
    {
        if (objetivo.ClienteId != clienteId) return "El usuario no existe.";
        if (objetivo.Id == actorId) return "No podés modificar tu propio usuario desde acá.";
        if (objetivo.Rol != RolesCliente.Usuario) return "Solo podés administrar a los usuarios empleados.";
        return null;
    }

    /// <summary>Un pedido reducido a lo que importa para el resumen.</summary>
    public record PedidoCargado(Guid CargadoPor, EstadoPedido Estado);

    public record FilaResumen(
        Guid UsuarioId, string Nombre, string Rol, bool Activo,
        int Cargados, int Entregados, int Fallidos, int Cancelados, int Pendientes);

    public record Login(Guid Id, string Nombre, string Rol, bool Activo);

    /// <summary>Una fila por login (aunque no haya cargado nada en el período, para que el dueño vea
    /// también a quien no trabajó). Los estados se agrupan con NegocioCliente.Grupo, igual que en
    /// "Mi negocio".</summary>
    public static List<FilaResumen> Resumir(IEnumerable<Login> logins, IEnumerable<PedidoCargado> pedidos)
    {
        var porUsuario = pedidos.ToLookup(p => p.CargadoPor);
        return logins
            .Select(l =>
            {
                var c = NegocioCliente.Contar(porUsuario[l.Id].Select(p => p.Estado));
                return new FilaResumen(
                    l.Id, l.Nombre, l.Rol, l.Activo,
                    Cargados: c.Total, Entregados: c.Entregados, Fallidos: c.Fallidos,
                    Cancelados: c.Cancelados, Pendientes: c.EnCurso);
            })
            .OrderByDescending(f => f.Cargados)
            .ThenBy(f => f.Nombre)
            .ToList();
    }
}
