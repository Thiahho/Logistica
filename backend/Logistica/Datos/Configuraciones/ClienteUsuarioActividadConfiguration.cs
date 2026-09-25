using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ClienteUsuarioActividadConfiguration : IEntityTypeConfiguration<ClienteUsuarioActividad>
{
    public void Configure(EntityTypeBuilder<ClienteUsuarioActividad> b)
    {
        b.ToTable("clientes_usuarios_actividad");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id").IsRequired();
        b.Property(x => x.ClienteUsuarioId).HasColumnName("cliente_usuario_id").IsRequired();
        b.Property(x => x.Accion).HasColumnName("accion").IsRequired();
        b.Property(x => x.EntidadTipo).HasColumnName("entidad_tipo");
        b.Property(x => x.EntidadId).HasColumnName("entidad_id");
        b.Property(x => x.Detalle).HasColumnName("detalle");
        b.Property(x => x.OcurridoEn).HasColumnName("ocurrido_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ClienteUsuario).WithMany()
            .HasForeignKey(x => x.ClienteUsuarioId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.OcurridoEn }).IsDescending(false, true);
        b.HasIndex(x => x.ClienteUsuarioId);
    }
}
