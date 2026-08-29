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
    }
}
