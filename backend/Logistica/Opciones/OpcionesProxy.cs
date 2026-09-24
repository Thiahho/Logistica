namespace Logistica.Opciones;

/// <summary>
/// Producción detrás de Vercel → Render (auditoria_seguridad.md hallazgo 14). Sin esto,
/// Connection.RemoteIpAddress es la IP del proxy y el límite de intentos de login ("login", por IP)
/// queda con un único cupo para todo el sistema.
///
/// Habilitado: toma X-Forwarded-For/X-Forwarded-Proto del salto que agrega Render (solo el último,
/// ForwardLimit = 1) y, si el request trae el Secreto que pone frontend/proxy.ts, la IP que informó
/// Vercel en X-Cliente-Ip. Deshabilitado (desarrollo): no se lee ninguna cabecera de reenvío.
/// </summary>
public class OpcionesProxy
{
    public bool Habilitado { get; set; }

    /// <summary>Compartido con PROXY_SECRETO del frontend. Nunca en appsettings.json: en Render,
    /// variable de entorno Proxy__Secreto.</summary>
    public string? Secreto { get; set; }
}
