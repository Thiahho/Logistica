using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
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
    private static readonly PasswordHasher<ClienteUsuario> HasherCliente = new();

    /// <summary>Hashing reusado por UsuariosController (alta y reset de contraseña) para no
    /// instanciar un segundo PasswordHasher con configuración potencialmente distinta.</summary>
    public static string Hashear(Usuario usuario, string password) => Hasher.HashPassword(usuario, password);

    /// <summary>Análogo a Hashear pero para logins de cliente (tabla separada, ver ClienteUsuario).
    /// PasswordHasher&lt;T&gt; no lee ningún dato del objeto que recibe: dos instancias genéricas
    /// distintas producen exactamente el mismo algoritmo de hashing.</summary>
    public static string HashearCliente(ClienteUsuario clienteUsuario, string password) =>
        HasherCliente.HashPassword(clienteUsuario, password);

    public async Task<ResultadoLogin?> LoginAsync(string email, string password, string? ip, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Email == email && u.Activo, ct);
        if (usuario is not null)
        {
            if (Hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password) == PasswordVerificationResult.Failed)
                return null;
            return await EmitirParAsync(usuario, ip, ct);
        }

        var clienteUsuario = await db.ClientesUsuarios.SingleOrDefaultAsync(u => u.Email == email && u.Activo, ct);
        if (clienteUsuario is null) return null;

        if (HasherCliente.VerifyHashedPassword(clienteUsuario, clienteUsuario.PasswordHash, password) == PasswordVerificationResult.Failed)
            return null;

        return await EmitirParClienteAsync(clienteUsuario, ip, ct);
    }

    /// <summary>Rota el refresh token: el usado queda revocado y encadenado al nuevo.</summary>
    public async Task<ResultadoLogin?> RefrescarAsync(string refreshTokenPlano, string? ip, CancellationToken ct = default)
    {
        var hash = TokenService.Hash(refreshTokenPlano);
        var actual = await db.RefreshTokens
            .Include(t => t.Usuario)
            .Include(t => t.ClienteUsuario)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (actual is null || !actual.EstaActivo) return null;

        if (actual.Usuario is not null)
        {
            if (!actual.Usuario.Activo) return null;

            actual.RevocadoEn = DateTimeOffset.UtcNow;
            var (nuevoPlano, nuevaEntidad) = tokens.CrearRefreshToken(actual.Usuario.Id, ip);
            actual.ReemplazadoPorId = nuevaEntidad.Id;
            db.RefreshTokens.Add(nuevaEntidad);
            await db.SaveChangesAsync(ct);

            var access = tokens.CrearAccessToken(actual.Usuario);
            return new ResultadoLogin(access, nuevoPlano, nuevaEntidad.ExpiraEn);
        }
        else
        {
            var clienteUsuario = actual.ClienteUsuario!;
            if (!clienteUsuario.Activo) return null;

            actual.RevocadoEn = DateTimeOffset.UtcNow;
            var (nuevoPlano, nuevaEntidad) = tokens.CrearRefreshTokenCliente(clienteUsuario.Id, ip);
            actual.ReemplazadoPorId = nuevaEntidad.Id;
            db.RefreshTokens.Add(nuevaEntidad);
            await db.SaveChangesAsync(ct);

            var access = tokens.CrearAccessToken(clienteUsuario);
            return new ResultadoLogin(access, nuevoPlano, nuevaEntidad.ExpiraEn);
        }
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

    private async Task<ResultadoLogin> EmitirParClienteAsync(ClienteUsuario clienteUsuario, string? ip, CancellationToken ct)
    {
        var access = tokens.CrearAccessToken(clienteUsuario);
        var (plano, entidad) = tokens.CrearRefreshTokenCliente(clienteUsuario.Id, ip);
        db.RefreshTokens.Add(entidad);
        // Solo el login con contraseña, no cada refresh: el dueño quiere ver cuándo entró cada
        // empleado, no la rotación de tokens.
        ActividadPortal.Registrar(db, clienteUsuario.ClienteId, clienteUsuario.Id, AccionesPortal.SesionIniciada);
        await db.SaveChangesAsync(ct);
        return new ResultadoLogin(access, plano, entidad.ExpiraEn);
    }
}
