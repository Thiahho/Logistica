using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class CostoFijoConfiguration : IEntityTypeConfiguration<CostoFijo>
{
    public void Configure(EntityTypeBuilder<CostoFijo> b)
    {
        b.ToTable("costos_fijos", t =>
        {
            t.HasCheckConstraint("ck_costos_fijos_mes", "extract(day from mes) = 1");
            t.HasCheckConstraint("ck_costos_fijos_monto", "monto >= 0");
            t.HasCheckConstraint("ck_costos_fijos_categoria", "btrim(categoria) <> ''");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Mes).HasColumnName("mes");
        b.Property(x => x.Categoria).HasColumnName("categoria");
        b.Property(x => x.Descripcion).HasColumnName("descripcion");
        b.Property(x => x.Monto).HasColumnName("monto").HasColumnType("numeric(14,2)");
        b.Property(x => x.CreadoPor).HasColumnName("creado_por");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.CreadoPor).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Mes);
    }
}
