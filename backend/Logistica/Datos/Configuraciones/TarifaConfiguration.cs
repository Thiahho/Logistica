using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class TarifaConfiguration : IEntityTypeConfiguration<Tarifa>
{
    public void Configure(EntityTypeBuilder<Tarifa> b)
    {
        b.ToTable("tarifas", t =>
        {
            t.HasCheckConstraint("ck_tarifas_precio", "precio > 0");
            t.HasCheckConstraint("ck_tarifas_vigencia",
                "vigente_hasta is null or vigente_hasta >= vigente_desde");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.ZonaId).HasColumnName("zona_id");
        b.Property(x => x.Precio).HasColumnName("precio").HasColumnType("numeric(12,2)");
        b.Property(x => x.VigenteDesde).HasColumnName("vigente_desde").HasDefaultValueSql("current_date");
        b.Property(x => x.VigenteHasta).HasColumnName("vigente_hasta");
        b.Property(x => x.CreadaEn).HasColumnName("creada_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Zona).WithMany()
            .HasForeignKey(x => x.ZonaId).OnDelete(DeleteBehavior.Restrict);

        // tarifa_vigente(cliente, zona): la tarifa activa por combinación
        b.HasIndex(x => new { x.ClienteId, x.ZonaId })
            .HasFilter("vigente_hasta is null");

        // El unique real es sobre (coalesce(cliente_id,0), zona_id, vigente_desde) — índice de
        // expresión, se crea a mano en la migración ReglasDeBaseDeDatos.
    }
}
