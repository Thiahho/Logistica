using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Fila de la previsualización — con `Asunto`/`Mensaje` YA armados por el servidor
/// (nunca el front decide el texto), para que el admin vea exactamente qué se va a mandar antes
/// de confirmar. `Omitido` no null = no se va a mandar nada para este cliente (y el resto de los
/// campos queda parcial o null).</summary>
public record AvisoPrevisualizado(
    int ClienteId, string RazonSocial, string Categoria,
    decimal Monto, DateOnly? Vencimiento,
    string? Email, string? TelefonoNormalizado,
    string? Asunto, string? Mensaje,
    string? LinkWhatsApp, string? Omitido);

public record PrevisualizacionAvisos(bool ResendConfigurado, List<AvisoPrevisualizado> Avisos);

public record ResultadoAvisoCliente(
    int ClienteId, string RazonSocial, string Categoria,
    string? EmailDestino, bool EmailEnviado, bool EmailSimulado, string? EmailError,
    string? TelefonoNormalizado, string? LinkWhatsApp,
    long? EventoId, string? Omitido);

/// <summary>
/// Panel de cobranza: orquesta CuentaCorrienteService.RiesgoAsync (estado real de cada cliente,
/// nunca lo que mande el front) → plantilla según la categoría → EmailService (best-effort) →
/// link de WhatsApp (siempre que haya teléfono, con o sin email) → registro en eventos_cliente
/// (tipo aviso_cobranza, migración …AgregarTipoEventoAvisoCobranza).
///
/// Sin transacción envolvente, a propósito: cliente por cliente, SaveChangesAsync por iteración,
/// SECUENCIAL (nunca Task.WhenAll — el DbContext no es thread-safe entre llamadas concurrentes
/// del mismo scope, y Resend tiene rate limit en el plan gratuito). El efecto que importa (el
/// mail ya salió) no es transaccional: envolver el registro del evento en una transacción que se
/// revierte ante un fallo produce el peor resultado posible — mail enviado, cero rastro. Y un
/// cliente con datos inválidos no puede bloquear a los demás del mismo lote.
/// </summary>
public class AvisosCobranzaService(
    LogisticaDbContext db, CuentaCorrienteService cuentaCorriente, EmailService email,
    ILogger<AvisosCobranzaService> log)
{
    public const int MaxClientesPorLote = 100;

    private record Plan(
        ClienteEnRiesgo Cliente, decimal Monto, string Asunto, string Mensaje,
        string? TelefonoNormalizado, string? LinkWhatsApp);

    /// <summary>Resuelve cada clienteId contra el estado REAL de riesgo y arma mensaje + link de
    /// WhatsApp. `clienteIds` que ya no están en riesgo, o sin ningún contacto usable, salen como
    /// `Omitido` en vez de plan — comparten la misma lógica entre previsualizar y enviar, así las
    /// dos vías nunca pueden divergir.</summary>
    private async Task<(List<Plan> Planes, List<AvisoPrevisualizado> Omitidos)> ArmarAsync(
        IReadOnlyCollection<int> clienteIds, DateOnly hoy, CancellationToken ct)
    {
        var enRiesgo = await cuentaCorriente.RiesgoAsync(
            hoy, CuentaCorrienteService.DiasPreavisoDefault, clienteIds, soloActivos: false, ct);
        var porId = enRiesgo.ToDictionary(c => c.ClienteId);

        var planes = new List<Plan>();
        var omitidos = new List<AvisoPrevisualizado>();

        foreach (var id in clienteIds.Distinct())
        {
            if (!porId.TryGetValue(id, out var cliente))
            {
                omitidos.Add(new AvisoPrevisualizado(
                    id, "", "", 0m, null, null, null, null, null, null,
                    "Ya no tiene deuda vencida ni una factura próxima a vencer."));
                continue;
            }

            var monto = cliente.Categoria == "vencido" ? cliente.DeudaVencida : cliente.SaldoProximoAVencer;
            var mensaje = cliente.Categoria == "vencido"
                ? PlantillasAviso.DeudaVencida(cliente.RazonSocial, monto, cliente.ServicioCortado)
                // Categoria == "por_vencer" garantiza ProximoVencimiento no null (RiesgoAsync).
                : PlantillasAviso.ProximoAVencer(
                    cliente.RazonSocial, monto, cliente.ProximoVencimiento!.Value, cliente.DiasHastaVencimiento ?? 0);

            var telefonoNormalizado = EnlaceWhatsApp.NormalizarTelefonoAr(cliente.Telefono);
            var link = telefonoNormalizado is null ? null : EnlaceWhatsApp.ConstruirLink(telefonoNormalizado, mensaje.Cuerpo);

            if (string.IsNullOrWhiteSpace(cliente.Email) && telefonoNormalizado is null)
            {
                omitidos.Add(new AvisoPrevisualizado(
                    cliente.ClienteId, cliente.RazonSocial, cliente.Categoria, monto, cliente.ProximoVencimiento,
                    null, null, mensaje.Asunto, mensaje.Cuerpo, null, "Sin email ni teléfono."));
                continue;
            }

            planes.Add(new Plan(cliente, monto, mensaje.Asunto, mensaje.Cuerpo, telefonoNormalizado, link));
        }

        return (planes, omitidos);
    }

    public async Task<PrevisualizacionAvisos> PrevisualizarAsync(
        IReadOnlyCollection<int> clienteIds, DateOnly hoy, CancellationToken ct)
    {
        var (planes, omitidos) = await ArmarAsync(clienteIds, hoy, ct);

        var avisos = planes
            .Select(p => new AvisoPrevisualizado(
                p.Cliente.ClienteId, p.Cliente.RazonSocial, p.Cliente.Categoria, p.Monto, p.Cliente.ProximoVencimiento,
                p.Cliente.Email, p.TelefonoNormalizado, p.Asunto, p.Mensaje, p.LinkWhatsApp, null))
            .Concat(omitidos)
            .ToList();

        return new PrevisualizacionAvisos(email.Configurado, avisos);
    }

    public async Task<List<ResultadoAvisoCliente>> EnviarAsync(
        IReadOnlyCollection<int> clienteIds, bool enviarEmail, Guid actor, DateOnly hoy, CancellationToken ct)
    {
        var (planes, omitidos) = await ArmarAsync(clienteIds, hoy, ct);

        var resultados = omitidos
            .Select(o => new ResultadoAvisoCliente(
                o.ClienteId, o.RazonSocial, o.Categoria, null, false, false, null, null, null, null, o.Omitido))
            .ToList();

        foreach (var plan in planes)
        {
            var emailEnviado = false;
            var emailSimulado = false;
            string? emailError = null;

            if (enviarEmail && !string.IsNullOrWhiteSpace(plan.Cliente.Email))
            {
                var resultadoEmail = await email.EnviarAsync(plan.Cliente.Email, plan.Asunto, plan.Mensaje, ct);
                emailEnviado = resultadoEmail.Enviado;
                emailSimulado = resultadoEmail.Simulado;
                emailError = resultadoEmail.Error;
            }

            var eventoId = await RegistrarEventoAsync(plan, emailEnviado, emailSimulado, actor);

            resultados.Add(new ResultadoAvisoCliente(
                plan.Cliente.ClienteId, plan.Cliente.RazonSocial, plan.Cliente.Categoria,
                plan.Cliente.Email, emailEnviado, emailSimulado, emailError,
                plan.TelefonoNormalizado, plan.LinkWhatsApp, eventoId, null));
        }

        return resultados;
    }

    private async Task<long?> RegistrarEventoAsync(Plan plan, bool emailEnviado, bool emailSimulado, Guid actor)
    {
        try
        {
            var tipo = await db.TiposEventoCliente.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Codigo == "aviso_cobranza", CancellationToken.None);
            if (tipo is null)
            {
                log.LogError(
                    "Falta el tipo_evento_cliente 'aviso_cobranza' (¿migración AgregarTipoEventoAvisoCobranza sin aplicar?) — " +
                    "el aviso al cliente {ClienteId} salió sin quedar registrado.", plan.Cliente.ClienteId);
                return null;
            }

            var canales = new List<string>();
            if (emailEnviado) canales.Add($"email a {plan.Cliente.Email}");
            else if (emailSimulado) canales.Add("email simulado (Resend sin configurar)");
            if (plan.LinkWhatsApp is not null) canales.Add("whatsapp generado");

            var evento = new EventoCliente
            {
                ClienteId = plan.Cliente.ClienteId,
                TipoId = tipo.Id,
                ValorNum = plan.Monto,
                Nota = $"{(plan.Cliente.Categoria == "vencido" ? "Deuda vencida" : "Próximo a vencer")} · {string.Join(" · ", canales)}",
                OcurridoEn = DateTimeOffset.UtcNow,
                RegistradoPorId = actor,
            };
            db.EventosCliente.Add(evento);
            // CancellationToken.None a propósito: si el email ya salió, un `ct` cancelado acá no
            // puede dejarlo sin rastro — ver el doc-comment de la clase.
            await db.SaveChangesAsync(CancellationToken.None);
            return evento.Id;
        }
        catch (Exception ex)
        {
            // Nunca abortar la respuesta después de haber mandado el email: ya salió, tirar acá
            // borraría del resultado la evidencia que el admin necesita ver en pantalla.
            log.LogError(ex, "No se pudo registrar el evento de aviso de cobranza para el cliente {ClienteId}", plan.Cliente.ClienteId);
            return null;
        }
    }
}
