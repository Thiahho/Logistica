using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Logistica.Tests.BaseDeDatos;

/// <summary>
/// Un Postgres real en un contenedor, con las migraciones del proyecto aplicadas desde cero. Las reglas
/// de construccion_v1.md §3 regla 3 viven en triggers: sin una base no hay forma de probarlas.
///
/// Un solo contenedor para toda la colección. Las pruebas no limpian: cada una crea sus propias filas
/// (varias tablas son de solo inserción y no se pueden borrar), así que ninguna depende de otra.
/// Necesita Docker; para correr solo las pruebas sin base: dotnet test --filter "Categoria!=BaseDeDatos".
/// </summary>
public sealed class BaseDePrueba : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _fuente = null!;
    private int _zonaId;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        // Fuente aparte para migrar: Npgsql lee el catálogo de tipos al conectar, y el enum
        // estado_pedido recién existe después de la primera migración (changelog 1.44).
        await using (var paraMigrar = CrearFuente())
        await using (var db = new LogisticaDbContext(Opciones(paraMigrar)))
            await db.Database.MigrateAsync();

        _fuente = CrearFuente();

        // zonas.codigo es de un carácter y único: una sola zona para todas las pruebas.
        await using var contexto = CrearContexto();
        var zona = new Zona { Codigo = "Z", Nombre = "Zona de prueba" };
        contexto.Zonas.Add(zona);
        // Un depósito: el origen por defecto de todo pedido que da de alta la aplicación.
        contexto.Ubicaciones.Add(new Ubicacion
        {
            CalleNumero = "Depósito 1", NombreDeposito = "Depósito de prueba", Verificada = true,
            Lat = -34.6037m, Lng = -58.3816m,
        });
        await contexto.SaveChangesAsync();
        _zonaId = zona.Id;
    }

    public async Task<Localidad> LocalidadAsync(bool conZona = true, string? nombre = null, string? partido = null)
    {
        await using var db = CrearContexto();
        var localidad = new Localidad
        {
            Nombre = nombre ?? $"Localidad {Unico()}", Partido = partido, ZonaId = conZona ? _zonaId : null,
        };
        db.Localidades.Add(localidad);
        await db.SaveChangesAsync();
        return localidad;
    }

    public async Task DisposeAsync()
    {
        if (_fuente is not null) await _fuente.DisposeAsync();
        await _contenedor.DisposeAsync();
    }

    public LogisticaDbContext CrearContexto() => new(Opciones(_fuente));

    private NpgsqlDataSource CrearFuente()
    {
        var builder = new NpgsqlDataSourceBuilder(_contenedor.GetConnectionString());
        builder.MapEnum<EstadoPedido>("estado_pedido");
        return builder.Build();
    }

    private static DbContextOptions<LogisticaDbContext> Opciones(NpgsqlDataSource fuente) =>
        new DbContextOptionsBuilder<LogisticaDbContext>().UseNpgsql(fuente).Options;

    // ---- SQL directo: lo que haría un fix a mano en la base, sin pasar por la aplicación ----

    public async Task<int> EjecutarAsync(string sql)
    {
        await using var comando = _fuente.CreateCommand(sql);
        return await comando.ExecuteNonQueryAsync();
    }

    public async Task<T> EscalarAsync<T>(string sql)
    {
        await using var comando = _fuente.CreateCommand(sql);
        return (T)(await comando.ExecuteScalarAsync())!;
    }

    /// <summary>La sentencia tiene que ser rechazada por un trigger (RAISE EXCEPTION, SqlState P0001)
    /// cuyo mensaje contenga `fragmento`.</summary>
    public async Task RechazaPorTriggerAsync(string sql, string fragmento)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => EjecutarAsync(sql));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Contains(fragmento, error.MessageText);
    }

    // ---- Datos mínimos válidos ----

    private static string Unico() => Guid.NewGuid().ToString("N")[..12];

    public async Task<Usuario> UsuarioAsync(string rol = Roles.Administracion)
    {
        await using var db = CrearContexto();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), Nombre = "Prueba", Email = $"{Unico()}@interno.test", PasswordHash = "x", Rol = rol,
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    public async Task<Cliente> ClienteAsync()
    {
        await using var db = CrearContexto();
        var cliente = new Cliente { RazonSocial = $"Cliente {Unico()}" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente;
    }

    public async Task<ClienteUsuario> ClienteUsuarioAsync(int clienteId)
    {
        await using var db = CrearContexto();
        var login = new ClienteUsuario
        {
            Id = Guid.NewGuid(), ClienteId = clienteId, Nombre = "Dueño", Email = $"{Unico()}@cliente.test", PasswordHash = "x",
        };
        db.ClientesUsuarios.Add(login);
        await db.SaveChangesAsync();
        return login;
    }

    /// <param name="conLocalidad">false deja la ubicación sin localidad.</param>
    /// <param name="conZona">false deja la localidad sin zona asignada.</param>
    public async Task<Ubicacion> UbicacionAsync(
        bool verificada = true, string? confianza = "alta", bool conLocalidad = true, bool conZona = true)
    {
        await using var db = CrearContexto();
        Localidad? localidad = null;
        if (conLocalidad)
        {
            localidad = new Localidad { Nombre = $"Localidad {Unico()}" };
            if (conZona) localidad.ZonaId = _zonaId;
        }
        var ubicacion = new Ubicacion
        {
            CalleNumero = $"Calle {Unico()} 100", Localidad = localidad, Verificada = verificada, GeoConfianza = confianza,
        };
        db.Ubicaciones.Add(ubicacion);
        await db.SaveChangesAsync();
        return ubicacion;
    }

    public async Task<Pedido> PedidoAsync(EstadoPedido estado = EstadoPedido.Borrador, long? destinoId = null)
    {
        var cliente = await ClienteAsync();
        var origen = await UbicacionAsync();
        destinoId ??= (await UbicacionAsync()).Id;

        await using var db = CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = cliente.Id,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destinoId.Value,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = new DateOnly(2026, 10, 5),
            Estado = estado,
        };
        if (estado != EstadoPedido.Borrador)
        {
            pedido.PrecioBase = 1000m;
            pedido.Total = 1000m;
        }
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    public async Task<Ruta> RutaAsync(Guid? repartidorId = null)
    {
        await using var db = CrearContexto();
        var ruta = new Ruta { Fecha = new DateOnly(2026, 10, 5), RepartidorId = repartidorId };
        db.Rutas.Add(ruta);
        await db.SaveChangesAsync();
        return ruta;
    }

    public async Task<RutaParada> ParadaAsync(long rutaId, long ubicacionId)
    {
        await using var db = CrearContexto();
        var parada = new RutaParada { RutaId = rutaId, UbicacionId = ubicacionId, Tipo = "entrega", Orden = 1 };
        db.RutaParadas.Add(parada);
        await db.SaveChangesAsync();
        return parada;
    }

    public async Task<Liquidacion> LiquidacionAsync(Guid repartidorId, Guid emitidaPor)
    {
        await using var db = CrearContexto();
        var liquidacion = new Liquidacion
        {
            RepartidorId = repartidorId,
            Desde = new DateOnly(2026, 10, 1),
            Hasta = new DateOnly(2026, 10, 15),
            CantidadRutas = 1,
            Total = 5000m,
            EmitidaPor = emitidaPor,
        };
        db.Liquidaciones.Add(liquidacion);
        await db.SaveChangesAsync();
        return liquidacion;
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionBaseDeDatos : ICollectionFixture<BaseDePrueba>
{
    public const string Nombre = "BaseDeDatos";
}
