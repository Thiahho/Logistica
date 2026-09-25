using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Tests;

// Portal con dueño + empleados: qué logins puede administrar el dueño y cómo se arma el resumen de
// envíos cargados por cada uno.
public class EquipoClienteTests
{
    private static readonly Guid Dueno = Guid.NewGuid();
    private const int Empresa = 7;

    [Fact]
    public void El_dueno_administra_a_un_empleado_de_su_empresa()
    {
        var empleado = new EquipoCliente.LoginObjetivo(Guid.NewGuid(), Empresa, RolesCliente.Usuario);
        Assert.Null(EquipoCliente.ValidarGestion(Dueno, Empresa, empleado));
    }

    [Fact]
    public void El_dueno_no_se_modifica_a_si_mismo()
    {
        var yo = new EquipoCliente.LoginObjetivo(Dueno, Empresa, RolesCliente.Dueno);
        Assert.NotNull(EquipoCliente.ValidarGestion(Dueno, Empresa, yo));
    }

    [Fact]
    public void El_dueno_no_toca_a_otro_dueno()
    {
        var otroDueno = new EquipoCliente.LoginObjetivo(Guid.NewGuid(), Empresa, RolesCliente.Dueno);
        Assert.NotNull(EquipoCliente.ValidarGestion(Dueno, Empresa, otroDueno));
    }

    [Fact]
    public void El_dueno_no_ve_empleados_de_otra_empresa()
    {
        var ajeno = new EquipoCliente.LoginObjetivo(Guid.NewGuid(), Empresa + 1, RolesCliente.Usuario);
        Assert.Equal("El usuario no existe.", EquipoCliente.ValidarGestion(Dueno, Empresa, ajeno));
    }

    [Fact]
    public void El_resumen_agrupa_por_quien_cargo_y_por_como_termino()
    {
        var ana = new EquipoCliente.Login(Guid.NewGuid(), "Ana", RolesCliente.Usuario, true);
        var beto = new EquipoCliente.Login(Guid.NewGuid(), "Beto", RolesCliente.Usuario, true);
        var pedidos = new[]
        {
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.Entregado),
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.Entregado),
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.Fallido),
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.Devuelto),
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.Cancelado),
            new EquipoCliente.PedidoCargado(ana.Id, EstadoPedido.EnRuta),
            new EquipoCliente.PedidoCargado(beto.Id, EstadoPedido.Borrador),
        };

        var filas = EquipoCliente.Resumir([beto, ana], pedidos);

        var filaAna = filas[0]; // más cargados primero
        Assert.Equal("Ana", filaAna.Nombre);
        Assert.Equal(6, filaAna.Cargados);
        Assert.Equal(2, filaAna.Entregados);
        Assert.Equal(2, filaAna.Fallidos);
        Assert.Equal(1, filaAna.Cancelados);
        Assert.Equal(1, filaAna.Pendientes);

        Assert.Equal(1, filas[1].Cargados);
        Assert.Equal(1, filas[1].Pendientes);
    }

    [Fact]
    public void El_resumen_incluye_a_quien_no_cargo_nada()
    {
        var quieto = new EquipoCliente.Login(Guid.NewGuid(), "Quieto", RolesCliente.Usuario, true);
        var filas = EquipoCliente.Resumir([quieto], []);

        var fila = Assert.Single(filas);
        Assert.Equal(0, fila.Cargados);
    }
}
