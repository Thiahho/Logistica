using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Datos.Configuraciones;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", t =>
        {
            // Exactamente uno de los dos: un login es de personal interno o de un cliente,
            // nunca ambos ni ninguno.
            t.HasCheckConstraint("ck_refresh_tokens_actor_unico",
                "(usuario_id is not null and cliente_usuario_id is null) or " +
                "(usuario_id is null and cliente_usuario_id is not null)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        b.Property(x => x.ClienteUsuarioId).HasColumnName("cliente_usuario_id");
        b.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
        b.Property(x => x.CreadoEn).HasColumnName("creado_en").HasDefaultValueSql("now()");
        b.Property(x => x.ExpiraEn).HasColumnName("expira_en");
        b.Property(x => x.RevocadoEn).HasColumnName("revocado_en");
        b.Property(x => x.ReemplazadoPorId).HasColumnName("reemplazado_por_id");
        b.Property(x => x.CreadoPorIp).HasColumnName("creado_por_ip");

        b.Ignore(x => x.EstaActivo);

        b.HasOne(x => x.Usuario).WithMany()
            .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ClienteUsuario).WithMany()
            .HasForeignKey(x => x.ClienteUsuarioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RefreshToken>().WithMany()
            .HasForeignKey(x => x.ReemplazadoPorId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UsuarioId);
        b.HasIndex(x => x.ClienteUsuarioId);
    }
}
