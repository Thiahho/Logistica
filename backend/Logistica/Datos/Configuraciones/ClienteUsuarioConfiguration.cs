using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class ClienteUsuarioConfiguration : IEntityTypeConfiguration<ClienteUsuario>
{
    public void Configure(EntityTypeBuilder<ClienteUsuario> b)
    {
        b.ToTable("clientes_usuarios", t =>
        {
            t.HasCheckConstraint("ck_clientes_usuarios_rol", "rol in ('dueno','usuario')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(x => x.ClienteId).HasColumnName("cliente_id").IsRequired();
        b.Property(x => x.Nombre).HasColumnName("nombre").IsRequired();
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        b.Property(x => x.Rol).HasColumnName("rol").IsRequired().HasDefaultValue(RolesCliente.Dueno);
        b.Property(x => x.Activo).HasColumnName("activo").HasDefaultValue(true);
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");

        b.HasOne(x => x.Cliente).WithMany()
            .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Email).IsUnique();
        // La unicidad de email ENTRE esta tabla y `usuarios` la impone un trigger (migración
        // ReglasDeBaseDeDatos): un índice único de EF no puede abarcar dos tablas.
    }
}
