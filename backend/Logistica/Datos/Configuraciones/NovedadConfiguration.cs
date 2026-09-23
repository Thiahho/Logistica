using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class NovedadConfiguration : IEntityTypeConfiguration<Novedad>
{
    public void Configure(EntityTypeBuilder<Novedad> b)
    {
        b.ToTable("novedades", t =>
        {
            t.HasCheckConstraint("ck_novedades_tipo",
                "tipo in ('incidencia_ruta','problema_carga','cambio_propuesto','cambio_operacion','cancelacion')");
            t.HasCheckConstraint("ck_novedades_origen", "origen in ('repartidor','operacion')");
            t.HasCheckConstraint("ck_novedades_estado", "estado in ('abierta','resuelta','rechazada')");
            // El origen no es libre: cada tipo nace de un solo lado.
            t.HasCheckConstraint("ck_novedades_origen_coherente",
                "(origen = 'repartidor' and tipo in ('incidencia_ruta','problema_carga','cambio_propuesto')) "
                + "or (origen = 'operacion' and tipo in ('cambio_operacion','cancelacion'))");
            t.HasCheckConstraint("ck_novedades_propuesta",
                "(tipo = 'cambio_propuesto') = (propuesta_campo is not null and propuesta_valor_nuevo is not null)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RutaId).HasColumnName("ruta_id");
        b.Property(x => x.ParadaId).HasColumnName("parada_id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
        b.Property(x => x.Origen).HasColumnName("origen").IsRequired();
        b.Property(x => x.Categoria).HasColumnName("categoria");
        b.Property(x => x.Descripcion).HasColumnName("descripcion").IsRequired();
        b.Property(x => x.PropuestaCampo).HasColumnName("propuesta_campo");
        b.Property(x => x.PropuestaValorAnterior).HasColumnName("propuesta_valor_anterior");
        b.Property(x => x.PropuestaValorNuevo).HasColumnName("propuesta_valor_nuevo");
        b.Property(x => x.FotoPath).HasColumnName("foto_path");
        b.Property(x => x.DeviceUuid).HasColumnName("device_uuid");
        b.Property(x => x.Estado).HasColumnName("estado").HasDefaultValue("abierta");
        b.Property(x => x.CreadaPor).HasColumnName("creada_por");
        b.Property(x => x.CreadaEn).HasColumnName("creada_en");
        b.Property(x => x.ResueltaPor).HasColumnName("resuelta_por");
        b.Property(x => x.ResueltaEn).HasColumnName("resuelta_en");
        b.Property(x => x.Resolucion).HasColumnName("resolucion");
        b.Property(x => x.VistoEn).HasColumnName("visto_en");

        b.HasOne(x => x.Ruta).WithMany().HasForeignKey(x => x.RutaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Parada).WithMany().HasForeignKey(x => x.ParadaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pedido).WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreadaPorUsuario).WithMany().HasForeignKey(x => x.CreadaPor).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ResueltaPorUsuario).WithMany().HasForeignKey(x => x.ResueltaPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.RutaId, x.Estado });
        // El panel del back-office pregunta "¿qué está abierto?" sin importar la ruta.
        b.HasIndex(x => x.Estado).HasFilter("estado = 'abierta'").HasDatabaseName("ix_novedades_abiertas");
        // RNF-02: el reintento del mismo dispositivo no crea una segunda fila.
        b.HasIndex(x => new { x.RutaId, x.DeviceUuid }).IsUnique().HasFilter("device_uuid is not null");
    }
}
