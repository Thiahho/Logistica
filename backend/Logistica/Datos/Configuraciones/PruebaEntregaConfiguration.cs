using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class PruebaEntregaConfiguration : IEntityTypeConfiguration<PruebaEntrega>
{
    public void Configure(EntityTypeBuilder<PruebaEntrega> b)
    {
        b.ToTable("pruebas_entrega", t => t.HasCheckConstraint(
            "ck_pruebas_entrega_resultado", "resultado in ('entregado','fallido')"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.ParadaId).HasColumnName("parada_id");
        b.Property(x => x.Resultado).HasColumnName("resultado").IsRequired();
        b.Property(x => x.MotivoFallo).HasColumnName("motivo_fallo");
        b.Property(x => x.ReceptorNombre).HasColumnName("receptor_nombre");
        b.Property(x => x.IdentidadVerificada).HasColumnName("identidad_verificada").HasDefaultValue(false);
        b.Property(x => x.FotoPath).HasColumnName("foto_path");
        b.Property(x => x.Lat).HasColumnName("lat").HasColumnType("numeric(10,7)");
        b.Property(x => x.Lng).HasColumnName("lng").HasColumnType("numeric(10,7)");
        b.Property(x => x.DesvioMetros).HasColumnName("desvio_metros");
        b.Property(x => x.CapturadaEn).HasColumnName("capturada_en").IsRequired();
        b.Property(x => x.SincronizadaEn).HasColumnName("sincronizada_en").HasDefaultValueSql("now()");
        b.Property(x => x.DeviceUuid).HasColumnName("device_uuid").IsRequired();

        b.HasOne(x => x.Pedido).WithMany()
            .HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Parada).WithMany()
            .HasForeignKey(x => x.ParadaId).OnDelete(DeleteBehavior.Restrict);

        // RNF-02: idempotencia de la sincronización — el duplicado falla y se descarta
        b.HasIndex(x => new { x.PedidoId, x.DeviceUuid }).IsUnique();
        b.HasIndex(x => x.CapturadaEn);
    }
}
