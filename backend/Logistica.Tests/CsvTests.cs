using Logistica.Web;

namespace Logistica.Tests;

// F-2: el apóstrofo contra CSV injection es para texto. Un número negativo no es una fórmula y, con
// prefijo, la planilla lo lee como texto (márgenes negativos que no suman).
public class CsvTests
{
    private static string Campo(object? valor) =>
        Csv.Escribir(["x"], [[valor]]).Split(Environment.NewLine)[1];

    [Theory]
    [InlineData(-1500.5, "-1500.5")]
    [InlineData(-3.0, "-3")]
    public void Un_double_negativo_no_lleva_prefijo(double valor, string esperado) =>
        Assert.Equal(esperado, Campo(valor));

    [Fact]
    public void Un_decimal_negativo_no_lleva_prefijo() =>
        Assert.Equal("-1500.25", Campo(-1500.25m));

    [Fact]
    public void Un_entero_negativo_no_lleva_prefijo()
    {
        Assert.Equal("-7", Campo(-7));
        Assert.Equal("-7", Campo(-7L));
        Assert.Equal("-2.5", Campo(-2.5f));
    }

    [Theory]
    [InlineData("-x", "'-x")]
    [InlineData("=SUMA(A1)", "'=SUMA(A1)")]
    [InlineData("+54 11", "'+54 11")]
    [InlineData("@usuario", "'@usuario")]
    [InlineData("-150", "'-150")]
    public void Un_texto_que_empieza_como_formula_lleva_prefijo(string valor, string esperado) =>
        Assert.Equal(esperado, Campo(valor));

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("dijo \"hola\"", "\"dijo \"\"hola\"\"\"")]
    [InlineData("=a;b", "\"'=a;b\"")]
    [InlineData("normal", "normal")]
    public void El_entrecomillado_no_cambia(string valor, string esperado) =>
        Assert.Equal(esperado, Campo(valor));

    [Fact]
    public void Nulo_es_vacio() => Assert.Equal("", Campo(null));
}
