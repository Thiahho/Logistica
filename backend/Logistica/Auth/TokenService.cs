using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Logistica.Entidades;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Logistica.Auth;

/// <summary>
/// Emite access tokens JWT de vida corta y refresh tokens opacos.
/// El refresh token nunca se persiste en texto plano; solo su hash (ver Hash).
/// </summary>
public class TokenService(IOptions<OpcionesJwt> opciones)
{
    private readonly OpcionesJwt _o = opciones.Value;

    public string CrearAccessToken(Usuario usuario)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new("nombre", usuario.Nombre),
            new(ClaimTypes.Role, usuario.Rol),
        };
        if (usuario.ClienteId is not null)
            claims.Add(new Claim("cliente_id", usuario.ClienteId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _o.Issuer,
            audience: _o.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_o.AccessMinutos),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string TokenPlano, RefreshToken Entidad) CrearRefreshToken(Guid usuarioId, string? ip)
    {
        var tokenPlano = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entidad = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TokenHash = Hash(tokenPlano),
            CreadoEn = DateTimeOffset.UtcNow,
            ExpiraEn = DateTimeOffset.UtcNow.AddDays(_o.RefreshDias),
            CreadoPorIp = ip,
        };
        return (tokenPlano, entidad);
    }

    public static string Hash(string tokenPlano) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenPlano)));
}
