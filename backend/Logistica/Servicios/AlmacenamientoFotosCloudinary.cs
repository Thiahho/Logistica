using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>
/// Producción: las fotos van a Cloudinary como "authenticated" (privadas: sin la firma del servidor
/// no se pueden ver), con public_id = {CarpetaCloudinary}/{la misma ruta relativa de siempre, sin .jpg}.
/// La base guarda la ruta relativa de siempre, así que cambiar de proveedor no toca ningún dato.
///
/// El navegador nunca recibe una URL de Cloudinary: los endpoints de foto (PruebasEntregaController,
/// NovedadesController) siguen chequeando el rol y devuelven el archivo, que este servicio descarga del
/// lado del servidor con una URL firmada. RNF-09: el control de acceso sigue siendo el del sistema.
/// </summary>
public class AlmacenamientoFotosCloudinary(
    IOptions<OpcionesPruebaEntrega> opciones,
    IOptions<OpcionesAlmacenamiento> almacenamiento,
    Cloudinary cloudinary,
    HttpClient http) : AlmacenamientoFotos(opciones)
{
    private const string TipoPrivado = "authenticated";

    private string PublicId(string rutaRelativa) =>
        $"{almacenamiento.Value.CarpetaCloudinary.Trim('/')}/{Path.ChangeExtension(rutaRelativa, null)}";

    protected override async Task EscribirAsync(string rutaRelativa, Stream contenido, CancellationToken ct)
    {
        var resultado = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(Path.GetFileName(rutaRelativa), contenido),
            PublicId = PublicId(rutaRelativa),
            Type = TipoPrivado,
            // Mismo deviceUuid en un reintento = mismo public_id: pisa la subida anterior, igual que
            // el disco local. Idempotente.
            Overwrite = true,
            UniqueFilename = false,
        }, ct);

        // No es un error del usuario (no InvalidOperationException, que sale como 400 con el mensaje):
        // es el proveedor. ManejadorExcepciones lo devuelve como 500 sin detalle y queda en el log.
        if (resultado.Error is not null)
            throw new IOException($"Cloudinary rechazó la subida de {rutaRelativa}: {resultado.Error.Message}");
    }

    public override async Task<Stream?> AbrirAsync(string rutaRelativa, CancellationToken ct)
    {
        var url = cloudinary.Api.UrlImgUp
            .Type(TipoPrivado)
            .Secure(true)
            .Signed(true)
            .BuildUrl($"{PublicId(rutaRelativa)}.jpg");

        using var respuesta = await http.GetAsync(url, ct);
        if (!respuesta.IsSuccessStatusCode) return null;

        // Las fotos pesan como mucho TamanoMaximoKb (400 KB): se copian a memoria para poder cerrar la
        // respuesta HTTP antes de devolver el stream al controller.
        var copia = new MemoryStream();
        await respuesta.Content.CopyToAsync(copia, ct);
        copia.Position = 0;
        return copia;
    }
}
