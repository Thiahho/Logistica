using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// Una contraseña nueva, un rol nuevo o una baja dejan vieja a toda sesión abierta del login: se cortan
// todas (cada dispositivo), no solo la del que hizo el cambio, y no recién cuando vence el refresh token.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class RevocacionDeSesionesTests(BaseDePrueba baseDatos)
{
    private const string Clave = "ClaveVieja2026";
    private const string ClaveNueva = "ClaveNueva2026";

    private static readonly TokenService Tokens =
        new(Options.Create(new OpcionesJwt { Key = "clave-de-prueba-de-al-menos-32-bytes-0123456789" }));

    private static AuthService Auth(LogisticaDbContext db) => new(db, Tokens);

    private static UsuariosController Usuarios(LogisticaDbContext db, Guid adminId) =>
        new UsuariosController(db, Auth(db)).ComoUsuario(adminId);

    private static ClientesController Clientes(LogisticaDbContext db, Guid adminId) =>
        new ClientesController(db, null!, null!, null!, null!, Auth(db), new RetencionPedidosService(db)).ComoUsuario(adminId);

    // El dueño administra a sus empleados: de MiCuentaController solo se usan la base y AuthService.
    private static MiCuentaController MiCuenta(LogisticaDbContext db, ClienteUsuario dueno) =>
        new MiCuentaController(
                db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, Auth(db))
            .ComoPortal(dueno, db);

    /// <summary>Inicia sesión como lo haría un dispositivo y devuelve su refresh token.</summary>
    private async Task<string> AbrirSesionAsync(string email, string clave = Clave)
    {
        await using var db = baseDatos.CrearContexto();
        var sesion = await Auth(db).LoginAsync(email, clave, ip: null);
        Assert.NotNull(sesion);
        return sesion.RefreshToken;
    }

    /// <summary>Si el dispositivo que guarda ese refresh token todavía puede renovar su sesión. Un
    /// refresh que sale bien rota el token: cada uno se consulta una sola vez.</summary>
    private async Task<bool> SigueAbiertaAsync(string refreshToken)
    {
        await using var db = baseDatos.CrearContexto();
        return await Auth(db).RefrescarAsync(refreshToken, ip: null) is not null;
    }

    // ---- Personal interno ----

    [Fact]
    public async Task Resetear_la_contrasena_de_un_usuario_cierra_todas_sus_sesiones_y_solo_las_suyas()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var otro = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var celular = await AbrirSesionAsync(usuario.Email);
        var tablet = await AbrirSesionAsync(usuario.Email);
        var ajena = await AbrirSesionAsync(otro.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).CambiarPassword(
                usuario.Id, new UsuariosController.CambiarPasswordRequest(ClaveNueva), CancellationToken.None));

        Assert.False(await SigueAbiertaAsync(celular));
        Assert.False(await SigueAbiertaAsync(tablet));
        Assert.True(await SigueAbiertaAsync(ajena));
        // Con la contraseña nueva entra de nuevo: lo que se cortó son las sesiones, no el usuario.
        Assert.True(await SigueAbiertaAsync(await AbrirSesionAsync(usuario.Email, ClaveNueva)));
    }

    [Fact]
    public async Task Cambiar_el_rol_de_un_usuario_cierra_sus_sesiones()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Administracion, Clave);
        var sesion = await AbrirSesionAsync(usuario.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).Actualizar(
                usuario.Id, new UsuariosController.ActualizarUsuarioRequest(usuario.Nombre, Roles.Operacion, true),
                CancellationToken.None));

        Assert.False(await SigueAbiertaAsync(sesion));
    }

    [Fact]
    public async Task Editar_el_nombre_de_un_usuario_no_cierra_sus_sesiones()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Operacion, Clave);
        var sesion = await AbrirSesionAsync(usuario.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).Actualizar(
                usuario.Id, new UsuariosController.ActualizarUsuarioRequest("Otro nombre", usuario.Rol, true),
                CancellationToken.None));

        Assert.True(await SigueAbiertaAsync(sesion));
    }

    // Con el usuario inactivo el refresh ya se rechazaba; lo que faltaba es que la sesión vieja no
    // reviva cuando lo reactivan. Por las dos puertas que dan de baja: el alta/baja rápida y la edición.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task La_sesion_de_un_usuario_dado_de_baja_no_revive_al_reactivarlo(bool porLaEdicion)
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var sesion = await AbrirSesionAsync(usuario.Email);

        foreach (var activo in new[] { false, true })
        {
            await using var db = baseDatos.CrearContexto();
            var respuesta = porLaEdicion
                ? await Usuarios(db, admin.Id).Actualizar(
                    usuario.Id, new UsuariosController.ActualizarUsuarioRequest(usuario.Nombre, usuario.Rol, activo),
                    CancellationToken.None)
                : await Usuarios(db, admin.Id).CambiarActivo(
                    usuario.Id, new UsuariosController.ActivoRequest(activo), CancellationToken.None);
            Assert.IsType<NoContentResult>(respuesta);
        }

        Assert.False(await SigueAbiertaAsync(sesion));
    }

    // ---- Logins del portal, desde el BackOffice ----

    [Fact]
    public async Task Resetear_desde_el_backoffice_la_contrasena_de_un_login_del_portal_cierra_sus_sesiones()
    {
        var admin = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var socio = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var celular = await AbrirSesionAsync(login.Email);
        var oficina = await AbrirSesionAsync(login.Email);
        var ajena = await AbrirSesionAsync(socio.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Clientes(db, admin.Id).CambiarPasswordUsuario(
                cliente.Id, login.Id, new ClientesController.CambiarPasswordClienteUsuarioRequest(ClaveNueva),
                CancellationToken.None));

        Assert.False(await SigueAbiertaAsync(celular));
        Assert.False(await SigueAbiertaAsync(oficina));
        Assert.True(await SigueAbiertaAsync(ajena));
    }

    [Fact]
    public async Task La_sesion_de_un_login_del_portal_dado_de_baja_desde_el_backoffice_no_revive_al_reactivarlo()
    {
        var admin = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var sesion = await AbrirSesionAsync(login.Email);

        foreach (var activo in new[] { false, true })
        {
            await using var db = baseDatos.CrearContexto();
            Assert.IsType<NoContentResult>(await Clientes(db, admin.Id).CambiarActivoUsuario(
                cliente.Id, login.Id, new ClientesController.ActivoRequest(activo), CancellationToken.None));
        }

        Assert.False(await SigueAbiertaAsync(sesion));
    }

    // ---- Empleados del portal, administrados por el dueño ----

    [Fact]
    public async Task El_dueno_que_resetea_la_contrasena_de_un_empleado_cierra_las_sesiones_del_empleado_y_no_la_suya()
    {
        var cliente = await baseDatos.ClienteAsync();
        var dueno = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var empleado = await baseDatos.ClienteUsuarioAsync(cliente.Id, RolesCliente.Usuario, Clave);
        var delEmpleado = await AbrirSesionAsync(empleado.Email);
        var delDueno = await AbrirSesionAsync(dueno.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await MiCuenta(db, dueno).CambiarPasswordUsuario(
                empleado.Id, new MiCuentaController.PasswordEquipoRequest(ClaveNueva), CancellationToken.None));

        Assert.False(await SigueAbiertaAsync(delEmpleado));
        Assert.True(await SigueAbiertaAsync(delDueno));
    }

    [Fact]
    public async Task La_sesion_de_un_empleado_que_el_dueno_dio_de_baja_no_revive_al_reactivarlo()
    {
        var cliente = await baseDatos.ClienteAsync();
        var dueno = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var empleado = await baseDatos.ClienteUsuarioAsync(cliente.Id, RolesCliente.Usuario, Clave);
        var sesion = await AbrirSesionAsync(empleado.Email);

        foreach (var activo in new[] { false, true })
        {
            await using var db = baseDatos.CrearContexto();
            Assert.IsType<NoContentResult>(await MiCuenta(db, dueno).CambiarActivoUsuario(
                empleado.Id, new MiCuentaController.ActivoEquipoRequest(activo), CancellationToken.None));
        }

        Assert.False(await SigueAbiertaAsync(sesion));
    }
}
