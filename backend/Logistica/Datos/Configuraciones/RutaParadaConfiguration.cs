using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class RutaParadaConfiguration : IEntityTypeConfiguration<RutaParada>
{
    public void Configure(EntityTypeBuilder<RutaParada> b)
    {
        b.ToTable("ruta_paradas", t =>
        {
            t.HasCheckConstraint("ck_ruta_paradas_tipo", "tipo in ('retiro','entrega','deposito')");
            t.HasCheckConstraint("ck_ruta_paradas_estado", "estado in ('pendiente','completada','fallida','cancelada')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RutaId).HasColumnName("ruta_id");
        b.Property(x => x.UbicacionId).HasColumnName("ubicacion_id");
        b.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
        b.Property(x => x.Orden).HasColumnName("orden");
        b.Property(x => x.Anclada).HasColumnName("anclada").HasDefaultValue(false);
        b.Property(x => x.Estado).HasColumnName("estado").HasDefaultValue("pendiente");
        b.Property(x => x.LlegadaEn).HasColumnName("llegada_en");
        b.Property(x => x.SalidaEn).HasColumnName("salida_en");

        b.HasOne(x => x.Ruta).WithMany()
            .HasForeignKey(x => x.RutaId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Ubicacion).WithMany()
            .HasForeignKey(x => x.UbicacionId).OnDelete(DeleteBehavior.Restrict);

        // constraint parada_orden_uk unique(ruta_id, orden) deferrable initially deferred:
        // EF no expresa DEFERRABLE, se crea a mano en la migración ReglasDeBaseDeDatos.
        // Sin eso, el drag & drop de RF-12 choca a mitad del reordenamiento.
    }
}
