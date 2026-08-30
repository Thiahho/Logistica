using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ZonaConfiguration : IEntityTypeConfiguration<Zona>
{
    public void Configure(EntityTypeBuilder<Zona> b)
    {
        b.ToTable("zonas", t =>
        {
            t.HasCheckConstraint("ck_zonas_km_desde", "km_desde is null or km_desde >= 0");
            t.HasCheckConstraint("ck_zonas_km_rango", "km_hasta is null or km_desde is null or km_hasta > km_desde");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Codigo).HasColumnName("codigo").HasMaxLength(1).IsRequired();
        b.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
        b.Property(x => x.Activa).HasColumnName("activa").HasDefaultValue(true);
        b.Property(x => x.KmDesde).HasColumnName("km_desde");
        b.Property(x => x.KmHasta).HasColumnName("km_hasta");

        b.HasIndex(x => x.Codigo).IsUnique();
    }
}
