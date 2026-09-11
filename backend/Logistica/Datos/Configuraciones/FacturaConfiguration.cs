using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class FacturaConfiguration : IEntityTypeConfiguration<Factura>
{
    public void Configure(EntityTypeBuilder<Factura> b)
    {
        b.ToTable("facturas", t =>
        {
            t.HasCheckConstraint("ck_facturas_ciclo", "ciclo in ('quincenal','mensual')");
            t.HasCheckConstraint("ck_facturas_periodo", "periodo_hasta >= periodo_desde");
            t.HasCheckConstraint("ck_facturas_vencimiento", "fecha_vencimiento >= periodo_hasta");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.Ciclo).HasColumnName("ciclo").IsRequired();
        b.Property(x => x.PeriodoDesde).HasColumnName("periodo_desde");
        b.Property(x => x.PeriodoHasta).HasColumnName("periodo_hasta");
        b.Property(x => x.FechaEmision).HasColumnName("fecha_emision");
        b.Property(x => x.FechaVencimiento).HasColumnName("fecha_vencimiento");
        b.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(12,2)");
        b.Property(x => x.EmitidaPor).HasColumnName("emitida_por");
        b.Property(x => x.CreadaEn).HasColumnName("creada_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmitidaPorUsuario).WithMany()
            .HasForeignKey(x => x.EmitidaPor).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(x => x.Factura)
            .HasForeignKey(x => x.FacturaId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.FechaEmision });
        b.HasIndex(x => x.FechaVencimiento);
        // Idempotencia del cierre a nivel de base (regla §3.3: la app muestra el error, no lo
        // previene por su cuenta) — correr el cierre dos veces el mismo período no duplica.
        b.HasIndex(x => new { x.ClienteId, x.PeriodoHasta }).IsUnique().HasDatabaseName("ux_facturas_periodo");
    }
}
