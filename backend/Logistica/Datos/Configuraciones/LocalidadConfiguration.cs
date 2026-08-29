using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class LocalidadConfiguration : IEntityTypeConfiguration<Localidad>
{
    public void Configure(EntityTypeBuilder<Localidad> b)
    {
        b.ToTable("localidades");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
        b.Property(x => x.Partido).HasColumnName("partido");
        b.Property(x => x.Cp).HasColumnName("cp");
        b.Property(x => x.ZonaId).HasColumnName("zona_id");

        b.HasOne(x => x.Zona).WithMany()
            .HasForeignKey(x => x.ZonaId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.Nombre, x.Partido }).IsUnique();
    }
}
