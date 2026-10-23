using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Tests.BaseDeDatos;

// Cierre de ciclo sin actor, como lo llama Servicios/CierreCiclosAutomatico.cs: emite, no repite y no
// se frena por ajustes sin aprobar.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class CierreDeCiclosTests(BaseDePrueba baseDatos)
{
    private static readonly DateOnly FinDeSeptiembre = new(2026, 9, 30);
    private static readonly DateOnly PrimeroDeOctubre = new(2026, 10, 1);

    private async Task<Pedido> PedidoConItemAsync(decimal? monto, string estado, string tipo = "pedido")
    {
        var pedido = await baseDatos.PedidoAsync();
        await ItemAsync(pedido.Id, monto, estado, tipo);
        return pedido;
    }

    private async Task ItemAsync(long pedidoId, decimal? monto, string estado, string tipo)
    {
        await using var db = baseDatos.CrearContexto();
        db.FacturaItems.Add(new FacturaItem
        {
            PedidoId = pedidoId, Tipo = tipo, Descripcion = "Envío de prueba", Monto = monto, Estado = estado,
            CreadoEn = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<ResultadoCierreCliente>> CerrarAsync(int clienteId)
    {
        await using var db = baseDatos.CrearContexto();
        return await new CuentaCorrienteService(db).CerrarCiclosAsync(
            PrimeroDeOctubre, clienteId, previsualizar: false, actor: null, CancellationToken.None);
    }

    [Fact]
    public async Task El_cierre_sin_actor_emite_la_factura_del_periodo_vencido()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");

        var resultado = Assert.Single(await CerrarAsync(pedido.ClienteId));

        Assert.NotNull(resultado.FacturaId);
        Assert.Equal(FinDeSeptiembre, resultado.PeriodoHasta);
        Assert.Equal(1000m, resultado.Total);
        await using var db = baseDatos.CrearContexto();
        var factura = await db.Facturas.SingleAsync(f => f.ClienteId == pedido.ClienteId);
        Assert.Null(factura.EmitidaPor);
        Assert.Equal(new DateOnly(2026, 10, 10), factura.FechaVencimiento); // mensual: cierre + 10 días
        Assert.Equal(factura.Id, (await db.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id)).FacturaId);
    }

    [Fact]
    public async Task Repetir_el_cierre_no_emite_una_segunda_factura()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await CerrarAsync(pedido.ClienteId);

        var segunda = await CerrarAsync(pedido.ClienteId);

        Assert.DoesNotContain(segunda, r => r.FacturaId is not null);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(1, await db.Facturas.CountAsync(f => f.ClienteId == pedido.ClienteId));
    }

    [Fact]
    public async Task Un_ajuste_sin_aprobar_no_frena_la_factura_y_queda_para_la_siguiente()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await ItemAsync(pedido.Id, null, "pendiente", "ajuste");

        var resultado = Assert.Single(await CerrarAsync(pedido.ClienteId));

        Assert.NotNull(resultado.FacturaId);
        Assert.Equal(1000m, resultado.Total);
        Assert.Equal(1, resultado.AjustesPendientes);
        await using var db = baseDatos.CrearContexto();
        var ajuste = await db.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id && i.Tipo == "ajuste");
        Assert.Null(ajuste.FacturaId);
    }

    [Fact]
    public async Task Un_cliente_sin_nada_para_facturar_no_recibe_factura()
    {
        var cliente = await baseDatos.ClienteAsync();

        var resultados = await CerrarAsync(cliente.Id);

        Assert.DoesNotContain(resultados, r => r.FacturaId is not null);
        await using var db = baseDatos.CrearContexto();
        Assert.Equal(0, await db.Facturas.CountAsync(f => f.ClienteId == cliente.Id));
    }

    // Dar de baja a un cliente no le perdona lo que ya se le entregó.
    [Fact]
    public async Task Un_cliente_dado_de_baja_con_items_aprobados_recibe_su_factura()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await DarDeBajaAsync(pedido.ClienteId);

        var resultado = Assert.Single(await CerrarAsync(pedido.ClienteId));

        Assert.NotNull(resultado.FacturaId);
        Assert.Equal(1000m, resultado.Total);
        await using var db = baseDatos.CrearContexto();
        var factura = await db.Facturas.SingleAsync(f => f.ClienteId == pedido.ClienteId);
        Assert.Equal(factura.Id, (await db.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id)).FacturaId);
    }

    [Fact]
    public async Task Un_cliente_dado_de_baja_sin_nada_aprobado_no_aparece_en_el_cierre()
    {
        var pedido = await PedidoConItemAsync(null, "pendiente", "ajuste");
        await DarDeBajaAsync(pedido.ClienteId);

        Assert.Empty(await CerrarAsync(pedido.ClienteId));
    }

    [Fact]
    public async Task Un_cliente_dado_de_baja_ya_facturado_no_vuelve_a_aparecer()
    {
        var pedido = await PedidoConItemAsync(1000m, "aprobado");
        await DarDeBajaAsync(pedido.ClienteId);
        await CerrarAsync(pedido.ClienteId);

        Assert.Empty(await CerrarAsync(pedido.ClienteId));
    }

    private Task DarDeBajaAsync(int clienteId) =>
        baseDatos.EjecutarAsync($"update clientes set activo = false where id = {clienteId}");

    // Una corrida sobre TODOS los clientes, como la automática. La base es compartida: se cierra a una
    // fecha vieja, anterior a cualquier ítem de las demás pruebas, para no facturarles nada a ellas.
    private static readonly DateOnly PrimeroDeFebreroDe2020 = new(2020, 2, 1);

    private async Task<Pedido> PedidoConItemViejoAsync()
    {
        var pedido = await baseDatos.PedidoAsync();
        await using var db = baseDatos.CrearContexto();
        db.FacturaItems.Add(new FacturaItem
        {
            PedidoId = pedido.Id, Tipo = "pedido", Descripcion = "Envío de prueba", Monto = 1000m, Estado = "aprobado",
            CreadoEn = new DateTimeOffset(2020, 1, 10, 15, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        return pedido;
    }

    private async Task<List<ResultadoCierreCliente>> CerrarTodosAsync()
    {
        await using var db = baseDatos.CrearContexto();
        return await new CuentaCorrienteService(db).CerrarCiclosAsync(
            PrimeroDeFebreroDe2020, clienteId: null, previsualizar: false, actor: null, CancellationToken.None);
    }

    /// <summary>Un trigger que rechaza la escritura de un solo cliente: lo que haría una fila que viola una
    /// regla de la base. `enLaFactura` falla al insertar la factura; si no, al asignarle sus ítems, con
    /// la factura ya insertada en la transacción. Devuelve la sentencia que lo saca.</summary>
    private async Task<string> RomperCierreDeAsync(Pedido pedido, bool enLaFactura)
    {
        var nombre = $"trg_prueba_cierre_roto_{pedido.ClienteId}";
        await baseDatos.EjecutarAsync("""
            create or replace function fn_prueba_cierre_roto() returns trigger language plpgsql as $$
            begin
              raise exception 'Falla provocada por la prueba';
            end $$
            """);
        await baseDatos.EjecutarAsync(enLaFactura
            ? $"create trigger {nombre} before insert on facturas for each row when (new.cliente_id = {pedido.ClienteId}) execute function fn_prueba_cierre_roto()"
            : $"create trigger {nombre} before update on factura_items for each row when (new.pedido_id = {pedido.Id}) execute function fn_prueba_cierre_roto()");
        return $"drop trigger {nombre} on {(enLaFactura ? "facturas" : "factura_items")}";
    }

    // Antes, el primer cliente que fallaba abortaba la corrida entera, y como la corrida automática
    // reintenta siempre igual, ese cliente dejaba sin facturar a todos los que venían después.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Un_cliente_que_falla_se_omite_y_los_siguientes_se_facturan_igual(bool fallaEnLaFactura)
    {
        var roto = await PedidoConItemViejoAsync();
        var sano = await PedidoConItemViejoAsync();
        Assert.True(roto.ClienteId < sano.ClienteId); // el que falla va primero en la corrida
        var arreglar = await RomperCierreDeAsync(roto, fallaEnLaFactura);
        try
        {
            var resultados = await CerrarTodosAsync();

            var omitido = Assert.Single(resultados, r => r.ClienteId == roto.ClienteId);
            Assert.Null(omitido.FacturaId);
            Assert.Equal(CuentaCorrienteService.OmitidoPorError, omitido.Omitido);
            var facturado = Assert.Single(resultados, r => r.ClienteId == sano.ClienteId);
            Assert.NotNull(facturado.FacturaId);
            Assert.Null(facturado.Omitido);
            Assert.Equal(1000m, facturado.Total);
            Assert.Equal(resultados.Select(r => r.ClienteId).Order(), resultados.Select(r => r.ClienteId));

            await using var db = baseDatos.CrearContexto();
            Assert.Equal(0, await db.Facturas.CountAsync(f => f.ClienteId == roto.ClienteId));
            Assert.Null((await db.FacturaItems.SingleAsync(i => i.PedidoId == roto.Id)).FacturaId);
            var factura = await db.Facturas.SingleAsync(f => f.ClienteId == sano.ClienteId);
            Assert.Equal(factura.Id, (await db.FacturaItems.SingleAsync(i => i.PedidoId == sano.Id)).FacturaId);
        }
        finally
        {
            // La base es de toda la colección: el trigger se saca aunque una aserción falle.
            await baseDatos.EjecutarAsync(arreglar);
        }

        // Nada quedó a medias: resuelta la causa, la corrida siguiente le emite su factura.
        var reintento = Assert.Single(await CerrarTodosAsync(), r => r.ClienteId == roto.ClienteId);
        Assert.NotNull(reintento.FacturaId);
        Assert.Equal(1000m, reintento.Total);
    }

    /// <summary>Lo mismo que RomperCierreDeAsync, pero con una falla que no es de los datos del cliente.
    /// `cortandoLaConexion` mata el backend en plena escritura (lo que se ve cuando la base se cae o se
    /// corta la red); si no, la base rechaza con una clase de SQLSTATE que no es de datos (53300,
    /// demasiadas conexiones). Devuelve la sentencia que lo saca.</summary>
    private async Task<string> RomperLaBaseAlCerrarAAsync(Pedido pedido, bool cortandoLaConexion)
    {
        var nombre = $"trg_prueba_base_rota_{pedido.ClienteId}";
        var funcion = cortandoLaConexion ? "fn_prueba_conexion_cortada" : "fn_prueba_base_saturada";
        await baseDatos.EjecutarAsync(cortandoLaConexion
            ? """
              create or replace function fn_prueba_conexion_cortada() returns trigger language plpgsql as $$
              begin
                perform pg_terminate_backend(pg_backend_pid());
                perform pg_sleep(30); -- la baja llega en el primer punto de interrupción
                return new;
              end $$
              """
            : """
              create or replace function fn_prueba_base_saturada() returns trigger language plpgsql as $$
              begin
                raise exception 'Falla provocada por la prueba' using errcode = '53300';
              end $$
              """);
        await baseDatos.EjecutarAsync(
            $"create trigger {nombre} before insert on facturas for each row when (new.cliente_id = {pedido.ClienteId}) execute function {funcion}()");
        return $"drop trigger {nombre} on facturas";
    }

    // Aislar a un cliente es para lo que falla por SUS datos. Si lo que falla es la base, tragarlo
    // devolvería una lista de clientes "omitidos" y nadie se enteraría de que la corrida no corrió.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Una_falla_que_no_es_de_los_datos_del_cliente_corta_la_corrida(bool cortandoLaConexion)
    {
        var pedido = await PedidoConItemViejoAsync();
        var arreglar = await RomperLaBaseAlCerrarAAsync(pedido, cortandoLaConexion);
        try
        {
            await using var db = baseDatos.CrearContexto();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => new CuentaCorrienteService(db).CerrarCiclosAsync(
                PrimeroDeFebreroDe2020, pedido.ClienteId, previsualizar: false, actor: null, CancellationToken.None));
            Assert.IsNotType<OperationCanceledException>(error);
        }
        finally
        {
            await baseDatos.EjecutarAsync(arreglar);
        }

        await using var despues = baseDatos.CrearContexto();
        Assert.Equal(0, await despues.Facturas.CountAsync(f => f.ClienteId == pedido.ClienteId));
        Assert.Null((await despues.FacturaItems.SingleAsync(i => i.PedidoId == pedido.Id)).FacturaId);
    }

    // El host que se apaga o el request que se corta no es una falla del cliente que se estaba cerrando.
    [Fact]
    public async Task Una_cancelacion_en_medio_del_cierre_de_un_cliente_propaga()
    {
        var pedido = await PedidoConItemViejoAsync();
        var nombre = $"trg_prueba_cierre_lento_{pedido.ClienteId}";
        await baseDatos.EjecutarAsync("""
            create or replace function fn_prueba_cierre_lento() returns trigger language plpgsql as $$
            begin
              perform pg_sleep(30);
              return new;
            end $$
            """);
        await baseDatos.EjecutarAsync(
            $"create trigger {nombre} before insert on facturas for each row when (new.cliente_id = {pedido.ClienteId}) execute function fn_prueba_cierre_lento()");
        try
        {
            await using var db = baseDatos.CrearContexto();
            using var cancelacion = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CuentaCorrienteService(db).CerrarCiclosAsync(
                PrimeroDeFebreroDe2020, pedido.ClienteId, previsualizar: false, actor: null, cancelacion.Token));
        }
        finally
        {
            await baseDatos.EjecutarAsync($"drop trigger {nombre} on facturas");
        }

        await using var despues = baseDatos.CrearContexto();
        Assert.Equal(0, await despues.Facturas.CountAsync(f => f.ClienteId == pedido.ClienteId));
    }
}
