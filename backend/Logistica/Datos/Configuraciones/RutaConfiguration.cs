using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
    public void Configure(EntityTypeBuilder<Ruta> b)
    {
        b.ToTable("rutas", t => t.HasCheckConstraint(
            "ck_rutas_estado", "estado in ('planificada','en_curso','cerrada')"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Fecha).HasColumnName("fecha");
        b.Property(x => x.RepartidorId).HasColumnName("repartidor_id");
        b.Property(x => x.VehiculoId).HasColumnName("vehiculo_id");
        b.Property(x => x.OrigenUbicacionId).HasColumnName("origen_ubicacion_id");
        b.Property(x => x.CapacidadParadas).HasColumnName("capacidad_paradas").HasDefaultValue(24);
        b.Property(x => x.Estado).HasColumnName("estado").HasDefaultValue("planificada");
        b.Property(x => x.KmInicial).HasColumnName("km_inicial");
        b.Property(x => x.KmFinal).HasColumnName("km_final");
        b.Property(x => x.CombustibleMonto).HasColumnName("combustible_monto").HasColumnType("numeric(12,2)");
        b.Property(x => x.PeajesMonto).HasColumnName("peajes_monto").HasColumnType("numeric(12,2)");
        b.Property(x => x.OtrosCostos).HasColumnName("otros_costos").HasColumnType("numeric(12,2)");
        b.Property(x => x.PagoRepartidor).HasColumnName("pago_repartidor").HasColumnType("numeric(12,2)");
        b.Property(x => x.NotasCierre).HasColumnName("notas_cierre");
        b.Property(x => x.CerradaEn).HasColumnName("cerrada_en");
        b.Property(x => x.CreadaEn).HasColumnName("creada_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Repartidor).WithMany()
            .HasForeignKey(x => x.RepartidorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehiculo).WithMany()
            .HasForeignKey(x => x.VehiculoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Origen).WithMany()
            .HasForeignKey(x => x.OrigenUbicacionId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Fecha);
    }
}
