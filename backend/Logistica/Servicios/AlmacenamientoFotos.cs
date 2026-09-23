using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>
/// Guarda las fotos de prueba de entrega en disco local (sin bucket externo — regla 2, ninguna
/// credencial de proveedor va al bundle, y acá ni siquiera hace falta un proveedor externo:
/// el proyecto es 100% self-hosted). Ruta determinista pruebas/{paradaId}/{deviceUuid}.jpg: una
/// parada consolidada (RF-14, N pedidos) tiene una sola foto, y el mismo deviceUuid en un
/// reintento sobreescribe el mismo archivo — idempotente sin lógica extra.
///
/// Desde acta changelog 4.7 la carpeta y el sufijo del nombre son parámetros, con los valores de
/// antes como default: la firma del retiro (RF-35) va a retiros/{rutaId}/{deviceUuid}.jpg y la
/// foto del documento a pruebas/{paradaId}/{deviceUuid}-documento.jpg. Los archivos ya guardados
/// no se mueven — el default reproduce exactamente la ruta anterior.
/// </summary>
public class AlmacenamientoFotos(IOptions<OpcionesPruebaEntrega> opciones)
{
    private static readonly byte[] MagicJpeg = [0xFF, 0xD8, 0xFF];
    private readonly OpcionesPruebaEntrega _opciones = opciones.Value;

    private string RaizAbsoluta => Path.GetFullPath(_opciones.RaizFotos);

    /// <param name="carpeta">Primer segmento de la ruta relativa. Literal del código, nunca del
    /// cliente — de ahí que no se valide como sí se valida deviceUuid.</param>
    /// <param name="sufijo">Se agrega al nombre antes de la extensión, para poder guardar más de
    /// un archivo por (carpeta, id, dispositivo) sin que uno pise al otro.</param>
    public async Task<string> GuardarAsync(
        long paradaId, string deviceUuid, Stream contenido, long tamanoBytes, CancellationToken ct,
        string carpeta = "pruebas", string sufijo = "")
    {
        // deviceUuid llega del cliente (multipart) y termina en la ruta de disco: sin este chequeo,
        // un valor tipo "../../../etc/algo" escribiría fuera de RaizFotos (path traversal).
        if (!Guid.TryParse(deviceUuid, out _))
            throw new InvalidOperationException("DeviceUuid inválido.");

        if (tamanoBytes > _opciones.TamanoMaximoKb * 1024L)
            throw new InvalidOperationException($"La foto supera el máximo permitido de {_opciones.TamanoMaximoKb} KB.");

        var buffer = new byte[3];
        var leidos = await contenido.ReadAsync(buffer, ct);
        if (leidos < 3 || !buffer.AsSpan(0, 3).SequenceEqual(MagicJpeg))
            throw new InvalidOperationException("La foto debe ser un JPEG válido.");
        contenido.Position = 0;

        var rutaRelativa = Path.Combine(carpeta, paradaId.ToString(), $"{deviceUuid}{sufijo}.jpg");
        var rutaAbsoluta = Path.Combine(RaizAbsoluta, rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaAbsoluta)!);

        await using (var destino = File.Create(rutaAbsoluta))
            await contenido.CopyToAsync(destino, ct);

        return rutaRelativa.Replace('\\', '/');
    }

    /// <summary>Devuelve null si el archivo no existe (foto huérfana borrada a mano, o ruta
    /// corrupta) — el controller lo traduce a 404, no a 500.</summary>
    public Task<Stream?> AbrirAsync(string rutaRelativa, CancellationToken ct)
    {
        var rutaAbsoluta = Path.GetFullPath(Path.Combine(RaizAbsoluta, rutaRelativa));

        // Defensa en profundidad: FotoPath siempre lo genera GuardarAsync, nunca el cliente, pero
        // el costo de validar que no se escape de RaizFotos es una comparación de strings.
        if (!rutaAbsoluta.StartsWith(RaizAbsoluta, StringComparison.Ordinal))
            return Task.FromResult<Stream?>(null);

        if (!File.Exists(rutaAbsoluta))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(rutaAbsoluta));
    }
}
