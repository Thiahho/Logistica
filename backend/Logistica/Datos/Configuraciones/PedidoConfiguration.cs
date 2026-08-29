using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("pedidos", t =>
        {
            t.HasCheckConstraint("ck_pedidos_tipo", "tipo in ('entrega','retorno','reintento')");
            t.HasCheckConstraint("ck_pedidos_origen_carga",
                "origen_carga in ('interno','importado','portal','api')");
            t.HasCheckConstraint("ck_pedidos_bultos", "bultos > 0");
            t.HasCheckConstraint("ck_pedidos_origen_coherente",
                "tipo = 'entrega' or pedido_origen_id is not null");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.ReferenciaCliente).HasColumnName("referencia_cliente");
        b.Property(x => x.Tipo).HasColumnName("tipo").HasDefaultValue("entrega");
        b.Property(x => x.PedidoOrigenId).HasColumnName("pedido_origen_id");

        b.Property(x => x.OrigenUbicacionId).HasColumnName("origen_ubicacion_id");
        b.Property(x => x.DestinoUbicacionId).HasColumnName("destino_ubicacion_id");
        b.Property(x => x.DestinatarioNombre).HasColumnName("destinatario_nombre").IsRequired();
        b.Property(x => x.DestinatarioTelefono).HasColumnName("destinatario_telefono").IsRequired();
        b.Property(x => x.Bultos).HasColumnName("bultos").HasDefaultValue(1);
        b.Property(x => x.PesoKg).HasColumnName("peso_kg").HasColumnType("numeric(8,2)");
        b.Property(x => x.ValorDeclarado).HasColumnName("valor_declarado").HasColumnType("numeric(12,2)");
        b.Property(x => x.FechaEntrega).HasColumnName("fecha_entrega");
        b.Property(x => x.Urgente).HasColumnName("urgente").HasDefaultValue(false);

        b.Property(x => x.ZonaId).HasColumnName("zona_id");
        b.Property(x => x.PrecioBase).HasColumnName("precio_base").HasColumnType("numeric(12,2)");
        b.Property(x => x.RecargoUrgencia).HasColumnName("recargo_urgencia").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.DescuentoRuta).HasColumnName("descuento_ruta").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Peajes).HasColumnName("peajes").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(12,2)");
        b.Property(x => x.PrecioCongeladoEn).HasColumnName("precio_congelado_en").HasDefaultValueSql("now()");

        b.Property(x => x.Estado).HasColumnName("estado").HasColumnType("estado_pedido").HasDefaultValue(EstadoPedido.Borrador);
        b.Property(x => x.OrigenCarga).HasColumnName("origen_carga").HasDefaultValue("interno");
        b.Property(x => x.Observaciones).HasColumnName("observaciones");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PedidoOrigen).WithMany()
            .HasForeignKey(x => x.PedidoOrigenId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.OrigenUbicacion).WithMany()
            .HasForeignKey(x => x.OrigenUbicacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DestinoUbicacion).WithMany()
            .HasForeignKey(x => x.DestinoUbicacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Zona).WithMany()
            .HasForeignKey(x => x.ZonaId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.FechaEntrega, x.Estado });
        b.HasIndex(x => new { x.ClienteId, x.FechaEntrega });
        b.HasIndex(x => x.PedidoOrigenId);
    }
}
