using System.Net.Mail;

namespace Logistica.Dominio;

/// <summary>
/// Formato de email, CUIT y teléfono (auditoria_seguridad.md hallazgo 3). Antes se guardaban tal cual:
/// no era una vulnerabilidad (EF parametriza todo), pero dejaba entrar datos basura en silencio — un
/// email que nunca recibe el aviso de cobranza, un CUIT que no identifica a nadie. Lógica pura: se
/// prueba sola en Logistica.Tests.
/// </summary>
public static class Validaciones
{
    /// <summary>Una dirección que MailAddress acepta, sin nombre visible ("Juan &lt;x@y.com&gt;") y con
    /// un dominio que tiene al menos un punto (MailAddress acepta "x@localhost").</summary>
    public static bool EmailValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        var limpio = email.Trim();
        try
        {
            var direccion = new MailAddress(limpio);
            return direccion.Address == limpio && direccion.Host.Contains('.') && !direccion.Host.EndsWith('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static readonly int[] PesosCuit = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
    private static readonly string[] PrefijosCuit = ["20", "23", "24", "27", "30", "33", "34"];

    /// <summary>
    /// El CUIT sin guiones ni espacios si es válido: 11 dígitos, prefijo de persona o empresa y dígito
    /// verificador módulo 11 (resto 11 → 0; resto 10 no es un CUIT emitido). null si no es válido.
    /// </summary>
    public static string? NormalizarCuit(string? cuit)
    {
        if (string.IsNullOrWhiteSpace(cuit)) return null;
        var digitos = new string(cuit.Where(c => c is not ('-' or ' ' or '.')).ToArray());
        if (digitos.Length != 11 || !digitos.All(char.IsAsciiDigit) || !PrefijosCuit.Contains(digitos[..2])) return null;

        var suma = PesosCuit.Select((peso, i) => peso * (digitos[i] - '0')).Sum();
        var verificador = 11 - suma % 11;
        if (verificador == 11) verificador = 0;
        return verificador != 10 && verificador == digitos[10] - '0' ? digitos : null;
    }

    /// <summary>Entre 8 y 15 dígitos después de sacar espacios, guiones, paréntesis, puntos y un "+"
    /// inicial: cubre un fijo de CABA sin característica y un móvil con +54 9.</summary>
    public static bool TelefonoValido(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) return false;
        var t = telefono.Trim();
        if (t.StartsWith('+')) t = t[1..];
        var digitos = new string(t.Where(c => c is not (' ' or '-' or '(' or ')' or '.')).ToArray());
        return digitos.Length is >= 8 and <= 15 && digitos.All(char.IsAsciiDigit);
    }
}
