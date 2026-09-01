using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class UbicacionConfiguration : IEntityTypeConfiguration<Ubicacion>
{
    public void Configure(EntityTypeBuilder<Ubicacion> b)
    {
        b.ToTable("ubicaciones", t => t.HasCheckConstraint(
            "ck_ubicaciones_geo_confianza",
            "geo_confianza in ('alta','media','baja','fallida')"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CalleNumero).HasColumnName("calle_numero").IsRequired();
        b.Property(x => x.LocalidadId).HasColumnName("localidad_id");
        b.Property(x => x.Referencia).HasColumnName("referencia");
        b.Property(x => x.NombreDeposito).HasColumnName("nombre_deposito");
        b.Property(x => x.Lat).HasColumnName("lat").HasColumnType("numeric(10,7)");
        b.Property(x => x.Lng).HasColumnName("lng").HasColumnType("numeric(10,7)");
        b.Property(x => x.GeoConfianza).HasColumnName("geo_confianza");
        b.Property(x => x.GeoProveedor).HasColumnName("geo_proveedor");
        b.Property(x => x.GeoFecha).HasColumnName("geo_fecha");
        b.Property(x => x.Verificada).HasColumnName("verificada").HasDefaultValue(false);
        b.Property(x => x.CreadaEn).HasColumnName("creada_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Localidad).WithMany()
            .HasForeignKey(x => x.LocalidadId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.LocalidadId);

        // Dos depósitos no pueden compartir nombre (el selector de armar ruta los lista por
        // nombre). Filtrado, no sobre toda la tabla: el 99% de las filas tiene NombreDeposito
        // null y no debe competir por unicidad entre sí.
        b.HasIndex(x => x.NombreDeposito).IsUnique().HasFilter("nombre_deposito is not null");

        // El unique real es sobre lower(calle_numero), localidad_id — índice de expresión,
        // se crea a mano en la migración ReglasDeBaseDeDatos (EF no expresa funciones en índices).
    }
}
