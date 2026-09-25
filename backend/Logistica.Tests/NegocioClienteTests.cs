using Logistica.Dominio;
using Logistica.Entidades;

namespace Logistica.Tests;

// "Mi negocio" del dueño: períodos de control, agrupación de estados y estado de cuenta.
public class NegocioClienteTests
{
    [Fact]
    public void La_semana_va_de_lunes_a_domingo_aunque_cruce_de_mes()
    {
        // Miércoles 1/10/2026 → semana del lunes 28/09 al domingo 4/10.
        var p = NegocioCliente.Periodo(NegocioCliente.Semana, new DateOnly(2026, 10, 1))!;
        Assert.Equal(new DateOnly(2026, 9, 28), p.Actual.Desde);
        Assert.Equal(new DateOnly(2026, 10, 4), p.Actual.Hasta);
        Assert.Equal(new DateOnly(2026, 9, 21), p.Anterior.Desde);
        Assert.Equal(new DateOnly(2026, 9, 27), p.Anterior.Hasta);
    }

    [Theory]
    [InlineData(2026, 9, 28)] // lunes
    [InlineData(2026, 10, 4)] // domingo
    public void Lunes_y_domingo_caen_en_la_misma_semana(int anio, int mes, int dia)
    {
        var p = NegocioCliente.Periodo(NegocioCliente.Semana, new DateOnly(anio, mes, dia))!;
        Assert.Equal(new DateOnly(2026, 9, 28), p.Actual.Desde);
        Assert.Equal(7, p.Actual.Dias);
    }

    [Fact]
    public void El_mes_es_calendario_y_el_anterior_respeta_su_largo()
    {
        var p = NegocioCliente.Periodo(NegocioCliente.Mes, new DateOnly(2026, 3, 15))!;
        Assert.Equal(new DateOnly(2026, 3, 1), p.Actual.Desde);
        Assert.Equal(new DateOnly(2026, 3, 31), p.Actual.Hasta);
        Assert.Equal(new DateOnly(2026, 2, 1), p.Anterior.Desde);
        Assert.Equal(new DateOnly(2026, 2, 28), p.Anterior.Hasta);
    }

    [Fact]
    public void El_dia_se_compara_con_ayer()
    {
        var p = NegocioCliente.Periodo(NegocioCliente.Dia, new DateOnly(2026, 1, 1))!;
        Assert.Equal(new DateOnly(2025, 12, 31), p.Anterior.Desde);
        Assert.Equal(1, p.Actual.Dias);
    }

    [Fact]
    public void Un_periodo_desconocido_no_existe()
    {
        Assert.Null(NegocioCliente.Periodo("anio", new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Contar_agrupa_fallidos_con_devueltos_y_todo_lo_no_terminado_como_en_curso()
    {
        var c = NegocioCliente.Contar([
            EstadoPedido.Entregado, EstadoPedido.Fallido, EstadoPedido.Devuelto, EstadoPedido.Cancelado,
            EstadoPedido.Borrador, EstadoPedido.Confirmado, EstadoPedido.EnRuta, EstadoPedido.Reprogramado,
        ]);
        Assert.Equal(8, c.Total);
        Assert.Equal(1, c.Entregados);
        Assert.Equal(2, c.Fallidos);
        Assert.Equal(1, c.Cancelados);
        Assert.Equal(4, c.EnCurso);
    }

    [Fact]
    public void El_estado_de_cuenta_acumula_facturas_menos_pagos_desde_el_saldo_inicial()
    {
        var movimientos = NegocioCliente.EstadoDeCuenta(
            saldoInicial: 1000m,
            cargos:
            [
                new(10, new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), 5000m),
                new(11, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 30), 3000m),
            ],
            abonos:
            [
                new(1, new DateOnly(2026, 9, 15), "transferencia", null, 6000m),
                new(2, new DateOnly(2026, 9, 20), "efectivo", "cargado dos veces", -500m),
            ]);

        Assert.Equal(["factura", "pago", "pago", "factura"], movimientos.Select(m => m.Tipo));
        Assert.Equal([6000m, 0m, 500m, 3500m], movimientos.Select(m => m.Saldo));
        // Saldo final = inicial + facturado − pagado, el mismo criterio que saldo_cliente().
        Assert.Equal(1000m + 8000m - 5500m, movimientos[^1].Saldo);
        Assert.StartsWith("Corrección de pago", movimientos[2].Descripcion);
    }

    [Fact]
    public void Sin_movimientos_el_estado_de_cuenta_esta_vacio()
    {
        Assert.Empty(NegocioCliente.EstadoDeCuenta(250m, [], []));
    }
}
