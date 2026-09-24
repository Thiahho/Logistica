namespace Logistica.Dominio;

/// <summary>
/// Política de contraseñas (auditoria_seguridad.md hallazgo 4). Antes alcanzaba con 8 caracteres
/// cualquiera, también para una cuenta de administración. Se aplica en los cuatro lugares que fijan una
/// contraseña (alta y cambio de usuario interno, alta y cambio de login de cliente); las contraseñas ya
/// guardadas siguen valiendo hasta que se cambien. El hash sigue siendo el de PasswordHasher.
/// </summary>
public static class PoliticaContrasena
{
    public const int LargoMinimo = 10;

    /// <summary>Las que primero prueba un ataque, más la de los usuarios de prueba del seed
    /// (DatosSemilla): en producción no tiene que poder fijarse nunca.</summary>
    private static readonly HashSet<string> Comunes = new(StringComparer.OrdinalIgnoreCase)
    {
        "logistica123!", "logistica123", "password123", "contraseña123", "contrasena123", "1234567890",
        "qwerty12345", "abc1234567", "admin12345", "administrador1", "123456789a", "bftransportes1",
    };

    public const string Regla =
        "La contraseña debe tener al menos 10 caracteres, con letras y números, y no puede contener tu email ni ser una contraseña común.";

    /// <summary>null si cumple; si no, el motivo en español para devolver en el 400.</summary>
    public static string? Validar(string? contrasena, string? email)
    {
        if (string.IsNullOrEmpty(contrasena) || contrasena.Length < LargoMinimo)
            return $"La contraseña debe tener al menos {LargoMinimo} caracteres.";
        if (!contrasena.Any(char.IsLetter) || !contrasena.Any(char.IsDigit))
            return "La contraseña debe tener letras y números.";
        if (Comunes.Contains(contrasena))
            return "Esa contraseña es demasiado común. Elegí otra.";

        var usuarioEmail = email?.Split('@')[0].Trim();
        if (usuarioEmail is { Length: >= 3 } && contrasena.Contains(usuarioEmail, StringComparison.OrdinalIgnoreCase))
            return "La contraseña no puede contener tu email.";
        return null;
    }
}
