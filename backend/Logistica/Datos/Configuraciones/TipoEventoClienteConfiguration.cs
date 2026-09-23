using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class TipoEventoClienteConfiguration : IEntityTypeConfiguration<TipoEventoCliente>
{
    public void Configure(EntityTypeBuilder<TipoEventoCliente> b)
    {
        b.ToTable("tipos_evento_cliente", t => t.HasCheckConstraint(
            "ck_tipos_evento_cliente_dimension", "dimension in ('pago','trato','operacion')"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Codigo).HasColumnName("codigo").IsRequired();
        b.Property(x => x.Dimension).HasColumnName("dimension").IsRequired();
        b.Property(x => x.Descripcion).HasColumnName("descripcion").IsRequired();

        b.HasIndex(x => x.Codigo).IsUnique();
    }
}
