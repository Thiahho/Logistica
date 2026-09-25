using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ViajeConfiguration : IEntityTypeConfiguration<Viaje>
{
    public void Configure(EntityTypeBuilder<Viaje> b)
    {
        b.ToTable("viajes", t =>
        {
            t.HasCheckConstraint("ck_viajes_estado", "estado in ('activo','cancelado')");
            t.HasCheckConstraint("ck_viajes_tipo_vehiculo", "tipo_vehiculo is null or tipo_vehiculo in ('camioneta','moto')");
            // Lo carga un login del portal o alguien del personal interno, nunca los dos ni ninguno.
            t.HasCheckConstraint("ck_viajes_creador",
                "(creado_por_cliente_usuario_id is null) <> (creado_por_usuario_id is null)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ClienteId).HasColumnName("cliente_id");
        b.Property(x => x.FechaEntrega).HasColumnName("fecha_entrega");
        b.Property(x => x.OrigenUbicacionId).HasColumnName("origen_ubicacion_id");
        b.Property(x => x.TipoVehiculo).HasColumnName("tipo_vehiculo");
        b.Property(x => x.Estado).HasColumnName("estado").IsRequired().HasDefaultValue(EstadosViaje.Activo);
        b.Property(x => x.KmEstimados).HasColumnName("km_estimados").HasColumnType("numeric(8,2)");
        b.Property(x => x.RutaId).HasColumnName("ruta_id");
        b.Property(x => x.CreadoPorClienteUsuarioId).HasColumnName("creado_por_cliente_usuario_id");
        b.Property(x => x.CreadoPorUsuarioId).HasColumnName("creado_por_usuario_id");
        b.Property(x => x.Observaciones).HasColumnName("observaciones");
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.OrigenUbicacion).WithMany().HasForeignKey(x => x.OrigenUbicacionId).OnDelete(DeleteBehavior.Restrict);
        // Si Operación elimina la ruta propuesta (para rearmarla), el viaje queda sin ruta; sus
        // pedidos vuelven a ser candidatos del armado.
        b.HasOne(x => x.Ruta).WithMany().HasForeignKey(x => x.RutaId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.CreadoPorClienteUsuario).WithMany().HasForeignKey(x => x.CreadoPorClienteUsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreadoPorUsuario).WithMany().HasForeignKey(x => x.CreadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClienteId, x.FechaEntrega });
        b.HasIndex(x => x.RutaId);
    }
}
