namespace Logistica.Opciones;

/// <summary>
/// Panel de cobranza: config del canal de email (Resend). La ApiKey NUNCA va acá/appsettings.json
/// — dev: `dotnet user-secrets set "Resend:ApiKey" "..."` (mismo tratamiento que Jwt:Key);
/// producción: variable de entorno `Resend__ApiKey`.
/// </summary>
public class OpcionesResend
{
    /// <summary>null o vacío = MODO SIMULADO: Servicios/EmailService.cs no pega a la red, loguea
    /// y reporta el envío como simulado. Permite construir y demostrar todo el panel sin cuenta
    /// de Resend — la cuenta, el dominio verificado y la ApiKey son una dependencia externa que
    /// no tiene por qué bloquear el desarrollo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Resend solo entrega a destinatarios arbitrarios desde un dominio verificado —
    /// "onboarding@resend.dev" (el default de una cuenta nueva) solo entrega a la casilla dueña
    /// de la cuenta. Cambiar a una casilla del dominio propio una vez verificado.</summary>
    public string Remitente { get; set; } = "Logistica <onboarding@resend.dev>";

    public string? ResponderA { get; set; }

    public string BaseUrl { get; set; } = "https://api.resend.com/";
}
