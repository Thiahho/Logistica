using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ObjetivoRentabilidadConfiguration : IEntityTypeConfiguration<ObjetivoRentabilidad>
{
    public void Configure(EntityTypeBuilder<ObjetivoRentabilidad> b)
    {
        b.ToTable("objetivos_rentabilidad", t =>
        {
            t.HasCheckConstraint("ck_objetivos_rentabilidad_pct", "pct_min >= 0 and pct_max <= 100 and pct_min <= pct_max");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Nombre).HasColumnName("nombre");
        b.Property(x => x.PctMin).HasColumnName("pct_min").HasColumnType("numeric(5,2)");
        b.Property(x => x.PctMax).HasColumnName("pct_max").HasColumnType("numeric(5,2)");
        b.Property(x => x.Fuentes).HasColumnName("fuentes");
        b.Property(x => x.Orden).HasColumnName("orden");
    }
}
