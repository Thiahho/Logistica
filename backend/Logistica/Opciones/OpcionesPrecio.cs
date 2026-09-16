namespace Logistica.Opciones;

/// <summary>Tramo de km y el % que suma sobre precio_base (Anexo I §10.2-N, lectura "(ii) precio
/// proporcional al kilometraje recorrido"). Se aplica el último tramo cuyo DesdeKm &lt;= km —
/// semiabierto [DesdeKm, siguiente), mismo criterio que Zona.KmDesde/KmHasta y que
/// ZonasController.Solapan.</summary>
public record TramoKm(decimal DesdeKm, decimal Porcentaje);

/// <summary>
/// FACTOR_URGENCIA y FACTOR_DESCUENTO_RUTA son parámetros de configuración, no constantes
/// (construccion_v1.md §6 y §9). Valores iniciales provisionales: la decisión comercial real
/// está pendiente (construccion_v1.md §10).
/// </summary>
public class OpcionesPrecio
{
    public decimal FactorUrgencia { get; set; } = 0.20m;
    public decimal FactorDescuentoRuta { get; set; } = 0.10m;

    /// <summary>Lista VACÍA por defecto = sin recargo por km = fórmula y comportamiento actuales
    /// intactos para todo pedido existente. Sin valores de fábrica, mismo criterio que
    /// zonas.km_desde y que las tarifas mismas (acta_sistema.md §13: "un número inventado es peor
    /// que un casillero vacío"). Cambio de alcance según Anexo I §6 — ver docs/acta_sistema.md
    /// changelog por la entrada que habilita esta lectura de "precio por kilómetro".</summary>
    public TramoKm[] TramosKm { get; set; } = [];
}
