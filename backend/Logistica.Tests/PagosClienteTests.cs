using Logistica.Dominio;

namespace Logistica.Tests;

// E1 (Anexo I §10.2-B/D12): un pago mal cargado se corrige con un contraasiento — monto negativo y
// nota obligatoria. Las restricciones de la base se prueban en BaseDeDatos/TriggersCuentaCorrienteTests.
public class PagosClienteTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(1400)]
    [InlineData(9999999999.99)]
    public void Un_pago_positivo_se_registra_con_o_sin_nota(decimal monto)
    {
        Assert.Null(PagosCliente.Validar(monto, null, pagadoHastaAhora: 0m));
        Assert.Null(PagosCliente.Validar(monto, "Transferencia del lunes", pagadoHastaAhora: 0m));
    }

    [Fact]
    public void Un_monto_cero_se_rechaza() =>
        Assert.Equal("El monto no puede ser cero.", PagosCliente.Validar(0m, "nota", 5000m));

    [Theory]
    [InlineData(10000000000)]
    [InlineData(-10000000000)]
    public void Un_monto_que_no_entra_en_la_columna_se_rechaza(decimal monto) =>
        Assert.Equal("El monto es demasiado grande.", PagosCliente.Validar(monto, "nota", 20_000_000_000m));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Una_correccion_sin_nota_se_rechaza(string? nota) =>
        Assert.Contains("necesita una nota", PagosCliente.Validar(-1400m, nota, 5000m));

    [Theory]
    [InlineData(-1400, 1400)]   // deshace el pago entero
    [InlineData(-400, 1400)]    // deshace una parte
    public void Una_correccion_con_nota_que_no_supera_lo_pagado_se_registra(decimal monto, decimal pagado) =>
        Assert.Null(PagosCliente.Validar(monto, "Cargado al cliente equivocado", pagado));

    [Theory]
    [InlineData(-1400.01, 1400)]
    [InlineData(-1, 0)]
    public void Una_correccion_no_puede_restar_mas_de_lo_pagado(decimal monto, decimal pagado) =>
        Assert.Contains("supera lo que el cliente tiene registrado como pagado",
            PagosCliente.Validar(monto, "Cargado al cliente equivocado", pagado));
}
