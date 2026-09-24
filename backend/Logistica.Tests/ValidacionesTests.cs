using Logistica.Dominio;

namespace Logistica.Tests;

// auditoria_seguridad.md hallazgos 3 y 4: formato de email, CUIT y teléfono, y política de contraseñas.
public class ValidacionesTests
{
    [Theory]
    [InlineData("20-12345678-6", "20123456786")]
    [InlineData("20 12345678 6", "20123456786")]
    [InlineData("30712345671", "30712345671")]
    public void Cuit_valido_se_normaliza(string entrada, string esperado) =>
        Assert.Equal(esperado, Validaciones.NormalizarCuit(entrada));

    [Theory]
    [InlineData("20-12345678-5")] // verificador incorrecto
    [InlineData("99-12345678-6")] // prefijo inexistente
    [InlineData("2012345678")]    // 10 dígitos
    [InlineData("20-1234567A-6")]
    [InlineData("")]
    public void Cuit_invalido(string entrada) => Assert.Null(Validaciones.NormalizarCuit(entrada));

    [Theory]
    [InlineData("ana@empresa.com", true)]
    [InlineData("ana.perez+avisos@empresa.com.ar", true)]
    [InlineData("ana@localhost", false)]
    [InlineData("ana", false)]
    [InlineData("Ana <ana@empresa.com>", false)]
    [InlineData("ana@empresa.", false)]
    public void Email(string email, bool valido) => Assert.Equal(valido, Validaciones.EmailValido(email));

    [Theory]
    [InlineData("11 4555-1234", true)]
    [InlineData("+54 9 11 4555-1234", true)]
    [InlineData("(011) 4555.1234", true)]
    [InlineData("4555", false)]
    [InlineData("11-4555-ABCD", false)]
    public void Telefono(string telefono, bool valido) => Assert.Equal(valido, Validaciones.TelefonoValido(telefono));

    [Theory]
    [InlineData("Repartidor2026x", "juan@empresa.com")]
    [InlineData("camion azul 42", "ana@empresa.com")]
    public void Contrasena_valida(string clave, string email) => Assert.Null(PoliticaContrasena.Validar(clave, email));

    [Theory]
    [InlineData("abc12345", "x@y.com")]      // corta
    [InlineData("soloLetrasLargas", "x@y.com")]
    [InlineData("12345678901", "x@y.com")]   // sin letras
    [InlineData("Logistica123!", "x@y.com")] // la del seed
    [InlineData("juanperez2026", "juanperez@empresa.com")] // contiene el email
    public void Contrasena_invalida(string clave, string email) => Assert.NotNull(PoliticaContrasena.Validar(clave, email));
}
