using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class PagoConfiguration : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> b)
    {
        b.ToTable("pagos", t =>
        {
            t.HasCheckConstraint("ck_pagos_medio", "medio in ('transferencia','efectivo','cheque','otro')");
            // Insert-only (trg_pagos_inmutable): un pago mal cargado se corrige con un
            // contraasiento negativo, nunca editando el original.
            t.HasCheckConstraint("ck_pagos_monto", "monto <> 0");
            t.HasCheckConstraint("ck_pagos_reverso_nota",
                "monto > 0 or (nota is not null and length(btrim(nota)) > 0)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.Monto).HasColumnName("monto").HasColumnType("numeric(12,2)");
        b.Property(x => x.FechaPago).HasColumnName("fecha_pago").HasDefaultValueSql("current_date");
        b.Property(x => x.Medio).HasColumnName("medio").IsRequired();
        b.Property(x => x.Nota).HasColumnName("nota");
        b.Property(x => x.RegistradoPor).HasColumnName("registrado_por");
        b.Property(x => x.RegistradoEn).HasColumnName("registrado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RegistradoPorUsuario).WithMany()
            .HasForeignKey(x => x.RegistradoPor).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.FechaPago });
    }
}
