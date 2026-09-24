using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ClienteRangoConfiguration : IEntityTypeConfiguration<ClienteRango>
{
    public void Configure(EntityTypeBuilder<ClienteRango> b)
    {
        b.ToTable("cliente_rangos", t =>
        {
            t.HasCheckConstraint("ck_cliente_rangos_origen", "origen in ('recalculo','ajuste')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.RangoAnterior).HasColumnName("rango_anterior");
        b.Property(x => x.RangoNuevo).HasColumnName("rango_nuevo");
        b.Property(x => x.Origen).HasColumnName("origen");
        b.Property(x => x.Trimestre).HasColumnName("trimestre");
        b.Property(x => x.Criterios).HasColumnName("criterios").HasColumnType("jsonb");
        b.Property(x => x.Motivo).HasColumnName("motivo");
        b.Property(x => x.RegistradoPor).HasColumnName("registrado_por");
        b.Property(x => x.RegistradoEn).HasColumnName("registrado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Rango>().WithMany().HasForeignKey(x => x.RangoAnterior).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Rango>().WithMany().HasForeignKey(x => x.RangoNuevo).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.RegistradoPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.RegistradoEn });
    }
}
