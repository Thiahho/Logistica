using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// V1 (auditoria_codigo.md): el PUT del armado asignaba repartidor y vehículo sin validar nada. Mismo
// criterio que ReasignarRepartidor: el repartidor existe, tiene ese rol y está activo; el vehículo
// existe y está activo.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class ActualizarRutaValidacionTests(BaseDePrueba baseDatos)
{
    private static RutasController Rutas(LogisticaDbContext db, Guid usuarioId) =>
        new RutasController(db, null!, null!, null!, null!, null!, null!).ComoUsuario(usuarioId);

    private async Task<Vehiculo> VehiculoAsync(bool activo = true)
    {
        await using var db = baseDatos.CrearContexto();
        var vehiculo = new Vehiculo { Patente = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(), Activo = activo };
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync();
        return vehiculo;
    }

    private async Task<Usuario> RepartidorAsync(bool activo = true)
    {
        var usuario = await baseDatos.UsuarioAsync(Roles.Repartidor);
        if (!activo) await baseDatos.EjecutarAsync($"update usuarios set activo = false where id = '{usuario.Id}'");
        return usuario;
    }

    private async Task<IActionResult> ActualizarAsync(long rutaId, long? vehiculoId, Guid? repartidorId)
    {
        var actor = await baseDatos.UsuarioAsync();
        await using var db = baseDatos.CrearContexto();
        return await Rutas(db, actor.Id).Actualizar(
            rutaId, new RutasController.ActualizarRutaRequest(vehiculoId, repartidorId, 20, null), CancellationToken.None);
    }

    private async Task NoCambioAsync(long rutaId)
    {
        await using var db = baseDatos.CrearContexto();
        var ruta = await db.Rutas.SingleAsync(r => r.Id == rutaId);
        Assert.Null(ruta.RepartidorId);
        Assert.Null(ruta.VehiculoId);
    }

    [Fact]
    public async Task Un_repartidor_inexistente_se_rechaza()
    {
        var ruta = await baseDatos.RutaAsync();
        var vehiculo = await VehiculoAsync();

        var respuesta = await ActualizarAsync(ruta.Id, vehiculo.Id, Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_repartidor_se_rechaza()
    {
        var ruta = await baseDatos.RutaAsync();
        var vehiculo = await VehiculoAsync();
        var administrativo = await baseDatos.UsuarioAsync(Roles.Administracion);

        var respuesta = await ActualizarAsync(ruta.Id, vehiculo.Id, administrativo.Id);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }

    [Fact]
    public async Task Un_repartidor_inactivo_se_rechaza()
    {
        var ruta = await baseDatos.RutaAsync();
        var vehiculo = await VehiculoAsync();
        var repartidor = await RepartidorAsync(activo: false);

        var respuesta = await ActualizarAsync(ruta.Id, vehiculo.Id, repartidor.Id);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }

    [Fact]
    public async Task Un_vehiculo_inexistente_se_rechaza()
    {
        var ruta = await baseDatos.RutaAsync();
        var repartidor = await RepartidorAsync();

        var respuesta = await ActualizarAsync(ruta.Id, long.MaxValue, repartidor.Id);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }

    [Fact]
    public async Task Un_vehiculo_inactivo_se_rechaza()
    {
        var ruta = await baseDatos.RutaAsync();
        var vehiculo = await VehiculoAsync(activo: false);
        var repartidor = await RepartidorAsync();

        var respuesta = await ActualizarAsync(ruta.Id, vehiculo.Id, repartidor.Id);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }

    [Fact]
    public async Task Repartidor_y_vehiculo_activos_se_asignan()
    {
        var ruta = await baseDatos.RutaAsync();
        var vehiculo = await VehiculoAsync();
        var repartidor = await RepartidorAsync();

        var respuesta = await ActualizarAsync(ruta.Id, vehiculo.Id, repartidor.Id);

        Assert.IsType<NoContentResult>(respuesta);
        await using var db = baseDatos.CrearContexto();
        var guardada = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal(repartidor.Id, guardada.RepartidorId);
        Assert.Equal(vehiculo.Id, guardada.VehiculoId);
        Assert.Equal(20, guardada.CapacidadParadas);
    }

    [Fact]
    public async Task Sin_repartidor_ni_vehiculo_se_guarda_como_antes()
    {
        var ruta = await baseDatos.RutaAsync();

        var respuesta = await ActualizarAsync(ruta.Id, null, null);

        Assert.IsType<NoContentResult>(respuesta);
        await NoCambioAsync(ruta.Id);
    }
}
