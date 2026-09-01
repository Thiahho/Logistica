using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> b)
    {
        b.ToTable("vehiculos", t =>
        {
            t.HasCheckConstraint("ck_vehiculos_anio", "anio is null or anio between 1950 and 2100");
            t.HasCheckConstraint("ck_vehiculos_capacidad", "capacidad_paradas > 0");
            t.HasCheckConstraint("ck_vehiculos_tipo", "tipo in ('camioneta','moto')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Patente).HasColumnName("patente").IsRequired();
        b.Property(x => x.Descripcion).HasColumnName("descripcion");
        b.Property(x => x.Marca).HasColumnName("marca");
        b.Property(x => x.Modelo).HasColumnName("modelo");
        b.Property(x => x.Tipo).HasColumnName("tipo").HasDefaultValue("camioneta");
        b.Property(x => x.Anio).HasColumnName("anio");
        b.Property(x => x.KmActual).HasColumnName("km_actual");
        b.Property(x => x.VenceVtv).HasColumnName("vence_vtv");
        b.Property(x => x.VenceSeguro).HasColumnName("vence_seguro");
        b.Property(x => x.CostoKm).HasColumnName("costo_km").HasColumnType("numeric(12,2)");
        b.Property(x => x.CapacidadParadas).HasColumnName("capacidad_paradas").HasDefaultValue(24);
        b.Property(x => x.Activo).HasColumnName("activo").HasDefaultValue(true);
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasIndex(x => x.Patente).IsUnique();
    }
}
