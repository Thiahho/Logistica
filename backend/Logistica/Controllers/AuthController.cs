using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Logistica.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Logistica.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, IWebHostEnvironment env) : ControllerBase
{
    public record LoginRequest(string Email, string Password);
    public record AccessTokenResponse(string AccessToken);

    /// <summary>Sin límite de intentos era fuerza bruta viable (auditoría de seguridad) —
    /// EnableRateLimiting("login") lo frena a 5 intentos por minuto por IP (Program.cs).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var resultado = await auth.LoginAsync(req.Email, req.Password, IpDelCliente(), ct);
        if (resultado is null) return Unauthorized();

        EstablecerCookieRefresh(resultado.RefreshToken);
        return Ok(new AccessTokenResponse(resultado.AccessToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AccessTokenResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("refresh_token", out var refreshActual) || refreshActual is null)
            return Unauthorized();

        var resultado = await auth.RefrescarAsync(refreshActual, IpDelCliente(), ct);
        if (resultado is null)
        {
            Response.Cookies.Delete("refresh_token", CookiePath());
            return Unauthorized();
        }

        EstablecerCookieRefresh(resultado.RefreshToken);
        return Ok(new AccessTokenResponse(resultado.AccessToken));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue("refresh_token", out var refreshActual) && refreshActual is not null)
            await auth.RevocarAsync(refreshActual, ct);

        Response.Cookies.Delete("refresh_token", CookiePath());
        return NoContent();
    }

    [HttpGet("yo")]
    [Authorize]
    public IActionResult Yo() => Ok(new
    {
        id = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
        nombre = User.FindFirstValue("nombre"),
        rol = User.FindFirstValue(ClaimTypes.Role),
        clienteId = User.FindFirstValue("cliente_id"),
    });

    private string? IpDelCliente() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Path="/" a propósito: el cookie scoping ignora el puerto (solo mira host + path), así
    // que con Path="/api/auth" el browser nunca lo manda en una navegación a localhost:3000/pedidos
    // y el middleware del frontend no puede detectar la sesión. Con "/" sí viaja entre puertos
    // del mismo host, que es exactamente lo que necesitamos en este setup de frontend/backend
    // separados en localhost.
    private static CookieOptions CookiePath() => new() { Path = "/" };

    // Sin `Expires`/`MaxAge` a propósito: cookie de SESIÓN, no persistente. El pedido explícito
    // es que cerrar la ventana del navegador cierre la sesión — con `Expires` (antes, 30 días vía
    // Jwt:RefreshDias) la cookie sobrevivía a cerrar y reabrir el navegador. El refresh token en
    // sí sigue teniendo su propio vencimiento server-side (RefreshToken.ExpiraEn, Entidades/
    // RefreshToken.cs) como red de seguridad independiente — esto solo cambia cuánto vive la
    // cookie en el navegador, no cuánto es válido el token si de algún modo se reenviara.
    private void EstablecerCookieRefresh(string token)
    {
        Response.Cookies.Append("refresh_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
