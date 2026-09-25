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

    /// <summary>Tope de paradas de un viaje (envío de varias paradas), portal y BackOffice. El mismo
    /// número que Ruta.CapacidadParadas por defecto: un viaje es una ruta propia.</summary>
    public int MaxParadasViaje { get; set; } = 24;

    /// <summary>Acta changelog 4.30: los clientes del portal son suscriptores (pagan su cuota o factura),
    /// así que el portal no muestra el precio por envío — ni al dueño. El precio se sigue calculando,
    /// guardando y facturando igual; esto solo decide si la API lo manda. true vuelve al comportamiento
    /// anterior (el dueño ve precios, el empleado nunca). Ver CurrentUserExtensions.VePreciosDeEnvio.</summary>
    public bool MostrarPrecios { get; set; }
}
