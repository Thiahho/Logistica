using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Auth;

public record ResultadoLogin(string AccessToken, string RefreshToken, DateTimeOffset RefreshExpiraEn);

/// <summary>
/// Login, rotación de refresh token y logout. La rotación de sesión no es un evento de dominio
/// (no dispara fn_log_estado_pedido), así que escribe con SaveChangesAsync directo, no con
/// GuardarComoAsync.
/// </summary>
public class AuthService(LogisticaDbContext db, TokenService tokens)
{
    private static readonly PasswordHasher<Usuario> Hasher = new();

    /// <summary>Hashing reusado por UsuariosController (alta y reset de contraseña) para no
    /// instanciar un segundo PasswordHasher con configuración potencialmente distinta.</summary>
    public static string Hashear(Usuario usuario, string password) => Hasher.HashPassword(usuario, password);

    public async Task<ResultadoLogin?> LoginAsync(string email, string password, string? ip, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Email == email && u.Activo, ct);
        if (usuario is null) return null;

        if (Hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password) == PasswordVerificationResult.Failed)
            return null;

        return await EmitirParAsync(usuario, ip, ct);
    }

    /// <summary>Rota el refresh token: el usado queda revocado y encadenado al nuevo.</summary>
    public async Task<ResultadoLogin?> RefrescarAsync(string refreshTokenPlano, string? ip, CancellationToken ct = default)
    {
        var hash = TokenService.Hash(refreshTokenPlano);
        var actual = await db.RefreshTokens.Include(t => t.Usuario)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (actual is null || !actual.EstaActivo || !actual.Usuario.Activo) return null;

        actual.RevocadoEn = DateTimeOffset.UtcNow;
        var (nuevoPlano, nuevaEntidad) = tokens.CrearRefreshToken(actual.UsuarioId, ip);
        actual.ReemplazadoPorId = nuevaEntidad.Id;
        db.RefreshTokens.Add(nuevaEntidad);
        await db.SaveChangesAsync(ct);

        var access = tokens.CrearAccessToken(actual.Usuario);
        return new ResultadoLogin(access, nuevoPlano, nuevaEntidad.ExpiraEn);
    }

    public async Task RevocarAsync(string refreshTokenPlano, CancellationToken ct = default)
    {
        var hash = TokenService.Hash(refreshTokenPlano);
        var actual = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (actual is null || !actual.EstaActivo) return;

        actual.RevocadoEn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ResultadoLogin> EmitirParAsync(Usuario usuario, string? ip, CancellationToken ct)
    {
        var access = tokens.CrearAccessToken(usuario);
        var (plano, entidad) = tokens.CrearRefreshToken(usuario.Id, ip);
        db.RefreshTokens.Add(entidad);
        await db.SaveChangesAsync(ct);
        return new ResultadoLogin(access, plano, entidad.ExpiraEn);
    }
}
