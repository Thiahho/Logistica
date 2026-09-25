using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    private const string Colores = "in ('verde','amarillo','rojo')";

    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        b.ToTable("clientes", t =>
        {
            t.HasCheckConstraint("ck_clientes_color_pago", $"color_pago {Colores}");
            t.HasCheckConstraint("ck_clientes_color_trato", $"color_trato {Colores}");
            t.HasCheckConstraint("ck_clientes_color_oper", $"color_oper {Colores}");
            t.HasCheckConstraint("ck_clientes_ciclo_facturacion", "ciclo_facturacion in ('quincenal','mensual')");
            // Los cuatro campos de corte suspendido van juntos o ninguno (E1, §10.2-L4).
            t.HasCheckConstraint("ck_clientes_corte_suspendido",
                "(corte_suspendido_hasta is null and corte_suspendido_por is null) " +
                "or (corte_suspendido_hasta is not null and corte_suspendido_por is not null " +
                "and corte_suspendido_motivo is not null)");
            // Definición F (acta 4.21): el ajuste manual es de un rango como máximo y, si existe, tiene
            // motivo escrito y vencimiento.
            t.HasCheckConstraint("ck_clientes_rango_ajuste",
                "rango_ajuste = 0 or (rango_ajuste in (-1, 1) and rango_ajuste_motivo is not null " +
                "and btrim(rango_ajuste_motivo) <> '' and rango_ajuste_vence is not null and rango_ajuste_por is not null)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RazonSocial).HasColumnName("razon_social").IsRequired();
        b.Property(x => x.Cuit).HasColumnName("cuit");
        b.Property(x => x.Contacto).HasColumnName("contacto");
        b.Property(x => x.Telefono).HasColumnName("telefono");
        b.Property(x => x.Email).HasColumnName("email");
        b.Property(x => x.ColorPago).HasColumnName("color_pago").HasDefaultValue("rojo");
        b.Property(x => x.ColorTrato).HasColumnName("color_trato").HasDefaultValue("amarillo");
        b.Property(x => x.ColorOper).HasColumnName("color_oper").HasDefaultValue("amarillo");
        b.Property(x => x.Activo).HasColumnName("activo").HasDefaultValue(true);
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.Property(x => x.CicloFacturacion).HasColumnName("ciclo_facturacion").HasDefaultValue("mensual");
        b.Property(x => x.CorteSuspendidoHasta).HasColumnName("corte_suspendido_hasta");
        b.Property(x => x.CorteSuspendidoMotivo).HasColumnName("corte_suspendido_motivo");
        b.Property(x => x.CorteSuspendidoPor).HasColumnName("corte_suspendido_por");
        b.Property(x => x.CorteSuspendidoEn).HasColumnName("corte_suspendido_en");

        b.HasOne(x => x.CorteSuspendidoPorUsuario).WithMany()
            .HasForeignKey(x => x.CorteSuspendidoPor).OnDelete(DeleteBehavior.Restrict);

        // B3 (acta RF-42, changelog 4.21)
        b.Property(x => x.RangoCalculado).HasColumnName("rango_calculado").HasDefaultValue("sin_rango");
        b.Property(x => x.RangoCalculadoEn).HasColumnName("rango_calculado_en");
        b.Property(x => x.RangoAjuste).HasColumnName("rango_ajuste").HasDefaultValue((short)0);
        b.Property(x => x.RangoAjusteMotivo).HasColumnName("rango_ajuste_motivo");
        b.Property(x => x.RangoAjusteVence).HasColumnName("rango_ajuste_vence");
        b.Property(x => x.RangoAjustePor).HasColumnName("rango_ajuste_por");
        b.Property(x => x.RangoAjusteEn).HasColumnName("rango_ajuste_en");
        b.HasOne<Rango>().WithMany().HasForeignKey(x => x.RangoCalculado).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.RangoAjustePor).OnDelete(DeleteBehavior.Restrict);
    }
}
