using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Logistica.Auth;

/// <summary>
/// Clave con que el teléfono del repartidor cifra el borrador local de una entrega (nombre, DNI y foto
/// del receptor). Vive en el servidor y no en el teléfono: el borrador ya solo se recupera con sesión
/// (el refresh token viaja en una cookie httpOnly), así que pedirla no le quita nada al repartidor, y
/// sin sesión lo guardado en el teléfono no se puede leer.
///
/// Se deriva por usuario de Jwt:Key con HKDF-SHA256 en vez de guardarse: no hace falta otro secreto ni
/// otra tabla. La etiqueta separa esta clave de la que firma los tokens (nunca se entrega Jwt:Key ni
/// nada que permita reconstruirla) y de la de cualquier otro usuario. Rotar Jwt:Key deja ilegibles los
/// borradores que hubiera, que de todos modos no viven más de un día.
/// </summary>
public class ClaveDeBorradores(IOptions<OpcionesJwt> opciones)
{
    public const int Bytes = 32;

    /// <summary>Va en la etiqueta: subirla cambia la clave de todos y deja ilegible lo ya cifrado.</summary>
    public const int VersionActual = 1;

    // Mismos bytes que usa la firma de los tokens (TokenService, Program.cs).
    private readonly byte[] _secreto = Encoding.UTF8.GetBytes(opciones.Value.Key);

    public byte[] Derivar(Guid usuarioId) => Derivar(_secreto, usuarioId, VersionActual);

    public static byte[] Derivar(byte[] secreto, Guid usuarioId, int version) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            secreto,
            Bytes,
            info: Encoding.UTF8.GetBytes($"borrador-entrega:v{version}:{usuarioId:D}"));
}
