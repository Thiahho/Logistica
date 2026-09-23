using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

public record ResultadoEnvioEmail(bool Enviado, bool Simulado, string? MensajeId, string? Error);

/// <summary>
/// HttpClient tipado directo contra la API REST de Resend (`POST /emails`), sin el paquete NuGet
/// oficial — mismo patrón que GeocodificacionService/RuteoService, los dos únicos precedentes de
/// integrar una API externa en este repo (`AddHttpClient&lt;T&gt;` + `User-Agent`). El .csproj
/// tiene 5 `PackageReference` en total; lo que aporta el SDK (reintentos, modelo de error tipado)
/// es justo lo que este diseño no quiere — el envío es best-effort, el error se muestra como
/// texto al admin en la pantalla del panel de cobranza.
///
/// Nunca propaga una excepción: un proveedor de email caído no puede tirar 500 sobre un endpoint
/// que además arma links de WhatsApp con el mismo mensaje — esos tienen que salir igual.
/// </summary>
public class EmailService(HttpClient http, IOptions<OpcionesResend> opciones, ILogger<EmailService> log)
{
    private record EmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] List<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string? ReplyTo);

    private record EmailResponse([property: JsonPropertyName("id")] string? Id);

    /// <summary>false = no hay ApiKey configurada, todo envío sale simulado. El panel lo muestra
    /// en la previsualización para que nunca parezca un envío real que no fue tal.</summary>
    public bool Configurado => !string.IsNullOrWhiteSpace(opciones.Value.ApiKey);

    public async Task<ResultadoEnvioEmail> EnviarAsync(string destino, string asunto, string cuerpo, CancellationToken ct)
    {
        var op = opciones.Value;
        if (string.IsNullOrWhiteSpace(op.ApiKey))
        {
            // Nunca el cuerpo en el log — lleva el monto adeudado del cliente.
            log.LogInformation("Email simulado (Resend sin ApiKey configurada) a {Destino}: {Asunto}", destino, asunto);
            return new ResultadoEnvioEmail(Enviado: false, Simulado: true, MensajeId: null, Error: null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(new EmailRequest(op.Remitente, [destino], asunto, cuerpo, op.ResponderA)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", op.ApiKey);

            var respuesta = await http.SendAsync(request, ct);
            if (respuesta.IsSuccessStatusCode)
            {
                var body = await respuesta.Content.ReadFromJsonAsync<EmailResponse>(cancellationToken: ct);
                return new ResultadoEnvioEmail(true, false, body?.Id, null);
            }

            var detalle = await respuesta.Content.ReadAsStringAsync(ct);
            log.LogWarning("Resend rechazó el envío a {Destino}: {Status} {Detalle}", destino, (int)respuesta.StatusCode, detalle);
            return new ResultadoEnvioEmail(false, false, null, Recortar(detalle));
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "No se pudo contactar a Resend para {Destino}", destino);
            return new ResultadoEnvioEmail(false, false, null, "No se pudo contactar al proveedor de email.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout del HttpClient (Program.cs), no cancelación del caller.
            return new ResultadoEnvioEmail(false, false, null, "El proveedor de email no respondió a tiempo.");
        }
    }

    private static string Recortar(string texto) => texto.Length > 200 ? texto[..200] + "…" : texto;
}
