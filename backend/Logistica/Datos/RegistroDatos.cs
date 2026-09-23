using Logistica.Entidades;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Datos;

/// <summary>
/// Arma la cadena de conexión (con el tuning de pool de OpcionesBaseDatos), el
/// NpgsqlDataSource y el DbContext en un solo lugar. Antes vivía inline en Program.cs; con el
/// tuning de pool/reintentos que agrega esto, inline se volvía ilegible.
/// </summary>
public static class RegistroDatos
{
    public static IServiceCollection AddDatosLogistica(this IServiceCollection services, IConfiguration configuration)
    {
        // Registrado para quien lo necesite vía IOptions&lt;OpcionesBaseDatos&gt; más adelante
        // (ej. diagnóstico). Para el propio arranque de acá abajo se lee directo de
        // IConfiguration porque el DbContext se registra ANTES de que el ServiceProvider esté
        // armado — todavía no hay un IOptions resoluble.
        services.Configure<OpcionesBaseDatos>(configuration.GetSection("BaseDatos"));
        var opciones = configuration.GetSection("BaseDatos").Get<OpcionesBaseDatos>() ?? new OpcionesBaseDatos();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Postgres");

        // La cadena de conexión sigue siendo solo credenciales (host/db/usuario/contraseña,
        // + SSL Mode si el entorno lo necesita) — el tuning de pool vive acá, versionado, en vez
        // de repetirse a mano en cada ConnectionStrings:Postgres de cada entorno.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = opciones.MaxPoolSize,
            MinPoolSize = opciones.MinPoolSize,
            ConnectionIdleLifetime = opciones.ConnectionIdleLifetimeSegundos,
            Timeout = opciones.TimeoutSegundos,
            CommandTimeout = opciones.CommandTimeoutSegundos,
            KeepAlive = opciones.KeepaliveSegundos,
        };

        if (opciones.DetrasDePoolerExterno)
        {
            // El pooler externo (Supabase 6543, Neon "-pooler") ya hace el reset de sesión al
            // devolver la conexión, y no soporta prepared statements con nombre en modo
            // transacción.
            connectionStringBuilder.NoResetOnClose = true;
            connectionStringBuilder.MaxAutoPrepare = 0;
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        dataSourceBuilder.MapEnum<EstadoPedido>("estado_pedido");
        var dataSource = dataSourceBuilder.Build();

        // Singleton en DI, no capturado en una closure suelta como antes: así el host lo
        // dispone al cerrar (Build() abre el pool; alguien tiene que ser dueño de cerrarlo).
        services.AddSingleton(dataSource);

        services.AddDbContext<LogisticaDbContext>((sp, options) =>
        {
            var ds = sp.GetRequiredService<NpgsqlDataSource>();
            options.UseNpgsql(ds, o =>
            {
                // NpgsqlRetryingExecutionStrategy exige que toda transacción manual (BeginTransactionAsync
                // en EscrituraDominio/RutasController/CuentaCorrienteService) pase por
                // Database.CreateExecutionStrategy().ExecuteAsync(...) — si no, tira
                // InvalidOperationException. Ver esos 4 sitios.
                o.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                o.CommandTimeout(opciones.CommandTimeoutSegundos);
            });
        });

        return services;
    }
}
