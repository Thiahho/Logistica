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

    /// <summary>E1: saldo total del cliente (facturado - pagado), incluye deuda todavía no
    /// vencida. Para el gate de corte (¿se admite un pedido nuevo?) no se usa esta función —
    /// se usa DeudaVencidaCliente, que solo cuenta lo vencido (acta §10.2-L3).</summary>
    [DbFunction("saldo_cliente", IsBuiltIn = false)]
    public static decimal SaldoCliente(int clienteId) =>
        throw new NotSupportedException("Solo se puede usar dentro de una consulta LINQ a EF.");

    /// <summary>E1: deuda con fecha de vencimiento ya pasada a `fecha` — la función real que
    /// gatea el corte de servicio (acta §10.2-L1/L3). Se apoya en v_facturas_saldo (FIFO), no
    /// reimplementa la imputación.</summary>
    [DbFunction("deuda_vencida_cliente", IsBuiltIn = false)]
    public static decimal DeudaVencidaCliente(int clienteId, DateOnly fecha) =>
        throw new NotSupportedException("Solo se puede usar dentro de una consulta LINQ a EF.");

    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<Localidad> Localidades => Set<Localidad>();
    public DbSet<Ubicacion> Ubicaciones => Set<Ubicacion>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Tarifa> Tarifas => Set<Tarifa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ClienteUsuario> ClientesUsuarios => Set<ClienteUsuario>();
    public DbSet<ClienteUsuarioActividad> ClientesUsuariosActividad => Set<ClienteUsuarioActividad>();
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
    public DbSet<Factura> Facturas => Set<Factura>();
    public DbSet<FacturaItem> FacturaItems => Set<FacturaItem>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<PagoInformado> PagosInformados => Set<PagoInformado>();
    public DbSet<Viaje> Viajes => Set<Viaje>();
    public DbSet<Novedad> Novedades => Set<Novedad>();
    public DbSet<ClienteDestinatario> ClientesDestinatarios => Set<ClienteDestinatario>();
    public DbSet<ParametroLiquidacion> ParametrosLiquidacion => Set<ParametroLiquidacion>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();
    public DbSet<Rango> Rangos => Set<Rango>();
    public DbSet<ClienteRango> ClienteRangos => Set<ClienteRango>();
    public DbSet<CostoFijo> CostosFijos => Set<CostoFijo>();
    public DbSet<ObjetivoRentabilidad> ObjetivosRentabilidad => Set<ObjetivoRentabilidad>();

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
            // Estado del pedido, para que la PWA pueda marcar uno cancelado por operación. Es un
            // estado, no un importe: la regla 3.4 (la vista no tiene precios) sigue intacta.
            b.Property(x => x.PedidoEstado).HasColumnName("pedido_estado").HasColumnType("estado_pedido");
        });

        // E1: saldo por factura derivado por FIFO (v_facturas_saldo, docs/schema_v3.sql). Igual
        // que v_paradas_repartidor: la vista es la única fuente de verdad del cálculo, esto solo
        // la expone a LINQ.
        modelBuilder.Entity<FacturaSaldo>(b =>
        {
            b.HasNoKey();
            b.ToView("v_facturas_saldo");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ClienteId).HasColumnName("cliente_id");
            b.Property(x => x.Ciclo).HasColumnName("ciclo");
            b.Property(x => x.PeriodoDesde).HasColumnName("periodo_desde");
            b.Property(x => x.PeriodoHasta).HasColumnName("periodo_hasta");
            b.Property(x => x.FechaEmision).HasColumnName("fecha_emision");
            b.Property(x => x.FechaVencimiento).HasColumnName("fecha_vencimiento");
            b.Property(x => x.Total).HasColumnName("total");
            b.Property(x => x.Saldo).HasColumnName("saldo");
            b.Property(x => x.Pagado).HasColumnName("pagado");
        });
    }
}

/// <summary>Proyección de solo lectura de v_facturas_saldo — saldo/pagado de cada factura
/// resuelto por FIFO (E1, acta §10.2-B/D12). Nunca se escribe a través de esta entidad.</summary>
public class FacturaSaldo
{
    public long Id { get; set; }
    public int ClienteId { get; set; }
    public string Ciclo { get; set; } = null!;
    public DateOnly PeriodoDesde { get; set; }
    public DateOnly PeriodoHasta { get; set; }
    public DateOnly FechaEmision { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public decimal Total { get; set; }
    public decimal Saldo { get; set; }
    public decimal Pagado { get; set; }
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
    public EstadoPedido PedidoEstado { get; set; }
}
