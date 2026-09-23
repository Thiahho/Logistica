namespace Logistica.Dominio;

/// <summary>
/// Sin integración de API de WhatsApp (Anexo I, riesgo R7: WhatsApp Business API tiene costo
/// recurrente por conversación, no presupuestado) — en su lugar, un link `wa.me` con el mensaje
/// precargado que el admin abre y manda a mano desde su propio WhatsApp. Cero costo, cero
/// aprobación de Meta.
/// </summary>
public static class EnlaceWhatsApp
{
    /// <summary>
    /// Normaliza a solo dígitos con código de país 54 antepuesto. Deliberadamente conservador: NO
    /// adivina el `9` de línea móvil ni saca un `15` de numeración local — ninguno de los dos se
    /// puede derivar con certeza sin conocer la característica de cada cliente. Un número mal
    /// normalizado abre un chat vacío en WhatsApp (fallo visible, se corrige a mano en la ficha
    /// del cliente), no un envío silencioso a un destinatario equivocado.
    /// </summary>
    public static string? NormalizarTelefonoAr(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) return null;

        var digitos = new string(telefono.Where(char.IsAsciiDigit).ToArray());
        if (digitos.StartsWith("00")) digitos = digitos[2..]; // prefijo de discado internacional
        if (digitos.StartsWith('0')) digitos = digitos[1..]; // prefijo de larga distancia local

        if (digitos.Length == 0) return null;
        if (!digitos.StartsWith("54")) digitos = "54" + digitos;
        return digitos;
    }

    /// <summary>Link `wa.me` con el mensaje precargado, a partir de un teléfono YA normalizado
    /// (ver <see cref="NormalizarTelefonoAr"/>) — nunca arma el link con el teléfono crudo.</summary>
    public static string ConstruirLink(string telefonoNormalizado, string mensaje) =>
        $"https://wa.me/{telefonoNormalizado}?text={Uri.EscapeDataString(mensaje)}";
}
