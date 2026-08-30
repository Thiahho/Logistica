namespace Logistica.Opciones;

/// <summary>
/// Config de la prueba de entrega (H2, RF-20/21/29). MotivosFallo y UmbralDesvioMetros son
/// PROVISIONALES — mismo estatus que Precio:FactorUrgencia: valores de arranque para poder
/// programar la validación, no una decisión de negocio cerrada (acta_sistema_v3.md §13, decisión
/// abierta sobre RF-21 en construccion_v1.md §10). Se cambian acá, sin tocar código.
/// </summary>
public class OpcionesPruebaEntrega
{
    /// <summary>Raíz en disco donde se guardan las fotos, fuera de wwwroot: no se sirven como
    /// estático público (RNF-09, contienen datos personales del destinatario).</summary>
    public string RaizFotos { get; set; } = null!;

    public int TamanoMaximoKb { get; set; } = 400;

    /// <summary>RF-29: distancia entre la prueba y el destino a partir de la cual se marca
    /// "desvío alto". Provisional — depende de la calidad real de geocodificación con rutas
    /// reales, no se conoce todavía.</summary>
    public int UmbralDesvioMetros { get; set; } = 150;

    /// <summary>RF-21: lista cerrada de motivos de entrega fallida. Provisional.</summary>
    public string[] MotivosFallo { get; set; } = [];
}
