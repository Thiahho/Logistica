using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
    public void Configure(EntityTypeBuilder<Ruta> b)
    {
        b.ToTable("rutas", t =>
        {
            t.HasCheckConstraint("ck_rutas_estado", "estado in ('planificada','en_curso','cerrada')");
            // RF-35: pisos numéricos, mismo criterio que los hallazgos de auditoría del
            // changelog 4.1 (un negativo entraba en silencio o rebotaba sin traducir).
            t.HasCheckConstraint("ck_rutas_retiro_bultos",
                "(retiro_bultos_esperados is null or retiro_bultos_esperados >= 0) and (retiro_bultos_contados is null or retiro_bultos_contados >= 0)");
            t.HasCheckConstraint("ck_rutas_retiro_km_inicial",
                "retiro_km_inicial is null or retiro_km_inicial >= 0");
            t.HasCheckConstraint("ck_rutas_cierre_repartidor_montos",
                "(cierre_repartidor_km_final is null or cierre_repartidor_km_final >= 0) and (cierre_repartidor_combustible is null or cierre_repartidor_combustible >= 0) and (cierre_repartidor_peajes is null or cierre_repartidor_peajes >= 0)");
            // La observación es obligatoria solo cuando los dos conteos difieren (acta §7). Va
            // como check y no como validación de C# porque es una regla del dato, no del flujo:
            // vale para cualquier escritura, venga del controller o de un fix a mano en la base.
            t.HasCheckConstraint("ck_rutas_retiro_discrepancia_observada",
                "retiro_confirmado_en is null or retiro_bultos_contados = retiro_bultos_esperados or (retiro_observaciones is not null and btrim(retiro_observaciones) <> '')");
        });

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
        b.Property(x => x.CerradaPor).HasColumnName("cerrada_por");
        b.Property(x => x.CreadaEn).HasColumnName("creada_en").HasDefaultValueSql("now()");

        // RF-35 — retiro con conteo firmado (acta changelog 4.7)
        b.Property(x => x.RetiroConfirmadoEn).HasColumnName("retiro_confirmado_en");
        b.Property(x => x.RetiroBultosEsperados).HasColumnName("retiro_bultos_esperados");
        b.Property(x => x.RetiroBultosContados).HasColumnName("retiro_bultos_contados");
        b.Property(x => x.RetiroObservaciones).HasColumnName("retiro_observaciones");
        b.Property(x => x.RetiroFirmaPath).HasColumnName("retiro_firma_path");
        b.Property(x => x.RetiroKmInicial).HasColumnName("retiro_km_inicial");
        b.Property(x => x.RetiroDeviceUuid).HasColumnName("retiro_device_uuid");

        // RF-26 mitad de calle — declaración del repartidor (acta changelog 4.7)
        b.Property(x => x.CierreRepartidorEn).HasColumnName("cierre_repartidor_en");
        b.Property(x => x.CierreRepartidorKmFinal).HasColumnName("cierre_repartidor_km_final");
        b.Property(x => x.CierreRepartidorCombustible).HasColumnName("cierre_repartidor_combustible").HasColumnType("numeric(12,2)");
        b.Property(x => x.CierreRepartidorPeajes).HasColumnName("cierre_repartidor_peajes").HasColumnType("numeric(12,2)");
        b.Property(x => x.CierreRepartidorNotas).HasColumnName("cierre_repartidor_notas");
        b.Property(x => x.CierreRepartidorDeviceUuid).HasColumnName("cierre_repartidor_device_uuid");

        b.HasOne(x => x.Repartidor).WithMany()
            .HasForeignKey(x => x.RepartidorId).OnDelete(DeleteBehavior.Restrict);
        // Sin propiedad de navegación: el admin que cerró no se recorre desde la ruta en ninguna
        // consulta, solo se muestra su nombre resolviéndolo aparte. La FK igual existe para que
        // la columna no pueda quedar con un uuid que no es un usuario.
        b.HasOne<Usuario>().WithMany()
            .HasForeignKey(x => x.CerradaPor).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehiculo).WithMany()
            .HasForeignKey(x => x.VehiculoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Origen).WithMany()
            .HasForeignKey(x => x.OrigenUbicacionId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Fecha);
    }
}
