namespace Logistica.Opciones;

/// <summary>
/// Dónde se guardan fotos de entrega, firmas del retiro y fotos de novedades
/// (Servicios/AlmacenamientoFotos.cs). "local": carpeta PruebaEntrega:RaizFotos (desarrollo).
/// "cloudinary": producción — el disco del contenedor de Render se borra en cada deploy
/// (auditoria_seguridad.md hallazgo 15).
/// </summary>
public class OpcionesAlmacenamiento
{
    public string Proveedor { get; set; } = "local";

    /// <summary>Solo "cloudinary": cloudinary://key:secret@cloud. Nunca en appsettings.json —
    /// variable de entorno Almacenamiento__CloudinaryUrl.</summary>
    public string? CloudinaryUrl { get; set; }

    /// <summary>Prefijo de los public_id en Cloudinary: separa entornos que compartan cuenta.</summary>
    public string CarpetaCloudinary { get; set; } = "logistica";
}
