using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ClienteDestinatarioConfiguration : IEntityTypeConfiguration<ClienteDestinatario>
{
    public void Configure(EntityTypeBuilder<ClienteDestinatario> b)
    {
        b.ToTable("clientes_destinatarios");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(x => x.ClienteId).HasColumnName("cliente_id").IsRequired();
        b.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
        b.Property(x => x.Telefono).HasColumnName("telefono").IsRequired();
        b.Property(x => x.DestinoUbicacionId).HasColumnName("destino_ubicacion_id");
        b.Property(x => x.Observaciones).HasColumnName("observaciones");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DestinoUbicacion).WithMany()
            .HasForeignKey(x => x.DestinoUbicacionId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ClienteId);
    }
}
