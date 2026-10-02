using Logistica.Entidades;

namespace Logistica.Tests.BaseDeDatos;

// Historial de rangos (acta RF-42), actividad del portal y unicidad de email entre las dos tablas de
// login: trg_cliente_rangos_inmutable, trg_actividad_portal_inmutable,
// trg_verificar_email_unico_usuarios y trg_verificar_email_unico_clientes_usuarios.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class TriggersSoloInsercionTests(BaseDePrueba baseDatos)
{
    // ---- trg_cliente_rangos_inmutable ----

    [Theory]
    [InlineData("update cliente_rangos set rango_nuevo = 'oro' where id = {0}")]
    [InlineData("delete from cliente_rangos where id = {0}")]
    public async Task El_historial_de_rangos_no_se_edita_ni_se_borra(string sql)
    {
        var administrador = await baseDatos.UsuarioAsync();
        var cliente = await baseDatos.ClienteAsync();
        await using var db = baseDatos.CrearContexto();
        var cambio = new ClienteRango
        {
            ClienteId = cliente.Id, RangoAnterior = "sin_rango", RangoNuevo = "bronce", Origen = "recalculo",
            Trimestre = "2026-T3", RegistradoPor = administrador.Id, RegistradoEn = DateTimeOffset.UtcNow,
        };
        db.ClienteRangos.Add(cambio);
        await db.SaveChangesAsync();

        await baseDatos.RechazaPorTriggerAsync(string.Format(sql, cambio.Id), "cliente_rangos es de solo inserción");
    }

    // ---- trg_actividad_portal_inmutable ----

    [Theory]
    [InlineData("update clientes_usuarios_actividad set accion = 'otra' where id = {0}")]
    [InlineData("delete from clientes_usuarios_actividad where id = {0}")]
    public async Task La_actividad_del_portal_no_se_edita_ni_se_borra(string sql)
    {
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id);
        await using var db = baseDatos.CrearContexto();
        var actividad = new ClienteUsuarioActividad
        {
            ClienteId = cliente.Id, ClienteUsuarioId = login.Id, Accion = "sesion.iniciada", OcurridoEn = DateTimeOffset.UtcNow,
        };
        db.ClientesUsuariosActividad.Add(actividad);
        await db.SaveChangesAsync();

        await baseDatos.RechazaPorTriggerAsync(
            string.Format(sql, actividad.Id), "clientes_usuarios_actividad es de solo inserción");
    }

    // ---- trg_verificar_email_unico_* ----

    [Fact]
    public async Task Un_usuario_interno_no_puede_usar_el_email_de_un_login_de_cliente()
    {
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id);

        await baseDatos.RechazaPorTriggerAsync(
            $"insert into usuarios (id, nombre, email, password_hash, rol) values (gen_random_uuid(), 'X', '{login.Email}', 'x', 'operacion')",
            "ya está en uso por un login de cliente");
    }

    [Fact]
    public async Task Un_usuario_interno_no_puede_cambiar_su_email_al_de_un_login_de_cliente()
    {
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id);
        var usuario = await baseDatos.UsuarioAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update usuarios set email = '{login.Email}' where id = '{usuario.Id}'",
            "ya está en uso por un login de cliente");
    }

    [Fact]
    public async Task Un_login_de_cliente_no_puede_usar_el_email_de_un_usuario_interno()
    {
        var cliente = await baseDatos.ClienteAsync();
        var usuario = await baseDatos.UsuarioAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"insert into clientes_usuarios (id, cliente_id, nombre, email, password_hash) values (gen_random_uuid(), {cliente.Id}, 'X', '{usuario.Email}', 'x')",
            "ya está en uso por un usuario interno");
    }

    [Fact]
    public async Task Un_login_de_cliente_no_puede_cambiar_su_email_al_de_un_usuario_interno()
    {
        var cliente = await baseDatos.ClienteAsync();
        var login = await baseDatos.ClienteUsuarioAsync(cliente.Id);
        var usuario = await baseDatos.UsuarioAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update clientes_usuarios set email = '{usuario.Email}' where id = '{login.Id}'",
            "ya está en uso por un usuario interno");
    }

    [Fact]
    public async Task Cambiar_otro_dato_de_un_usuario_no_dispara_la_verificacion_de_email()
    {
        var usuario = await baseDatos.UsuarioAsync();

        var filas = await baseDatos.EjecutarAsync($"update usuarios set nombre = 'Otro nombre' where id = '{usuario.Id}'");

        Assert.Equal(1, filas);
    }
}
