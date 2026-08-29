using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ParadaPedidoConfiguration : IEntityTypeConfiguration<ParadaPedido>
{
    public void Configure(EntityTypeBuilder<ParadaPedido> b)
    {
        b.ToTable("parada_pedidos");
        b.HasKey(x => new { x.ParadaId, x.PedidoId });
        b.Property(x => x.ParadaId).HasColumnName("parada_id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");

        b.HasOne(x => x.Parada).WithMany()
            .HasForeignKey(x => x.ParadaId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Pedido).WithMany()
            .HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
    }
}
