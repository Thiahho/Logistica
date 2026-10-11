namespace Logistica.Opciones;

/// <summary>Resumen diario de envíos por email al cliente (B6, Servicios/AvisosEstadoService.cs).</summary>
public class OpcionesAvisosEstado
{
    /// <summary>Apaga el proceso entero. Con esto prendido solo reciben correo los clientes que
    /// Administración activó en su ficha.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Hora local a la que cierra el día del resumen: informa lo que pasó desde el cierre
    /// anterior hasta esta hora.</summary>
    public TimeOnly HoraResumen { get; set; } = new(20, 0);
}
