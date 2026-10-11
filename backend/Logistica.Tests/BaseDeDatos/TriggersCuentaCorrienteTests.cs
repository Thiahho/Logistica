using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Tests.BaseDeDatos;

// E1 (Anexo I §10.2-B/D12): facturas y pagos son libros de solo inserción — trg_facturas_inmutable,
// trg_pagos_inmutable — y un pago mal cargado se corrige con un contraasiento (ck_pagos_monto,
// ck_pagos_reverso_nota).
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class TriggersCuentaCorrienteTests(BaseDePrueba baseDatos)
{
    private async Task<Factura> FacturaAsync()
    {
        var cliente = await baseDatos.ClienteAsync();
        await using var db = baseDatos.CrearContexto();
        var factura = new Factura
        {
            ClienteId = cliente.Id,
            Ciclo = "mensual",
            PeriodoDesde = new DateOnly(2026, 9, 1),
            PeriodoHasta = new DateOnly(2026, 9, 30),
            FechaEmision = new DateOnly(2026, 10, 1),
            FechaVencimiento = new DateOnly(2026, 10, 10),
            Total = 10000m,
            CreadaEn = DateTimeOffset.UtcNow,
        };
        db.Facturas.Add(factura);
        await db.SaveChangesAsync();
        return factura;
    }

    private async Task<Pago> PagoAsync(int clienteId, decimal monto, string? nota = null)
    {
        await using var db = baseDatos.CrearContexto();
        var pago = new Pago
        {
            ClienteId = clienteId, Monto = monto, FechaPago = new DateOnly(2026, 10, 2), Medio = "transferencia",
            Nota = nota, RegistradoEn = DateTimeOffset.UtcNow,
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();
        return pago;
    }

    private async Task<string?> RestriccionVioladaAsync(Func<Task> escritura)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(escritura);
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        return postgres.ConstraintName;
    }

    [Theory]
    [InlineData("update facturas set total = 1 where id = {0}")]
    [InlineData("update facturas set fecha_vencimiento = '2027-01-01' where id = {0}")]
    [InlineData("delete from facturas where id = {0}")]
    public async Task Una_factura_emitida_no_se_edita_ni_se_borra(string sql)
    {
        var factura = await FacturaAsync();

        await baseDatos.RechazaPorTriggerAsync(string.Format(sql, factura.Id), "una factura emitida no se edita ni se borra");
    }

    [Theory]
    [InlineData("update pagos set monto = 1 where id = {0}")]
    [InlineData("update pagos set nota = 'corregido' where id = {0}")]
    [InlineData("delete from pagos where id = {0}")]
    public async Task Un_pago_registrado_no_se_edita_ni_se_borra(string sql)
    {
        var cliente = await baseDatos.ClienteAsync();
        var pago = await PagoAsync(cliente.Id, 5000m);

        await baseDatos.RechazaPorTriggerAsync(string.Format(sql, pago.Id), "pagos es de solo inserción");
    }

    [Fact]
    public async Task Un_pago_mal_cargado_se_corrige_con_un_contraasiento_con_nota()
    {
        var cliente = await baseDatos.ClienteAsync();
        await PagoAsync(cliente.Id, 1400m);

        await PagoAsync(cliente.Id, -1400m, "Cargado por error al cliente equivocado");

        var saldo = await baseDatos.EscalarAsync<decimal>($"select saldo_cliente({cliente.Id})");
        Assert.Equal(0m, saldo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Un_contraasiento_sin_nota_se_rechaza(string? nota)
    {
        var cliente = await baseDatos.ClienteAsync();

        var restriccion = await RestriccionVioladaAsync(() => PagoAsync(cliente.Id, -1400m, nota));

        Assert.Equal("ck_pagos_reverso_nota", restriccion);
    }

    [Fact]
    public async Task Un_pago_de_monto_cero_se_rechaza()
    {
        var cliente = await baseDatos.ClienteAsync();

        var restriccion = await RestriccionVioladaAsync(() => PagoAsync(cliente.Id, 0m));

        Assert.Equal("ck_pagos_monto", restriccion);
    }
}
