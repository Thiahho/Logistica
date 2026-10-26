using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Logistica.Tests.BaseDeDatos;

// M-N3: una baja, una contraseña nueva o un rol nuevo cortan también el access token ya emitido, en
// el próximo request, no cuando vence (Jwt:AccessMinutos). RevocacionDeSesionesTests cubre el lado
// del refresh token; acá se valida el access token como lo hace el pipeline (OnTokenValidated).
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class RevocacionInstantaneaTests(BaseDePrueba baseDatos)
{
    private const string Clave = "ClaveVieja2026";
    private const string ClaveNueva = "ClaveNueva2026";

    private static readonly OpcionesJwt Opciones = new() { Key = "clave-de-prueba-de-al-menos-32-bytes-0123456789" };
    private static readonly TokenService Tokens = new(Options.Create(Opciones));

    private static AuthService Auth(LogisticaDbContext db) => new(db, Tokens);

    private static UsuariosController Usuarios(LogisticaDbContext db, Guid adminId) =>
        new UsuariosController(db, Auth(db)).ComoUsuario(adminId);

    private static ClientesController Clientes(LogisticaDbContext db, Guid adminId) =>
        new ClientesController(db, null!, null!, null!, null!, Auth(db), new RetencionPedidosService(db)).ComoUsuario(adminId);

    private static MiCuentaController MiCuenta(LogisticaDbContext db, ClienteUsuario dueno) =>
        new MiCuentaController(
                db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, Auth(db))
            .ComoPortal(dueno, db);

    private async Task<string> AccessTokenAsync(string email, string clave = Clave)
    {
        await using var db = baseDatos.CrearContexto();
        var sesion = await Auth(db).LoginAsync(email, clave, ip: null);
        Assert.NotNull(sesion);
        return sesion.AccessToken;
    }

    /// <summary>Lo que hace el JwtBearer de Program.cs con el token: firma, emisor, audiencia y vida
    /// (mismo handler, sin remapear claims) y después la validación contra la base.</summary>
    private async Task<bool> AceptaAsync(string accessToken)
    {
        var resultado = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(accessToken,
            new TokenValidationParameters
            {
                ValidIssuer = Opciones.Issuer,
                ValidAudience = Opciones.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Opciones.Key)),
            });
        Assert.True(resultado.IsValid, resultado.Exception?.Message);

        await using var db = baseDatos.CrearContexto();
        return await ValidacionDeSesion.EsVigenteAsync(db, new ClaimsPrincipal(resultado.ClaimsIdentity));
    }

    /// <summary>Un access token como los que se emitían antes de este cambio: sin el claim de emisión
    /// con milisegundos, solo el iat estándar.</summary>
    private static string TokenLegado(Usuario usuario) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: Opciones.Issuer,
            audience: Opciones.Audience,
            claims: [new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, usuario.Id.ToString()), new Claim(ClaimTypes.Role, usuario.Rol)],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Opciones.Key)), SecurityAlgorithms.HmacSha256)));

    [Fact]
    public async Task El_access_token_de_un_usuario_dado_de_baja_se_rechaza_en_el_proximo_request()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var otro = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var token = await AccessTokenAsync(usuario.Email);
        var ajeno = await AccessTokenAsync(otro.Email);
        Assert.True(await AceptaAsync(token));

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).CambiarActivo(
                usuario.Id, new UsuariosController.ActivoRequest(false), CancellationToken.None));

        Assert.False(await AceptaAsync(token));
        Assert.True(await AceptaAsync(ajeno));
    }

    [Fact]
    public async Task El_access_token_de_un_empleado_que_el_dueno_dio_de_baja_se_rechaza()
    {
        var cliente = await baseDatos.ClienteAsync();
        var dueno = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var empleado = await baseDatos.ClienteUsuarioAsync(cliente.Id, RolesCliente.Usuario, Clave);
        var delEmpleado = await AccessTokenAsync(empleado.Email);
        var delDueno = await AccessTokenAsync(dueno.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await MiCuenta(db, dueno).CambiarActivoUsuario(
                empleado.Id, new MiCuentaController.ActivoEquipoRequest(false), CancellationToken.None));

        Assert.False(await AceptaAsync(delEmpleado));
        Assert.True(await AceptaAsync(delDueno));
    }

    // Reactivarlo no revive el token viejo: el que vale es el del login nuevo.
    [Fact]
    public async Task El_access_token_de_un_login_del_portal_dado_de_baja_no_revive_al_reactivarlo()
    {
        var admin = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var token = await AccessTokenAsync(login.Email);

        foreach (var activo in new[] { false, true })
        {
            await using var db = baseDatos.CrearContexto();
            Assert.IsType<NoContentResult>(await Clientes(db, admin.Id).CambiarActivoUsuario(
                cliente.Id, login.Id, new ClientesController.ActivoRequest(activo), CancellationToken.None));
        }

        Assert.False(await AceptaAsync(token));
        Assert.True(await AceptaAsync(await AccessTokenAsync(login.Email)));
    }

    [Fact]
    public async Task Resetear_la_contrasena_rechaza_el_access_token_viejo_y_acepta_el_login_inmediato_con_la_nueva()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Operacion, Clave);
        var token = await AccessTokenAsync(usuario.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).CambiarPassword(
                usuario.Id, new UsuariosController.CambiarPasswordRequest(ClaveNueva), CancellationToken.None));
        // Sin esperar: el login nuevo cae casi siempre en el mismo segundo que la revocación.
        var nuevo = await AccessTokenAsync(usuario.Email, ClaveNueva);

        Assert.False(await AceptaAsync(token));
        Assert.True(await AceptaAsync(nuevo));
    }

    [Fact]
    public async Task El_dueno_que_resetea_la_contrasena_de_un_empleado_rechaza_su_access_token()
    {
        var cliente = await baseDatos.ClienteAsync();
        var dueno = await baseDatos.ClienteUsuarioAsync(cliente.Id, password: Clave);
        var empleado = await baseDatos.ClienteUsuarioAsync(cliente.Id, RolesCliente.Usuario, Clave);
        var token = await AccessTokenAsync(empleado.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await MiCuenta(db, dueno).CambiarPasswordUsuario(
                empleado.Id, new MiCuentaController.PasswordEquipoRequest(ClaveNueva), CancellationToken.None));

        Assert.False(await AceptaAsync(token));
        Assert.True(await AceptaAsync(await AccessTokenAsync(empleado.Email, ClaveNueva)));
    }

    [Fact]
    public async Task Cambiar_el_rol_rechaza_el_access_token_viejo()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Administracion, Clave);
        var token = await AccessTokenAsync(usuario.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).Actualizar(
                usuario.Id, new UsuariosController.ActualizarUsuarioRequest(usuario.Nombre, Roles.Operacion, true),
                CancellationToken.None));

        Assert.False(await AceptaAsync(token));
        Assert.True(await AceptaAsync(await AccessTokenAsync(usuario.Email)));
    }

    [Fact]
    public async Task Editar_el_nombre_no_rechaza_el_access_token()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Operacion, Clave);
        var token = await AccessTokenAsync(usuario.Email);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).Actualizar(
                usuario.Id, new UsuariosController.ActualizarUsuarioRequest("Otro nombre", usuario.Rol, true),
                CancellationToken.None));

        Assert.True(await AceptaAsync(token));
    }

    // Una baja hecha a mano en la base, sin pasar por RevocarSesionesAsync, también corta.
    [Fact]
    public async Task Un_login_inactivo_o_inexistente_se_rechaza_aunque_no_tenga_revocacion()
    {
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var token = await AccessTokenAsync(usuario.Email);
        await baseDatos.EjecutarAsync($"update usuarios set activo = false where id = '{usuario.Id}'");

        Assert.False(await AceptaAsync(token));
        Assert.False(await AceptaAsync(TokenLegado(new Usuario { Id = Guid.NewGuid(), Rol = Roles.Repartidor })));
    }

    // Los access tokens emitidos antes del deploy no traen el claim nuevo: siguen valiendo hasta que
    // vencen, salvo que el login haya sido revocado después.
    [Fact]
    public async Task Un_access_token_anterior_al_cambio_vale_si_el_login_no_fue_revocado()
    {
        var admin = await baseDatos.UsuarioAsync();
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);
        var revocado = await baseDatos.UsuarioAsync(Roles.Repartidor, Clave);

        await using (var db = baseDatos.CrearContexto())
            Assert.IsType<NoContentResult>(await Usuarios(db, admin.Id).CambiarPassword(
                revocado.Id, new UsuariosController.CambiarPasswordRequest(ClaveNueva), CancellationToken.None));

        Assert.True(await AceptaAsync(TokenLegado(usuario)));
        Assert.False(await AceptaAsync(TokenLegado(revocado)));
    }
}
