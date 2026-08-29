using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class PedidoEventoConfiguration : IEntityTypeConfiguration<PedidoEvento>
{
    public void Configure(EntityTypeBuilder<PedidoEvento> b)
    {
        b.ToTable("pedido_eventos", t =>
        {
            t.HasCheckConstraint("ck_pedido_eventos_actor_tipo", "actor_tipo in ('sistema','usuario')");
            t.HasCheckConstraint("ck_pedido_eventos_actor_identificado",
                "actor_tipo = 'sistema' or actor_usuario_id is not null");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.EstadoAnterior).HasColumnName("estado_anterior").HasColumnType("estado_pedido");
        b.Property(x => x.EstadoNuevo).HasColumnName("estado_nuevo").HasColumnType("estado_pedido").IsRequired();
        b.Property(x => x.Motivo).HasColumnName("motivo");
        b.Property(x => x.ActorTipo).HasColumnName("actor_tipo").IsRequired();
        b.Property(x => x.ActorUsuarioId).HasColumnName("actor_usuario_id");
        b.Property(x => x.ActorTexto).HasColumnName("actor_texto");
        b.Property(x => x.OcurridoEn).HasColumnName("ocurrido_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Pedido).WithMany()
            .HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ActorUsuario).WithMany()
            .HasForeignKey(x => x.ActorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PedidoId, x.OcurridoEn });
    }
}
