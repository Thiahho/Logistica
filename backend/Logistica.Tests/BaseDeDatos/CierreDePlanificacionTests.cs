using System.Data.Common;
using System.Net;
using System.Text;
using Logistica.Auth;
using Logistica.Controllers;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Logistica.Tests.BaseDeDatos;

// RF-17: al cerrar la planificación se cotiza y congela cada pedido en Borrador. El cierre resuelve las
// distancias en paralelo acotado y lee las tarifas de todos los pedidos de una vez; nada de eso puede
// cambiar un precio (P1: lo que se congela acá es lo que se factura) ni la respuesta ante una falla.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class CierreDePlanificacionTests(BaseDePrueba baseDatos)
{
    // Lejos de las fechas del resto de las pruebas: la zona es compartida y la tarifa general también.
    private static readonly DateOnly Dia = new(2032, 4, 4);

    // Dos tramos de km: el recargo depende de la distancia que devuelva el proveedor para cada pedido.
    private static readonly OpcionesPrecio Factores = new()
    {
        FactorUrgencia = 0.20m, TramosKm = [new TramoKm(0m, 0.05m), new TramoKm(10m, 0.15m)],
    };

    private static int _coordenada;

    /// <summary>OSRM de mentira: la distancia sale de las coordenadas pedidas (la misma para el mismo par,
    /// sea cual sea la instancia), y lleva la cuenta de cuántas consultas hubo y cuántas a la vez.</summary>
    private sealed class OsrmDePrueba(Func<int, TimeSpan>? demora = null, bool caido = false) : HttpMessageHandler
    {
        private readonly object _candado = new();
        private int _enVuelo;

        public int Llamadas { get; private set; }
        public int MaximoALaVez { get; private set; }
        public TaskCompletionSource PrimeraLlamada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            int numero;
            lock (_candado)
            {
                numero = ++Llamadas;
                MaximoALaVez = Math.Max(MaximoALaVez, ++_enVuelo);
            }
            PrimeraLlamada.TrySetResult();
            try
            {
                await Task.Delay(demora?.Invoke(numero) ?? TimeSpan.FromMilliseconds(40), ct);
                if (caido) throw new HttpRequestException("OSRM no responde.");

                var metros = 1000 + pedido.RequestUri!.AbsolutePath.Sum(c => (int)c) * 37 % 30000;
                var json = $$"""{"code":"Ok","routes":[{"geometry":{"coordinates":[[-58.4,-34.6]]},"distance":{{metros}},"duration":60}]}""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                lock (_candado) _enVuelo--;
            }
        }
    }

    /// <summary>Cuenta las consultas (SELECT) que salen hacia la base; las escrituras del cierre van aparte.</summary>
    private sealed class ContadorDeConsultas : DbCommandInterceptor
    {
        public int Consultas { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand comando, CommandEventData datos, InterceptionResult<DbDataReader> resultado, CancellationToken ct = default)
        {
            if (comando.CommandText.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase)) Consultas++;
            return base.ReaderExecutingAsync(comando, datos, resultado, ct);
        }
    }

    private static DistanciaService Distancias(HttpMessageHandler osrm) => new(
        new RuteoService(
            new HttpClient(osrm) { BaseAddress = new Uri("http://osrm.prueba/") },
            new MemoryCache(new MemoryCacheOptions()), Options.Create(new OpcionesRuteo())),
        Options.Create(new OpcionesDistancia()));

    private static PrecioService Precios(LogisticaDbContext db) => new(db, Options.Create(Factores));

    // El cierre de planificación solo cotiza: necesita precios y distancias, el resto no se toca.
    private static RutasController Rutas(LogisticaDbContext db, Guid usuarioId, HttpMessageHandler osrm) =>
        new RutasController(db, null!, Precios(db), Distancias(osrm), null!, null!, null!).ComoUsuario(usuarioId);

    private Task<int> ZonaAsync() => baseDatos.EscalarAsync<int>("select id from zonas where codigo = 'Z'");

    /// <summary>Una ubicación apta en un punto que ninguna otra prueba usa; sin coordenadas si se pide.</summary>
    private async Task<Ubicacion> UbicacionAsync(bool conCoordenadas = true)
    {
        var ubicacion = await baseDatos.UbicacionAsync();
        if (!conCoordenadas) return ubicacion;

        var n = Interlocked.Increment(ref _coordenada);
        await using var db = baseDatos.CrearContexto();
        var guardada = await db.Ubicaciones.SingleAsync(u => u.Id == ubicacion.Id);
        guardada.Lat = -34.5m - n * 0.003m;
        guardada.Lng = -58.4m - n * 0.002m;
        await db.SaveChangesAsync();
        return guardada;
    }

    private async Task TarifaPropiaAsync(int clienteId, decimal precio)
    {
        await using var db = baseDatos.CrearContexto();
        db.Tarifas.Add(new Tarifa
        {
            ClienteId = clienteId, ZonaId = await ZonaAsync(), TipoVehiculo = "camioneta", Precio = precio,
            VigenteDesde = Dia, CreadaEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Corre `prueba` con una tarifa general de un solo día en la zona compartida y el rango
    /// bronce con descuento; las dos filas son de toda la colección, así que quedan como estaban.</summary>
    private async Task ConTarifaGeneralAsync(Func<Task> prueba)
    {
        var descuentoAnterior = await baseDatos.EscalarAsync<decimal>("select descuento_pct from rangos where codigo = 'bronce'");
        var tarifa = new Tarifa
        {
            ZonaId = await ZonaAsync(), TipoVehiculo = "camioneta", Precio = 1777.77m,
            VigenteDesde = Dia, VigenteHasta = Dia, CreadaEn = DateTimeOffset.UtcNow,
        };
        try
        {
            await baseDatos.EjecutarAsync("update rangos set descuento_pct = 7.5 where codigo = 'bronce'");
            await using (var db = baseDatos.CrearContexto())
            {
                db.Tarifas.Add(tarifa);
                await db.SaveChangesAsync();
            }
            await prueba();
        }
        finally
        {
            await baseDatos.EjecutarAsync(FormattableString.Invariant(
                $"update rangos set descuento_pct = {descuentoAnterior} where codigo = 'bronce'"));
            if (tarifa.Id != 0) await baseDatos.EjecutarAsync($"delete from tarifas where id = {tarifa.Id}");
        }
    }

    private async Task<Pedido> BorradorAsync(
        int clienteId, Ubicacion? origen = null, Ubicacion? destino = null, Action<Pedido>? ajustar = null)
    {
        origen ??= await UbicacionAsync();
        destino ??= await UbicacionAsync();
        await using var db = baseDatos.CrearContexto();
        var pedido = new Pedido
        {
            ClienteId = clienteId,
            OrigenUbicacionId = origen.Id,
            DestinoUbicacionId = destino.Id,
            DestinatarioNombre = "Destinatario",
            DestinatarioTelefono = "1150000000",
            FechaEntrega = Dia,
            ZonaId = await ZonaAsync(),
        };
        ajustar?.Invoke(pedido);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    /// <summary>Un borrador de un cliente nuevo con tarifa propia: el caso común, sin tocar filas compartidas.</summary>
    private async Task<Pedido> BorradorConTarifaPropiaAsync(decimal precio = 1000m, Action<Pedido>? ajustar = null)
    {
        var cliente = await baseDatos.ClienteAsync();
        await TarifaPropiaAsync(cliente.Id, precio);
        return await BorradorAsync(cliente.Id, ajustar: ajustar);
    }

    /// <summary>Una ruta lista para cerrar la planificación (vehículo, repartidor y punto de partida), con
    /// una parada por destino.</summary>
    private async Task<Ruta> RutaListaAsync(IEnumerable<Pedido> pedidos)
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var origen = await baseDatos.UbicacionAsync();
        await using var db = baseDatos.CrearContexto();
        var vehiculo = new Vehiculo { Patente = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant() };
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync();
        var ruta = new Ruta
        {
            Fecha = Dia, VehiculoId = vehiculo.Id, RepartidorId = repartidor.Id, OrigenUbicacionId = origen.Id,
        };
        db.Rutas.Add(ruta);
        await db.SaveChangesAsync();

        var orden = 0;
        foreach (var deLaParada in pedidos.GroupBy(p => p.DestinoUbicacionId))
        {
            var parada = new RutaParada { RutaId = ruta.Id, UbicacionId = deLaParada.Key, Tipo = "entrega", Orden = ++orden };
            db.RutaParadas.Add(parada);
            await db.SaveChangesAsync();
            db.ParadaPedidos.AddRange(deLaParada.Select(p => new ParadaPedido { ParadaId = parada.Id, PedidoId = p.Id }));
            await db.SaveChangesAsync();
        }
        return ruta;
    }

    private async Task<IActionResult> CerrarAsync(
        long rutaId, Guid actor, HttpMessageHandler osrm, CancellationToken ct = default)
    {
        await using var db = baseDatos.CrearContexto();
        return await Rutas(db, actor, osrm).CerrarPlanificacion(rutaId, ct);
    }

    private async Task<Pedido> GuardadoAsync(long pedidoId)
    {
        await using var db = baseDatos.CrearContexto();
        return await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
    }

    private async Task<string> EstadoDeRutaAsync(long rutaId)
    {
        await using var db = baseDatos.CrearContexto();
        return (await db.Rutas.AsNoTracking().SingleAsync(r => r.Id == rutaId)).Estado;
    }

    /// <summary>El precio de un pedido cotizado solo, por el camino de siempre: su distancia y su
    /// CotizarAsync, uno por vez.</summary>
    private async Task<DesglosePrecio> CotizadoSoloAsync(long pedidoId)
    {
        await using var db = baseDatos.CrearContexto();
        var pedido = await db.Pedidos.AsNoTracking()
            .Include(p => p.OrigenUbicacion).Include(p => p.DestinoUbicacion).SingleAsync(p => p.Id == pedidoId);
        var distancia = await Distancias(new OsrmDePrueba(_ => TimeSpan.Zero)).ResolverAsync(
            pedido.OrigenUbicacion.Lat, pedido.OrigenUbicacion.Lng,
            pedido.DestinoUbicacion.Lat, pedido.DestinoUbicacion.Lng, pedido.KmManual);
        return await Precios(db).CotizarAsync(
            pedido.ClienteId, pedido.ZonaId!.Value, pedido.FechaEntrega, pedido.Urgente, pedido.Peajes,
            descuentoRuta: false, "camioneta", pedido.PrecioManual, distancia);
    }

    [Fact]
    public async Task Cada_pedido_queda_con_el_mismo_precio_que_si_se_cotizara_solo()
    {
        var actor = await baseDatos.UsuarioAsync();
        await ConTarifaGeneralAsync(async () =>
        {
            // Sin tarifa propia: precio de la lista general con descuento por rango, dos pedidos del mismo cliente.
            var conRango = await baseDatos.ClienteAsync();
            await baseDatos.EjecutarAsync($"update clientes set rango_calculado = 'bronce' where id = {conRango.Id}");
            // Precio vinculante del portal (B5): lo fijó un login del cliente y no se re-cotiza.
            var delPortal = await baseDatos.ClienteAsync();
            var login = await baseDatos.ClienteUsuarioAsync(delPortal.Id);
            var sinCoordenadas = await UbicacionAsync(conCoordenadas: false);
            var mismoOrigen = await UbicacionAsync();
            var mismoDestino = await UbicacionAsync();

            var cotizados = new List<Pedido>
            {
                await BorradorConTarifaPropiaAsync(1000.33m, p => { p.Urgente = true; p.Peajes = 150.55m; }),
                await BorradorAsync(conRango.Id, ajustar: p => p.Urgente = true),
                await BorradorAsync(conRango.Id, ajustar: p => p.KmManual = 12.5m),
                // Precio manual de administración (B9): es una base, sigue el camino normal sin tarifa.
                await BorradorAsync((await baseDatos.ClienteAsync()).Id, ajustar: p =>
                {
                    p.PrecioManual = 2500.10m; p.PrecioManualPor = actor.Id; p.PrecioManualEn = DateTimeOffset.UtcNow;
                    p.Urgente = true;
                }),
                await BorradorAsync((await baseDatos.ClienteAsync()).Id, sinCoordenadas, ajustar: p =>
                {
                    p.PrecioManual = 800m; p.PrecioManualPor = actor.Id; p.PrecioManualEn = DateTimeOffset.UtcNow;
                }),
            };
            // Dos pedidos con el mismo origen y destino, y varios más para pasar el tope de consultas a la vez.
            var repetido = await baseDatos.ClienteAsync();
            await TarifaPropiaAsync(repetido.Id, 903.07m);
            cotizados.Add(await BorradorAsync(repetido.Id, mismoOrigen, mismoDestino));
            cotizados.Add(await BorradorAsync(repetido.Id, mismoOrigen, mismoDestino, p => p.Urgente = true));
            for (var i = 0; i < 6; i++)
                cotizados.Add(await BorradorConTarifaPropiaAsync(700.01m + i * 111.11m, p => p.Urgente = i % 2 == 0));
            var vinculante = await BorradorAsync(delPortal.Id, ajustar: p =>
            {
                p.PrecioManual = 3100m; p.PrecioManualPor = login.Id; p.PrecioManualEn = DateTimeOffset.UtcNow;
            });

            var esperados = new Dictionary<long, DesglosePrecio>();
            foreach (var pedido in cotizados)
                esperados[pedido.Id] = await CotizadoSoloAsync(pedido.Id);
            var ruta = await RutaListaAsync([.. cotizados, vinculante]);

            // Las primeras consultas tardan más que las últimas: el orden en que terminan no es el de los pedidos.
            var osrm = new OsrmDePrueba(numero => TimeSpan.FromMilliseconds(Math.Max(5, 90 - numero * 12)));
            Assert.IsType<NoContentResult>(await CerrarAsync(ruta.Id, actor.Id, osrm));

            foreach (var pedido in cotizados)
            {
                var esperado = esperados[pedido.Id];
                var guardado = await GuardadoAsync(pedido.Id);
                Assert.Equal(EstadoPedido.EnRuta, guardado.Estado);
                Assert.Equal(esperado.PrecioBase, guardado.PrecioBase);
                Assert.Equal(esperado.RecargoKm, guardado.RecargoKm);
                Assert.Equal(
                    esperado.KmCobrados is { } km ? decimal.Round(km, 2, MidpointRounding.AwayFromZero) : null,
                    guardado.KmCobrados);
                Assert.Equal(esperado.KmFuente, guardado.KmFuente);
                Assert.Equal(esperado.RecargoUrgencia, guardado.RecargoUrgencia);
                Assert.Equal(esperado.DescuentoRuta, guardado.DescuentoRuta);
                Assert.Equal(esperado.DescuentoRango, guardado.DescuentoRango);
                Assert.Equal(esperado.Total, guardado.Total);
                Assert.NotNull(guardado.PrecioCongeladoEn);
            }
            var delVinculante = await GuardadoAsync(vinculante.Id);
            Assert.Equal(EstadoPedido.EnRuta, delVinculante.Estado);
            Assert.Equal(3100m, delVinculante.PrecioBase);
            Assert.Equal(3100m, delVinculante.Total);

            // La prueba cubre lo que dice cubrir: los tres orígenes del km, recargo por km y descuento por rango.
            Assert.Equal([null, "manual", "ruta"],esperados.Values.Select(e => e.KmFuente).Distinct().Order().ToArray());
            Assert.Contains(esperados.Values, e => e.RecargoKm > 0m);
            Assert.Equal(2, esperados.Values.Count(e => e.DescuentoRango > 0m));
        });
    }

    [Fact]
    public async Task Las_distancias_se_consultan_varias_a_la_vez_sin_pasar_el_tope_y_una_sola_vez_por_recorrido()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedidos = new List<Pedido>();
        for (var i = 0; i < 9; i++)
            pedidos.Add(await BorradorConTarifaPropiaAsync());
        // Mismo origen y destino que el primero: no es otra consulta.
        var primero = pedidos[0];
        await using (var db = baseDatos.CrearContexto())
        {
            var cliente = await baseDatos.ClienteAsync();
            await TarifaPropiaAsync(cliente.Id, 1200m);
            var repetido = new Pedido
            {
                ClienteId = cliente.Id, OrigenUbicacionId = primero.OrigenUbicacionId,
                DestinoUbicacionId = primero.DestinoUbicacionId, DestinatarioNombre = "Destinatario",
                DestinatarioTelefono = "1150000000", FechaEntrega = Dia, ZonaId = await ZonaAsync(),
            };
            db.Pedidos.Add(repetido);
            await db.SaveChangesAsync();
            pedidos.Add(repetido);
        }
        var ruta = await RutaListaAsync(pedidos);
        var osrm = new OsrmDePrueba(_ => TimeSpan.FromMilliseconds(80));

        Assert.IsType<NoContentResult>(await CerrarAsync(ruta.Id, actor.Id, osrm));

        Assert.Equal(9, osrm.Llamadas);
        Assert.True(osrm.MaximoALaVez > 1, $"Las consultas fueron de a una (máximo a la vez: {osrm.MaximoALaVez}).");
        Assert.True(osrm.MaximoALaVez <= DistanciaService.MaxConsultasALaVez,
            $"Hubo {osrm.MaximoALaVez} consultas a la vez; el tope es {DistanciaService.MaxConsultasALaVez}.");
    }

    [Fact]
    public async Task Las_consultas_a_la_base_no_crecen_con_la_cantidad_de_pedidos()
    {
        var actor = await baseDatos.UsuarioAsync();
        await ConTarifaGeneralAsync(async () =>
        {
            // Sin tarifa propia: cada pedido recorre el camino largo (tarifa, tarifa propia, cliente, rangos).
            async Task<int> ConsultasAlCerrarAsync(int cantidad)
            {
                var pedidos = new List<Pedido>();
                for (var i = 0; i < cantidad; i++)
                    pedidos.Add(await BorradorAsync((await baseDatos.ClienteAsync()).Id));
                var ruta = await RutaListaAsync(pedidos);

                var contador = new ContadorDeConsultas();
                await using var db = baseDatos.CrearContexto(contador);
                Assert.IsType<NoContentResult>(await Rutas(db, actor.Id, new OsrmDePrueba(_ => TimeSpan.Zero))
                    .CerrarPlanificacion(ruta.Id, CancellationToken.None));
                return contador.Consultas;
            }

            var conDos = await ConsultasAlCerrarAsync(2);
            var conSeis = await ConsultasAlCerrarAsync(6);

            Assert.Equal(conDos, conSeis);
        });
    }

    [Fact]
    public async Task Un_pedido_sin_tarifa_rechaza_el_cierre_entero_y_no_confirma_ninguno()
    {
        var actor = await baseDatos.UsuarioAsync();
        var conTarifa = await BorradorConTarifaPropiaAsync();
        var sinTarifa = await BorradorAsync((await baseDatos.ClienteAsync()).Id);
        var ruta = await RutaListaAsync([conTarifa, sinTarifa]);

        var respuesta = Assert.IsType<BadRequestObjectResult>(await CerrarAsync(ruta.Id, actor.Id, new OsrmDePrueba()));

        Assert.StartsWith($"Pedido {sinTarifa.Id}: No hay tarifa vigente para la zona", Assert.IsType<string>(respuesta.Value));
        Assert.Equal("planificada", await EstadoDeRutaAsync(ruta.Id));
        var guardado = await GuardadoAsync(conTarifa.Id);
        Assert.Equal(EstadoPedido.Borrador, guardado.Estado);
        Assert.Null(guardado.Total);
    }

    [Fact]
    public async Task Un_pedido_sin_zona_rechaza_el_cierre_entero()
    {
        var actor = await baseDatos.UsuarioAsync();
        var sinZona = await BorradorConTarifaPropiaAsync(ajustar: p => p.ZonaId = null);
        var ruta = await RutaListaAsync([sinZona]);

        var respuesta = Assert.IsType<BadRequestObjectResult>(await CerrarAsync(ruta.Id, actor.Id, new OsrmDePrueba()));

        Assert.Equal($"El pedido {sinZona.Id} no tiene zona resuelta; no se puede cotizar.", respuesta.Value);
        Assert.Equal("planificada", await EstadoDeRutaAsync(ruta.Id));
    }

    // DistanciaService no tira: sin proveedor el km sale de la línea recta y el cierre sigue.
    [Fact]
    public async Task Con_el_proveedor_de_distancias_caido_el_cierre_sigue_con_la_linea_recta()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedidos = new List<Pedido> { await BorradorConTarifaPropiaAsync(), await BorradorConTarifaPropiaAsync() };
        var ruta = await RutaListaAsync(pedidos);

        Assert.IsType<NoContentResult>(await CerrarAsync(ruta.Id, actor.Id, new OsrmDePrueba(caido: true)));

        foreach (var pedido in pedidos)
        {
            var guardado = await GuardadoAsync(pedido.Id);
            Assert.Equal(EstadoPedido.EnRuta, guardado.Estado);
            Assert.Equal("recta", guardado.KmFuente);
            Assert.NotNull(guardado.Total);
        }
    }

    [Fact]
    public async Task Cancelar_el_request_mientras_se_consultan_las_distancias_corta_el_cierre()
    {
        var actor = await baseDatos.UsuarioAsync();
        var pedidos = new List<Pedido>();
        for (var i = 0; i < 6; i++)
            pedidos.Add(await BorradorConTarifaPropiaAsync());
        var ruta = await RutaListaAsync(pedidos);
        var osrm = new OsrmDePrueba(_ => TimeSpan.FromSeconds(30));
        using var cancelacion = new CancellationTokenSource();

        var cierre = CerrarAsync(ruta.Id, actor.Id, osrm, cancelacion.Token);
        await osrm.PrimeraLlamada.Task.WaitAsync(TimeSpan.FromSeconds(20));
        cancelacion.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cierre);
        Assert.Equal("planificada", await EstadoDeRutaAsync(ruta.Id));
        foreach (var pedido in pedidos)
            Assert.Equal(EstadoPedido.Borrador, (await GuardadoAsync(pedido.Id)).Estado);
    }
}
