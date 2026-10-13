using System.Reflection;
using System.Text;
using Logistica.Auth;
using Logistica.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Logistica.Tests;

// La clave con que el teléfono cifra el borrador de la entrega (nombre, DNI y foto del receptor) sale
// del servidor, derivada por usuario de Jwt:Key. Estas pruebas fijan lo que la hace segura de entregar.
public class ClaveDeBorradoresTests
{
    private const string JwtKey = "clave-de-prueba-de-al-menos-32-bytes-0123456789";

    private static readonly Guid Ana = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beto = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClaveDeBorradores Servicio(string jwtKey = JwtKey) =>
        new(Options.Create(new OpcionesJwt { Key = jwtKey }));

    [Fact]
    public void El_mismo_usuario_recibe_siempre_la_misma_clave()
    {
        // Dos instancias: la clave no depende de nada guardado en memoria, así sobrevive a un reinicio.
        Assert.Equal(Servicio().Derivar(Ana), Servicio().Derivar(Ana));
    }

    [Fact]
    public void Dos_usuarios_reciben_claves_distintas()
    {
        Assert.NotEqual(Servicio().Derivar(Ana), Servicio().Derivar(Beto));
    }

    [Fact]
    public void La_clave_mide_32_bytes()
    {
        Assert.Equal(32, Servicio().Derivar(Ana).Length);
    }

    [Fact]
    public void La_clave_no_es_la_que_firma_los_tokens()
    {
        // Una Jwt:Key de exactamente 32 bytes: si la derivación la devolviera tal cual, coincidirían.
        const string jwtKeyDe32 = "0123456789abcdef0123456789abcdef";
        var firma = Encoding.UTF8.GetBytes(jwtKeyDe32);
        var clave = Servicio(jwtKeyDe32).Derivar(Ana);

        Assert.Equal(32, firma.Length);
        Assert.NotEqual(firma, clave);
    }

    [Fact]
    public void Otra_version_de_la_etiqueta_da_otra_clave()
    {
        var secreto = Encoding.UTF8.GetBytes(JwtKey);

        Assert.Equal(Servicio().Derivar(Ana), ClaveDeBorradores.Derivar(secreto, Ana, ClaveDeBorradores.VersionActual));
        Assert.NotEqual(
            ClaveDeBorradores.Derivar(secreto, Ana, ClaveDeBorradores.VersionActual),
            ClaveDeBorradores.Derivar(secreto, Ana, ClaveDeBorradores.VersionActual + 1));
    }

    [Fact]
    public void Otra_Jwt_Key_da_otra_clave()
    {
        Assert.NotEqual(Servicio().Derivar(Ana), Servicio("otra-clave-de-prueba-de-al-menos-32-bytes-98765").Derivar(Ana));
    }

    [Fact]
    public void El_endpoint_exige_sesion()
    {
        // No hay host de pruebas para pedirlo sin token y ver el 401: se fija que la acción lleve
        // [Authorize] y no [AllowAnonymous], como el resto de AuthController que sí es anónimo.
        var accion = typeof(AuthController).GetMethod(nameof(AuthController.ClaveBorradores))!;

        Assert.NotNull(accion.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(accion.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
