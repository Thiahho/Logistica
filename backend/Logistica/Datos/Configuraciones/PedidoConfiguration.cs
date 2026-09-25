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
            t.HasCheckConstraint("ck_pedidos_tipo", "tipo in ('entrega','retorno','reintento','delivery')");
            t.HasCheckConstraint("ck_pedidos_origen_carga",
                "origen_carga in ('interno','importado','portal','api')");
            t.HasCheckConstraint("ck_pedidos_bultos", "bultos > 0");
            t.HasCheckConstraint("ck_pedidos_origen_coherente",
                "tipo in ('entrega','delivery') or pedido_origen_id is not null");
            t.HasCheckConstraint("ck_pedidos_precio_manual", "precio_manual is null or precio_manual > 0");
            t.HasCheckConstraint("ck_pedidos_km_cobrados", "km_cobrados is null or km_cobrados >= 0");
            t.HasCheckConstraint("ck_pedidos_km_manual", "km_manual is null or km_manual >= 0");
            t.HasCheckConstraint("ck_pedidos_km_fuente", "km_fuente is null or km_fuente in ('ruta','recta','manual')");
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
        b.Property(x => x.DescuentoRango).HasColumnName("descuento_rango").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Peajes).HasColumnName("peajes").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(12,2)");
        b.Property(x => x.PrecioCongeladoEn).HasColumnName("precio_congelado_en").HasDefaultValueSql("now()");

        b.Property(x => x.PrecioManual).HasColumnName("precio_manual").HasColumnType("numeric(12,2)");
        b.Property(x => x.PrecioManualPor).HasColumnName("precio_manual_por");
        b.Property(x => x.PrecioManualEn).HasColumnName("precio_manual_en");

        b.Property(x => x.KmCobrados).HasColumnName("km_cobrados").HasColumnType("numeric(6,2)");
        b.Property(x => x.RecargoKm).HasColumnName("recargo_km").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.KmFuente).HasColumnName("km_fuente");
        b.Property(x => x.KmManual).HasColumnName("km_manual").HasColumnType("numeric(6,2)");
        b.Property(x => x.KmManualPor).HasColumnName("km_manual_por");
        b.Property(x => x.KmManualEn).HasColumnName("km_manual_en");

        b.Property(x => x.Estado).HasColumnName("estado").HasColumnType("estado_pedido").HasDefaultValue(EstadoPedido.Borrador);
        b.Property(x => x.OrigenCarga).HasColumnName("origen_carga").HasDefaultValue("interno");
        b.Property(x => x.Observaciones).HasColumnName("observaciones");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.Property(x => x.BultosDeclaradoCliente).HasColumnName("bultos_declarados_cliente");
        b.Property(x => x.RecepcionConfirmadaEn).HasColumnName("recepcion_confirmada_en");
        b.Property(x => x.RecepcionConfirmadaPor).HasColumnName("recepcion_confirmada_por");

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
        b.HasOne(x => x.KmManualPorUsuario).WithMany()
            .HasForeignKey(x => x.KmManualPor).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RecepcionConfirmadaPorUsuario).WithMany()
            .HasForeignKey(x => x.RecepcionConfirmadaPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.FechaEntrega, x.Estado });
        b.HasIndex(x => new { x.ClienteId, x.FechaEntrega });
        b.HasIndex(x => x.PedidoOrigenId);
        // Sin relación EF (puede ser un usuarios.id o un clientes_usuarios.id, B5 §1) — sin la
        // FK que antes generaba este índice por convención, hay que declararlo a mano.
        b.HasIndex(x => x.PrecioManualPor);

        b.Property(x => x.ViajeId).HasColumnName("viaje_id");
        b.Property(x => x.OrdenEnViaje).HasColumnName("orden_en_viaje");
        b.HasOne(x => x.Viaje).WithMany()
            .HasForeignKey(x => x.ViajeId).OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.CreadoPorClienteUsuarioId).HasColumnName("creado_por_cliente_usuario_id");
        b.HasOne(x => x.CreadoPorClienteUsuario).WithMany()
            .HasForeignKey(x => x.CreadoPorClienteUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
