namespace Logistica.Opciones;

/// <summary>
/// B5/B13 (diseño_b5_portal_carga.md §6, diseño_b13_recepcion_portal.md §1): corte de carga del
/// portal del cliente. Valor provisional 16:00 — la decisión comercial real (si en algún momento
/// hace falta diferenciarlo por cliente) sigue sin cerrar, mismo criterio que
/// OpcionesPrecio.FactorUrgencia.
/// </summary>
public class OpcionesPortal
{
    public TimeOnly HoraCorte { get; set; } = new(16, 0);
}
