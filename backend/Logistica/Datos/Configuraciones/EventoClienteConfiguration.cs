using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class EventoClienteConfiguration : IEntityTypeConfiguration<EventoCliente>
{
    public void Configure(EntityTypeBuilder<EventoCliente> b)
    {
        b.ToTable("eventos_cliente");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.TipoId).HasColumnName("tipo_id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.ValorNum).HasColumnName("valor_num").HasColumnType("numeric(12,2)");
        b.Property(x => x.Nota).HasColumnName("nota");
        b.Property(x => x.OcurridoEn).HasColumnName("ocurrido_en").HasDefaultValueSql("now()");
        b.Property(x => x.RegistradoPorId).HasColumnName("registrado_por");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Tipo).WithMany()
            .HasForeignKey(x => x.TipoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pedido).WithMany()
            .HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RegistradoPor).WithMany()
            .HasForeignKey(x => x.RegistradoPorId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.OcurridoEn });
    }
}
