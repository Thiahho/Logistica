using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class RangoConfiguration : IEntityTypeConfiguration<Rango>
{
    public void Configure(EntityTypeBuilder<Rango> b)
    {
        b.ToTable("rangos", t =>
        {
            t.HasCheckConstraint("ck_rangos_descuento", "descuento_pct between 0 and 100");
            t.HasCheckConstraint("ck_rangos_pct_pagos", "min_pct_pagos_en_termino is null or min_pct_pagos_en_termino between 0 and 100");
            t.HasCheckConstraint("ck_rangos_minimos",
                "(min_envios_trimestre is null or min_envios_trimestre >= 0) and (min_facturacion_trimestre is null or min_facturacion_trimestre >= 0) " +
                "and (min_antiguedad_meses is null or min_antiguedad_meses >= 0) and (min_semanas_activas is null or min_semanas_activas between 0 and 14) " +
                "and (limite_credito is null or limite_credito >= 0)");
        });

        b.HasKey(x => x.Codigo);
        b.Property(x => x.Codigo).HasColumnName("codigo");
        b.Property(x => x.Nombre).HasColumnName("nombre");
        b.Property(x => x.Orden).HasColumnName("orden");
        b.Property(x => x.MinEnviosTrimestre).HasColumnName("min_envios_trimestre");
        b.Property(x => x.MinFacturacionTrimestre).HasColumnName("min_facturacion_trimestre").HasColumnType("numeric(14,2)");
        b.Property(x => x.MinAntiguedadMeses).HasColumnName("min_antiguedad_meses");
        b.Property(x => x.MinSemanasActivas).HasColumnName("min_semanas_activas");
        b.Property(x => x.MinPctPagosEnTermino).HasColumnName("min_pct_pagos_en_termino").HasColumnType("numeric(5,2)");
        b.Property(x => x.DescuentoPct).HasColumnName("descuento_pct").HasColumnType("numeric(5,2)").HasDefaultValue(0m);
        b.Property(x => x.LimiteCredito).HasColumnName("limite_credito").HasColumnType("numeric(14,2)");
        b.Property(x => x.Prioridad).HasColumnName("prioridad").HasDefaultValue(0);

        b.HasIndex(x => x.Orden).IsUnique();

        // Las cinco filas son estructura (los rangos que nombra el Anexo I), no valores comerciales:
        // umbrales y efectos arrancan vacíos (acta §13). La prioridad sigue el orden, como punto de partida.
        b.HasData(
            new Rango { Codigo = "sin_rango", Nombre = "Sin rango", Orden = 0, Prioridad = 0 },
            new Rango { Codigo = "bronce", Nombre = "Bronce", Orden = 1, Prioridad = 1 },
            new Rango { Codigo = "plata", Nombre = "Plata", Orden = 2, Prioridad = 2 },
            new Rango { Codigo = "oro", Nombre = "Oro", Orden = 3, Prioridad = 3 },
            new Rango { Codigo = "empresa", Nombre = "Empresa", Orden = 4, Prioridad = 4 });
    }
}
