namespace Logistica.Opciones;

/// <summary>
/// FACTOR_URGENCIA y FACTOR_DESCUENTO_RUTA son parámetros de configuración, no constantes
/// (construccion_v1.md §6 y §9). Valores iniciales provisionales: la decisión comercial real
/// está pendiente (construccion_v1.md §10).
/// </summary>
public class OpcionesPrecio
{
    public decimal FactorUrgencia { get; set; } = 0.20m;
    public decimal FactorDescuentoRuta { get; set; } = 0.10m;
}
