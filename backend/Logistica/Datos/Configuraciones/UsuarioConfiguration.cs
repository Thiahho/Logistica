using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("usuarios", t =>
        {
            t.HasCheckConstraint("ck_usuarios_rol",
                "rol in ('administracion','operacion','repartidor')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        b.Property(x => x.Rol).HasColumnName("rol").IsRequired();
        b.Property(x => x.Activo).HasColumnName("activo").HasDefaultValue(true);
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasIndex(x => x.Email).IsUnique();
    }
}
