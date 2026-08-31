using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Datos;

public class LogisticaDbContext(DbContextOptions<LogisticaDbContext> options) : DbContext(options)
{
    /// <summary>Mapea la función ubicacion_apta(bigint) de docs/schema_v3.sql. Se usa en
    /// proyecciones LINQ para marcar direcciones dudosas sin duplicar el criterio en C#
    /// (construccion_v1.md §3 regla 3).</summary>
    [DbFunction("ubicacion_apta", IsBuiltIn = false)]
    public static bool UbicacionApta(long ubicacionId) =>
        throw new NotSupportedException("Solo se puede usar dentro de una consulta LINQ a EF.");

    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<Localidad> Localidades => Set<Localidad>();
    public DbSet<Ubicacion> Ubicaciones => Set<Ubicacion>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Tarifa> Tarifas => Set<Tarifa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ClienteUsuario> ClientesUsuarios => Set<ClienteUsuario>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoEvento> PedidoEventos => Set<PedidoEvento>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<RutaParada> RutaParadas => Set<RutaParada>();
    public DbSet<ParadaPedido> ParadaPedidos => Set<ParadaPedido>();
    public DbSet<PruebaEntrega> PruebasEntrega => Set<PruebaEntrega>();
    public DbSet<TipoEventoCliente> TiposEventoCliente => Set<TipoEventoCliente>();
    public DbSet<EventoCliente> EventosCliente => Set<EventoCliente>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<EstadoPedido>(schema: null, name: "estado_pedido");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogisticaDbContext).Assembly);

        // v_paradas_repartidor: proyección sin importes para la PWA (RNF-08, construccion_v1.md §3
        // regla 4). Se registra como entidad sin llave para poder consultarla con EF; la vista en
        // sí se crea en la migración ReglasDeBaseDeDatos.
        modelBuilder.Entity<ParadaRepartidor>(b =>
        {
            b.HasNoKey();
            b.ToView("v_paradas_repartidor");
            b.Property(x => x.ParadaId).HasColumnName("parada_id");
            b.Property(x => x.RutaId).HasColumnName("ruta_id");
            b.Property(x => x.Orden).HasColumnName("orden");
            b.Property(x => x.Tipo).HasColumnName("tipo");
            b.Property(x => x.Estado).HasColumnName("estado");
            b.Property(x => x.LlegadaEn).HasColumnName("llegada_en");
            b.Property(x => x.SalidaEn).HasColumnName("salida_en");
            b.Property(x => x.PedidoId).HasColumnName("pedido_id");
            b.Property(x => x.DestinatarioNombre).HasColumnName("destinatario_nombre");
            b.Property(x => x.DestinatarioTelefono).HasColumnName("destinatario_telefono");
            b.Property(x => x.Bultos).HasColumnName("bultos");
            b.Property(x => x.Observaciones).HasColumnName("observaciones");
            b.Property(x => x.CalleNumero).HasColumnName("calle_numero");
            b.Property(x => x.Referencia).HasColumnName("referencia");
            b.Property(x => x.Lat).HasColumnName("lat");
            b.Property(x => x.Lng).HasColumnName("lng");
            b.Property(x => x.Localidad).HasColumnName("localidad");
        });
    }
}

/// <summary>Proyección de solo lectura de v_paradas_repartidor. Nunca tiene importes.</summary>
public class ParadaRepartidor
{
    public long ParadaId { get; set; }
    public long RutaId { get; set; }
    public int Orden { get; set; }
    public string Tipo { get; set; } = null!;
    public string Estado { get; set; } = null!;
    public DateTimeOffset? LlegadaEn { get; set; }
    public DateTimeOffset? SalidaEn { get; set; }
    public long PedidoId { get; set; }
    public string DestinatarioNombre { get; set; } = null!;
    public string DestinatarioTelefono { get; set; } = null!;
    public int Bultos { get; set; }
    public string? Observaciones { get; set; }
    public string CalleNumero { get; set; } = null!;
    public string? Referencia { get; set; }
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }
    public string? Localidad { get; set; }
}
