using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class PagoInformadoConfiguration : IEntityTypeConfiguration<PagoInformado>
{
    public void Configure(EntityTypeBuilder<PagoInformado> b)
    {
        b.ToTable("pagos_informados", t =>
        {
            t.HasCheckConstraint("ck_pagos_informados_medio", "medio in ('transferencia','efectivo','cheque','otro')");
            t.HasCheckConstraint("ck_pagos_informados_monto", "monto > 0");
            t.HasCheckConstraint("ck_pagos_informados_estado", "estado in ('pendiente','confirmado','rechazado')");
            // Confirmado si y solo si ya existe el Pago que lo imputa.
            t.HasCheckConstraint("ck_pagos_informados_pago", "(estado = 'confirmado') = (pago_id is not null)");
            t.HasCheckConstraint("ck_pagos_informados_rechazo",
                "estado <> 'rechazado' or (motivo_rechazo is not null and length(btrim(motivo_rechazo)) > 0)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.ClienteUsuarioId).HasColumnName("cliente_usuario_id");
        b.Property(x => x.Monto).HasColumnName("monto").HasColumnType("numeric(12,2)");
        b.Property(x => x.FechaPago).HasColumnName("fecha_pago");
        b.Property(x => x.Medio).HasColumnName("medio").IsRequired();
        b.Property(x => x.Nota).HasColumnName("nota");
        b.Property(x => x.ComprobantePath).HasColumnName("comprobante_path");
        b.Property(x => x.Estado).HasColumnName("estado").IsRequired().HasDefaultValue(EstadosPagoInformado.Pendiente);
        b.Property(x => x.MotivoRechazo).HasColumnName("motivo_rechazo");
        b.Property(x => x.RevisadoPor).HasColumnName("revisado_por");
        b.Property(x => x.RevisadoEn).HasColumnName("revisado_en");
        b.Property(x => x.PagoId).HasColumnName("pago_id");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ClienteUsuario).WithMany()
            .HasForeignKey(x => x.ClienteUsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RevisadoPorUsuario).WithMany()
            .HasForeignKey(x => x.RevisadoPor).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pago).WithMany()
            .HasForeignKey(x => x.PagoId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.CreadoEn });
        b.HasIndex(x => x.Estado);
        b.HasIndex(x => x.PagoId).IsUnique();
    }
}
