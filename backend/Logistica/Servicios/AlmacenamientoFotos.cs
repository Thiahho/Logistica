using Logistica.Opciones;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

/// <summary>
/// Guarda las fotos de prueba de entrega en disco local (sin bucket externo — regla 2, ninguna
/// credencial de proveedor va al bundle, y acá ni siquiera hace falta un proveedor externo:
/// el proyecto es 100% self-hosted). Ruta determinista pruebas/{paradaId}/{deviceUuid}.jpg: una
/// parada consolidada (RF-14, N pedidos) tiene una sola foto, y el mismo deviceUuid en un
/// reintento sobreescribe el mismo archivo — idempotente sin lógica extra.
/// </summary>
public class AlmacenamientoFotos(IOptions<OpcionesPruebaEntrega> opciones)
{
    private static readonly byte[] MagicJpeg = [0xFF, 0xD8, 0xFF];
    private readonly OpcionesPruebaEntrega _opciones = opciones.Value;

    private string RaizAbsoluta => Path.GetFullPath(_opciones.RaizFotos);

    public async Task<string> GuardarAsync(long paradaId, string deviceUuid, Stream contenido, long tamanoBytes, CancellationToken ct)
    {
        if (tamanoBytes > _opciones.TamanoMaximoKb * 1024L)
            throw new InvalidOperationException($"La foto supera el máximo permitido de {_opciones.TamanoMaximoKb} KB.");

        var buffer = new byte[3];
        var leidos = await contenido.ReadAsync(buffer, ct);
        if (leidos < 3 || !buffer.AsSpan(0, 3).SequenceEqual(MagicJpeg))
            throw new InvalidOperationException("La foto debe ser un JPEG válido.");
        contenido.Position = 0;

        var rutaRelativa = Path.Combine("pruebas", paradaId.ToString(), $"{deviceUuid}.jpg");
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
