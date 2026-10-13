using System.Reflection;
using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// Empresa dada de baja (Cliente.Activo = false): "se corta el servicio que se le proporciona hasta que
// pague". Sus logins siguen entrando y leyendo sus facturas y su deuda, y pueden informar un pago; todo
// lo que pide o cambia servicio responde 409.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class EmpresaInactivaEnPortalTests(BaseDePrueba baseDatos)
{
    private const string Clave = "ClaveDePrueba2026";

    // Lo único que no es lectura y sigue abierto con la empresa inactiva. Sumar una acción acá es una
    // decisión de negocio, no un detalle técnico.
    private static readonly string[] Permitidas =
    [
        nameof(MiCuentaController.InformarPago),
        nameof(MiCuentaController.CambiarActivoUsuario),
        nameof(MiCuentaController.CambiarPasswordUsuario),
    ];

    private static IEnumerable<MethodInfo> AccionesQueNoSonLectura() =>
        typeof(MiCuentaController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => !a.HttpMethods.Contains("GET")));

    /// <summary>Toda acción del portal que no es lectura ni está permitida: sale del controller real,
    /// así una acción nueva entra sola a la prueba.</summary>
    public static TheoryData<string> Cortadas()
    {
        var datos = new TheoryData<string>();
        foreach (var accion in AccionesQueNoSonLectura().Select(m => m.Name).Except(Permitidas).Order()) datos.Add(accion);
        return datos;
    }

    public static TheoryData<string> AbiertasQueNoSonLectura()
    {
        var datos = new TheoryData<string>();
        foreach (var accion in Permitidas) datos.Add(accion);
        return datos;
    }

    // El filtro solo necesita la sesión y la base: el resto del controller no se toca.
    private static MiCuentaController MiCuenta(LogisticaDbContext db, ClienteUsuario login) =>
        new MiCuentaController(
                db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
            .ComoPortal(login, db);

    private async Task<ClienteUsuario> LoginDeEmpresaAsync(bool activa, string? password = null)
    {
        var cliente = await baseDatos.ClienteAsync();
        if (!activa) await baseDatos.EjecutarAsync($"update clientes set activo = false where id = {cliente.Id}");
        return await baseDatos.ClienteUsuarioAsync(cliente.Id, password: password);
    }

    /// <summary>Pasa `accion` por los filtros del controller. Devuelve la respuesta y si la acción
    /// llegó a ejecutarse (acá un doble que responde 200: lo que se prueba es el corte, no la acción).</summary>
    private async Task<(IActionResult Respuesta, bool Ejecutada)> PedirAsync(ClienteUsuario login, string accion)
    {
        await using var db = baseDatos.CrearContexto();
        var ejecutada = false;
        var respuesta = await MiCuenta(db, login).PorLosFiltrosAsync(accion, () =>
        {
            ejecutada = true;
            return Task.FromResult<IActionResult>(new OkResult());
        });
        return (respuesta, ejecutada);
    }

    [Theory]
    [MemberData(nameof(Cortadas))]
    public async Task Con_la_empresa_inactiva_lo_que_pide_o_cambia_servicio_responde_409(string accion)
    {
        var login = await LoginDeEmpresaAsync(activa: false);

        var (respuesta, ejecutada) = await PedirAsync(login, accion);

        var conflicto = Assert.IsType<ConflictObjectResult>(respuesta);
        Assert.Equal(CortaServicioAEmpresaInactivaAttribute.Mensaje, conflicto.Value);
        Assert.False(ejecutada);
    }

    [Theory]
    [MemberData(nameof(AbiertasQueNoSonLectura))]
    public async Task Con_la_empresa_inactiva_informar_un_pago_y_cuidar_la_cuenta_siguen_abiertos(string accion)
    {
        var login = await LoginDeEmpresaAsync(activa: false);

        var (respuesta, ejecutada) = await PedirAsync(login, accion);

        Assert.IsType<OkResult>(respuesta);
        Assert.True(ejecutada);
    }

    [Theory]
    [MemberData(nameof(Cortadas))]
    public async Task Con_la_empresa_activa_no_se_corta_nada(string accion)
    {
        var login = await LoginDeEmpresaAsync(activa: true);

        var (respuesta, ejecutada) = await PedirAsync(login, accion);

        Assert.IsType<OkResult>(respuesta);
        Assert.True(ejecutada);
    }

    // La lista de arriba es la que se le muestra al usuario: si el controller abre otra acción a una
    // empresa inactiva, esta prueba obliga a pasar por acá.
    [Fact]
    public void Las_unicas_acciones_abiertas_que_no_son_lectura_son_las_acordadas()
    {
        var abiertas = AccionesQueNoSonLectura()
            .Where(m => m.GetCustomAttribute<PermitidoConEmpresaInactivaAttribute>() is not null)
            .Select(m => m.Name);

        Assert.Equal(Permitidas.Order(), abiertas.Order());
        Assert.NotEmpty(Cortadas());
    }

    // La acción real, no un doble: la empresa inactiva tiene que poder ver qué debe.
    [Fact]
    public async Task Con_la_empresa_inactiva_el_dueno_sigue_viendo_sus_facturas()
    {
        var login = await LoginDeEmpresaAsync(activa: false);

        await using var db = baseDatos.CrearContexto();
        var portal = MiCuenta(db, login);
        var respuesta = await portal.PorLosFiltrosAsync(
            nameof(MiCuentaController.Facturas), () => portal.Facturas(CancellationToken.None));

        Assert.IsType<OkObjectResult>(respuesta);
    }

    // Las acciones reales de lectura, con los servicios que usan: el resto del controller no se toca.
    private static MiCuentaController MiCuentaConCuenta(LogisticaDbContext db, ClienteUsuario login) =>
        new MiCuentaController(
                db, new CuentaCorrienteService(db), null!, null!, null!, null!, null!, null!, null!, null!, null!,
                new RangoClienteService(db), null!, null!, null!)
            .ComoPortal(login, db);

    // El portal tiene que saber que la empresa está dada de baja para decirlo: sin esto, a una empresa
    // inactiva sin deuda vencida la cuenta le seguiría diciendo que está al día.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task La_cuenta_del_dueno_dice_si_la_empresa_esta_activa(bool activa)
    {
        var login = await LoginDeEmpresaAsync(activa);

        await using var db = baseDatos.CrearContexto();
        var portal = MiCuentaConCuenta(db, login);
        var cuenta = Assert.IsType<MiCuentaController.CuentaPropia>(
            Assert.IsType<OkObjectResult>(await portal.Detalle(CancellationToken.None)).Value);
        var estadoDeCuenta = Assert.IsType<MiCuentaController.EstadoDeCuentaPropio>(
            Assert.IsType<OkObjectResult>(await portal.EstadoDeCuenta(null, null, CancellationToken.None)).Value);

        Assert.Equal(activa, cuenta.EmpresaActiva);
        Assert.Equal(activa, estadoDeCuenta.EmpresaActiva);
    }

    // El empleado no ve la cuenta corriente, pero sí sus envíos suspendidos: también a él hay que avisarle.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task El_empleado_tambien_sabe_si_el_servicio_de_su_empresa_esta_suspendido(bool activa)
    {
        var cliente = await baseDatos.ClienteAsync();
        if (!activa) await baseDatos.EjecutarAsync($"update clientes set activo = false where id = {cliente.Id}");
        var empleado = await baseDatos.ClienteUsuarioAsync(cliente.Id, rol: RolesCliente.Usuario);

        await using var db = baseDatos.CrearContexto();
        var servicio = Assert.IsType<MiCuentaController.ServicioPropio>(
            Assert.IsType<OkObjectResult>(await MiCuenta(db, empleado).Servicio(CancellationToken.None)).Value);

        Assert.Equal(activa, servicio.EmpresaActiva);
    }

    // La decisión es cortar el servicio, no la entrada: sin sesión no podría ver la deuda ni avisar el pago.
    [Fact]
    public async Task Con_la_empresa_inactiva_el_login_sigue_entrando_y_renovando_su_sesion()
    {
        var login = await LoginDeEmpresaAsync(activa: false, password: Clave);
        var tokens = new TokenService(Options.Create(new OpcionesJwt { Key = "clave-de-prueba-de-al-menos-32-bytes-0123456789" }));

        await using var db = baseDatos.CrearContexto();
        var sesion = await new AuthService(db, tokens).LoginAsync(login.Email, Clave, ip: null);

        Assert.NotNull(sesion);
        Assert.NotNull(await new AuthService(db, tokens).RefrescarAsync(sesion.RefreshToken, ip: null));
    }
}
