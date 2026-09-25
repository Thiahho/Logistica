using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ParametroLiquidacionConfiguration : IEntityTypeConfiguration<ParametroLiquidacion>
{
    public void Configure(EntityTypeBuilder<ParametroLiquidacion> b)
    {
        b.ToTable("parametros_liquidacion", t =>
        {
            t.HasCheckConstraint("ck_parametros_liquidacion_tipo_vehiculo", "tipo_vehiculo in ('camioneta','moto')");
            t.HasCheckConstraint("ck_parametros_liquidacion_montos", "pago_por_entrega >= 0 and bono_ruta >= 0");
            t.HasCheckConstraint("ck_parametros_liquidacion_pct", "pct_minimo_exitosas between 0 and 100");
            t.HasCheckConstraint("ck_parametros_liquidacion_vigencia", "vigente_hasta is null or vigente_hasta >= vigente_desde");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TipoVehiculo).HasColumnName("tipo_vehiculo");
        b.Property(x => x.PagoPorEntrega).HasColumnName("pago_por_entrega").HasColumnType("numeric(12,2)");
        b.Property(x => x.BonoRuta).HasColumnName("bono_ruta").HasColumnType("numeric(12,2)");
        b.Property(x => x.PctMinimoExitosas).HasColumnName("pct_minimo_exitosas").HasColumnType("numeric(5,2)");
        b.Property(x => x.MotivosImputables).HasColumnName("motivos_imputables").HasDefaultValueSql("'{}'::text[]");
        b.Property(x => x.VigenteDesde).HasColumnName("vigente_desde");
        b.Property(x => x.VigenteHasta).HasColumnName("vigente_hasta");
        b.Property(x => x.CreadoPor).HasColumnName("creado_por");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.CreadoPor).OnDelete(DeleteBehavior.Restrict);

        // Una sola fila vigente (abierta) por tipo de vehículo, y nunca dos que arranquen el mismo día.
        b.HasIndex(x => x.TipoVehiculo).IsUnique().HasFilter("vigente_hasta is null")
            .HasDatabaseName("ux_parametros_liquidacion_vigente");
        b.HasIndex(x => new { x.TipoVehiculo, x.VigenteDesde }).IsUnique();
    }
}
