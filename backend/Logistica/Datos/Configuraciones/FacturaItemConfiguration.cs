using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class FacturaItemConfiguration : IEntityTypeConfiguration<FacturaItem>
{
    public void Configure(EntityTypeBuilder<FacturaItem> b)
    {
        b.ToTable("factura_items", t =>
        {
            t.HasCheckConstraint("ck_factura_items_tipo", "tipo in ('pedido','ajuste','nota_credito')");
            t.HasCheckConstraint("ck_factura_items_estado", "estado in ('pendiente','aprobado','rechazado')");
            // Un ítem sin aprobar nunca entra a una factura.
            t.HasCheckConstraint("ck_factura_items_estado_factura", "estado = 'aprobado' or factura_id is null");
            // Aprobado exige monto.
            t.HasCheckConstraint("ck_factura_items_monto", "estado <> 'aprobado' or monto is not null");
            // tipo='pedido' nace siempre aprobado y siempre cuelga de un pedido.
            t.HasCheckConstraint("ck_factura_items_pedido",
                "tipo <> 'pedido' or (pedido_id is not null and estado = 'aprobado')");
            // Resolución: los dos campos o ninguno, y solo fuera de 'pendiente'.
            t.HasCheckConstraint("ck_factura_items_resolucion",
                "(estado = 'pendiente' and resuelto_por is null and resuelto_en is null) " +
                "or (estado <> 'pendiente' and (resuelto_por is null) = (resuelto_en is null))");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.FacturaId).HasColumnName("factura_id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
        b.Property(x => x.Descripcion).HasColumnName("descripcion").IsRequired();
        b.Property(x => x.Monto).HasColumnName("monto").HasColumnType("numeric(12,2)");
        b.Property(x => x.Estado).HasColumnName("estado").HasDefaultValue("aprobado");
        b.Property(x => x.CreadoPor).HasColumnName("creado_por");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");
        b.Property(x => x.ResueltoPor).HasColumnName("resuelto_por");
        b.Property(x => x.ResueltoEn).HasColumnName("resuelto_en");

        b.HasOne(x => x.Pedido).WithMany()
            .HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreadoPorUsuario).WithMany()
            .HasForeignKey(x => x.CreadoPor).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ResueltoPorUsuario).WithMany()
            .HasForeignKey(x => x.ResueltoPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.FacturaId);
        b.HasIndex(x => x.PedidoId);
        // El barrido del cierre: todo lo aprobado y sin facturar todavía.
        b.HasIndex(x => new { x.Estado, x.FacturaId }).HasFilter("factura_id is null");
        // Un pedido se factura una sola vez, garantizado a nivel de base.
        b.HasIndex(x => x.PedidoId).IsUnique().HasFilter("tipo = 'pedido'").HasDatabaseName("ux_factura_items_pedido");
    }
}
