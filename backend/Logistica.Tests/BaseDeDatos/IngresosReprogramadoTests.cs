using System.Globalization;
using System.Text;
using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Entidades;
using Microsoft.AspNetCore.Mvc;

namespace Logistica.Tests.BaseDeDatos;

// F-1: un reprogramado conserva la fila de parada_pedidos de la ruta donde falló (D13). El ingreso del
// pedido es de la ruta donde se entregó (su última parada), no de las dos.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class IngresosReprogramadoTests(BaseDePrueba baseDatos)
{
    // Fechas que ninguna otra prueba usa: el export filtra por rango.
    private static readonly DateOnly DiaFallo = new(2031, 3, 10);
    private static readonly DateOnly DiaEntrega = new(2031, 3, 11);

    /// <summary>Pedido entregado (total 1000) que falló en una ruta y se entregó en otra, ambas cerradas
    /// y sin costos, del mismo repartidor.</summary>
    private async Task<(long RutaFallo, long RutaEntrega, Guid Repartidor)> ReprogramadoEntregadoAsync()
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var pedido = await baseDatos.PedidoAsync(EstadoPedido.Entregado);
        var rutaFallo = await RutaCerradaAsync(repartidor.Id, DiaFallo, pedido, "fallida");
        var rutaEntrega = await RutaCerradaAsync(repartidor.Id, DiaEntrega, pedido, "completada");
        return (rutaFallo, rutaEntrega, repartidor.Id);
    }

    private async Task<long> RutaCerradaAsync(Guid repartidorId, DateOnly fecha, Pedido pedido, string estadoParada)
    {
        var ruta = await baseDatos.RutaAsync(repartidorId);
        var parada = await baseDatos.ParadaAsync(ruta.Id, pedido.DestinoUbicacionId);
        await using (var db = baseDatos.CrearContexto())
        {
            db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedido.Id });
            await db.SaveChangesAsync();
        }
        await baseDatos.EjecutarAsync($"update ruta_paradas set estado = '{estadoParada}' where id = {parada.Id}");
        await baseDatos.EjecutarAsync(
            $"update rutas set estado = 'cerrada', fecha = '{fecha:yyyy-MM-dd}', cerrada_en = now() where id = {ruta.Id}");
        return ruta.Id;
    }

    [Fact]
    public async Task El_export_de_resultados_cuenta_el_ingreso_solo_en_la_ruta_que_entrego()
    {
        var (rutaFallo, rutaEntrega, _) = await ReprogramadoEntregadoAsync();

        await using var db = baseDatos.CrearContexto();
        var archivo = Assert.IsType<FileContentResult>(
            await new ExportarController(db).Resultados(DiaFallo, DiaEntrega, CancellationToken.None));

        var ingresos = Encoding.UTF8.GetString(archivo.FileContents).TrimStart('﻿')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(l => l.TrimEnd('\r').Split(';'))
            .ToDictionary(c => long.Parse(c[0]), c => decimal.Parse(c[2], CultureInfo.InvariantCulture));
        Assert.Equal(0m, ingresos[rutaFallo]);
        Assert.Equal(1000m, ingresos[rutaEntrega]);
    }

    [Fact]
    public async Task El_tablero_cuenta_el_ingreso_solo_en_la_ruta_que_entrego()
    {
        var (_, _, repartidor) = await ReprogramadoEntregadoAsync();
        var actor = await baseDatos.UsuarioAsync();

        await using var db = baseDatos.CrearContexto();
        var respuesta = Assert.IsType<OkObjectResult>(await new TableroController(db).ComoUsuario(actor.Id)
            .Obtener(DiaFallo, DiaEntrega, clienteId: null, zonaId: null, vehiculoId: null, repartidorId: repartidor,
                CancellationToken.None));
        var tablero = Assert.IsType<TableroController.Tablero>(respuesta.Value);

        Assert.Equal(1000m, tablero.MargenTotal);
        Assert.Equal(0m, tablero.PorDia.Single(d => d.Fecha == DiaFallo).Margen);
        Assert.Equal(1000m, tablero.PorDia.Single(d => d.Fecha == DiaEntrega).Margen);
    }
}
