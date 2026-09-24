using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class LiquidacionConfiguration : IEntityTypeConfiguration<Liquidacion>
{
    public void Configure(EntityTypeBuilder<Liquidacion> b)
    {
        b.ToTable("liquidaciones", t =>
        {
            t.HasCheckConstraint("ck_liquidaciones_periodo", "hasta >= desde");
            t.HasCheckConstraint("ck_liquidaciones_rutas", "cantidad_rutas > 0");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RepartidorId).HasColumnName("repartidor_id");
        b.Property(x => x.Desde).HasColumnName("desde");
        b.Property(x => x.Hasta).HasColumnName("hasta");
        b.Property(x => x.CantidadRutas).HasColumnName("cantidad_rutas");
        b.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(12,2)");
        b.Property(x => x.Nota).HasColumnName("nota");
        b.Property(x => x.EmitidaPor).HasColumnName("emitida_por");
        b.Property(x => x.EmitidaEn).HasColumnName("emitida_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Repartidor).WithMany().HasForeignKey(x => x.RepartidorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.EmitidaPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.RepartidorId, x.Desde });
    }
}
