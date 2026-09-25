namespace Logistica.Opciones;

/// <summary>
/// Urgencias en una ruta en curso (acta RF-45, changelog 4.26; §7 "13:00 — ventana de reagrupamiento,
/// único ingreso de urgencias", "solo si desplazan menos de tres paradas"). PROVISIONALES, mismo estatus
/// que Precio:FactorUrgencia: la duración de la ventana no está escrita en el acta y la decide la Empresa.
/// </summary>
public class OpcionesUrgencias
{
    public TimeOnly VentanaDesde { get; set; } = new(13, 0);
    public int VentanaMinutos { get; set; } = 60;

    /// <summary>"Menos de tres" (acta §7): como mucho dos paradas pendientes quedan después de la urgencia.</summary>
    public int MaxParadasDesplazadas { get; set; } = 2;
}
