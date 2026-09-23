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
    }
}
