using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Una parada tal como la carga el portal o el BackOffice.</summary>
public record ParadaViajeEntrada(
    long DestinoUbicacionId, string DestinatarioNombre, string DestinatarioTelefono,
    int Bultos, string? Observaciones);

/// <summary>Una parada ya ubicada, en el orden sugerido. `Indices` son las posiciones (en el orden en que
/// se cargaron) de los envíos que van a esta dirección: más de uno si se repite la dirección (RF-14).</summary>
public record ParadaPrevista(
    long UbicacionId, string CalleNumero, string? Localidad, decimal? Lat, decimal? Lng,
    bool DireccionApta, List<int> Indices);

public record PrevisualizacionViaje(
    OrigenViaje Origen, List<ParadaPrevista> Paradas, decimal Km, string FuenteKm,
    List<PuntoRuta>? Linea, List<long> SinCoordenadas);

public record OrigenViaje(long UbicacionId, string Nombre, string CalleNumero, decimal? Lat, decimal? Lng);

public record ParadaViajeDetalle(
    int Orden, long PedidoId, string DestinatarioNombre, string DestinatarioTelefono,
    string CalleNumero, string? Localidad, decimal? Lat, decimal? Lng, int Bultos, string? Observaciones,
    string Estado, bool EnRuta, decimal? Precio);

public record ViajeDetalle(
    long Id, int ClienteId, string ClienteRazonSocial, DateOnly FechaEntrega, string? TipoVehiculo,
    string Estado, long? RutaId, string? RutaEstado, decimal? KmEstimados, string CreadoPor, DateTimeOffset CreadoEn,
    string? Observaciones, OrigenViaje Origen, List<ParadaViajeDetalle> Paradas,
    int Entregadas, int Fallidas, decimal? Total, List<PuntoRuta>? Linea);

public record ViajeResumen(
    long Id, int ClienteId, string ClienteRazonSocial, DateOnly FechaEntrega, string Estado,
    int Paradas, int Entregadas, int Fallidas, decimal? KmEstimados, string CreadoPor, DateTimeOffset CreadoEn);

/// <summary>Datos comunes de un viaje nuevo. Uno de los dos creadores, nunca los dos (ck_viajes_creador).</summary>
public record NuevoViaje(
    int ClienteId, DateOnly FechaEntrega, bool Urgente, string? TipoVehiculo, string? Observaciones,
    Guid? ClienteUsuarioId, Guid? UsuarioId);

public record ViajeCreado(long Id, long? RutaId, List<long> PedidoIds, int ParadasFueraDeRuta);

/// <summary>
/// Viajes: un envío con varias paradas de una sola empresa cliente, con su ruta propuesta por el
/// sistema (Dominio/OrdenParadas) y revisada por Operación. Lo comparten el portal
/// (MiCuentaController) y el BackOffice (ViajesController): las validaciones propias de cada puerta
/// (corte horario, deuda, fecha) y el precio quedan en el controller; acá, lo que es igual en los dos.
/// </summary>
public class ViajeService(LogisticaDbContext db, OrigenRutaService origenes, RuteoService ruteo)
{
    /// <summary>Estado que ve el usuario: cancelado, o el de su ruta (sin_ruta si todavía no tiene).</summary>
    public static string EstadoVisible(string estado, string? rutaEstado) =>
        estado == EstadosViaje.Cancelado ? "cancelado" : rutaEstado switch
        {
            null => "sin_ruta",
            "planificada" => "planificado",
            "en_curso" => "en_curso",
            _ => "cerrado",
        };

    /// <summary>Las ubicaciones de las paradas, con su zona y si la dirección es apta para una ruta
    /// (misma regla que trg_bloquear_direccion_dudosa). Error si alguna no existe o no tiene zona.</summary>
    private async Task<(Dictionary<long, (Ubicacion U, bool Apta)>? Ubicaciones, string? Error)> CargarUbicacionesAsync(
        IReadOnlyList<ParadaViajeEntrada> paradas, CancellationToken ct)
    {
        var ids = paradas.Select(p => p.DestinoUbicacionId).Distinct().ToList();
        var filas = await db.Ubicaciones.Include(u => u.Localidad)
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { U = u, Apta = LogisticaDbContext.UbicacionApta(u.Id) })
            .ToListAsync(ct);
        if (filas.Count != ids.Count) return (null, "Alguna de las direcciones de destino no existe.");

        var sinZona = filas.FirstOrDefault(f => f.U.Localidad?.ZonaId is null);
        if (sinZona is not null)
            return (null, $"La localidad de {sinZona.U.CalleNumero} no tiene zona asignada; no se puede cotizar.");

        return (filas.ToDictionary(f => f.U.Id, f => (f.U, f.Apta)), null);
    }

    /// <summary>Valida lo que es igual para las dos puertas: cantidad de paradas y datos de cada una.</summary>
    /// `minimo`: 1 para previsualizar (la carga del portal revisa también un envío de una sola parada
    /// antes de confirmarlo), 2 para crear un viaje (una sola parada se carga como envío).
    public static string? ValidarParadas(IReadOnlyList<ParadaViajeEntrada> paradas, int maximo, int minimo = 2)
    {
        if (paradas.Count < minimo)
            return minimo == 1 ? "Cargá al menos una parada." : "Un viaje necesita al menos dos paradas; para una sola, cargá un envío.";
        if (paradas.Count > maximo) return $"Un viaje admite hasta {maximo} paradas.";
        for (var i = 0; i < paradas.Count; i++)
        {
            var p = paradas[i];
            if (string.IsNullOrWhiteSpace(p.DestinatarioNombre)) return $"Parada {i + 1}: falta el destinatario.";
            if (string.IsNullOrWhiteSpace(p.DestinatarioTelefono)) return $"Parada {i + 1}: falta el teléfono.";
            if (p.Bultos is < 1 or > 999) return $"Parada {i + 1}: los bultos deben estar entre 1 y 999.";
        }
        return null;
    }

    /// <summary>Orden sugerido y recorrido, sin guardar nada. El recorrido por calle viene del proveedor
    /// de ruteo; si no responde, km en línea recta y sin línea (el mapa dibuja rectas).</summary>
    public async Task<(PrevisualizacionViaje? Previa, string? Error)> PrevisualizarAsync(
        IReadOnlyList<ParadaViajeEntrada> paradas, CancellationToken ct)
    {
        var (ubicaciones, error) = await CargarUbicacionesAsync(paradas, ct);
        if (ubicaciones is null) return (null, error);

        var deposito = await origenes.PrincipalParaPedidosAsync(ct);
        var origen = new OrigenViaje(deposito.UbicacionId, deposito.Nombre, deposito.CalleNumero, deposito.Lat, deposito.Lng);

        var entrada = paradas.Select(p => ubicaciones[p.DestinoUbicacionId].U)
            .Select(u => new OrdenParadas.Parada(u.Id, u.Lat, u.Lng)).ToList();
        // Sin coordenadas del depósito se parte de la primera parada ubicada: el orden relativo
        // sigue sirviendo aunque falte el primer tramo.
        var puntoOrigen = deposito.Lat is not null && deposito.Lng is not null
            ? new OrdenParadas.Punto(deposito.Lat.Value, deposito.Lng.Value)
            : entrada.Where(p => p.Lat is not null).Select(p => new OrdenParadas.Punto(p.Lat!.Value, p.Lng!.Value)).FirstOrDefault()
              ?? new OrdenParadas.Punto(0, 0);
        var orden = OrdenParadas.Sugerir(puntoOrigen, entrada);

        var previstas = orden.Ubicaciones.Select(id =>
        {
            var (u, apta) = ubicaciones[id];
            var indices = paradas.Select((p, i) => (p, i)).Where(x => x.p.DestinoUbicacionId == id).Select(x => x.i).ToList();
            return new ParadaPrevista(u.Id, u.CalleNumero, u.Localidad?.Nombre, u.Lat, u.Lng, apta, indices);
        }).ToList();

        var (km, fuente, linea) = await RecorridoAsync(origen, previstas.Select(p => (p.Lat, p.Lng)).ToList(), orden.MetrosLineaRecta, ct);
        return (new PrevisualizacionViaje(origen, previstas, km, fuente, linea, orden.SinCoordenadas.ToList()), null);
    }

    private async Task<(decimal Km, string Fuente, List<PuntoRuta>? Linea)> RecorridoAsync(
        OrigenViaje origen, IReadOnlyList<(decimal? Lat, decimal? Lng)> paradas, int metrosRecta, CancellationToken ct)
    {
        var puntos = new List<PuntoRuta>();
        if (origen.Lat is not null && origen.Lng is not null) puntos.Add(new PuntoRuta(origen.Lat.Value, origen.Lng.Value));
        puntos.AddRange(paradas.Where(p => p.Lat is not null && p.Lng is not null).Select(p => new PuntoRuta(p.Lat!.Value, p.Lng!.Value)));
        if (puntos.Count > RuteoService.MaxPuntosOrs) puntos = puntos.Take(RuteoService.MaxPuntosOrs).ToList();

        var recorrido = await ruteo.TrazarAsync(puntos, ct);
        return recorrido is not null
            ? (Math.Round(recorrido.DistanciaMetros / 1000m, 1), "calle", recorrido.Linea.ToList())
            : (Math.Round(metrosRecta / 1000m, 1), "recta", null);
    }

    /// <summary>
    /// Crea el viaje, un pedido por parada (Borrador, en el orden recibido) y la ruta propuesta en
    /// `planificada`, todo en una transacción. Las paradas con dirección dudosa no entran a la ruta
    /// (trg_bloquear_direccion_dudosa las frenaría): quedan en el viaje para que Operación corrija la
    /// dirección y las sume. `precios` va alineado con `paradas` (null = sin precio vinculante: lo fija
    /// la ruta al cerrar la planificación). `km` es el del recorrido previsto.
    /// </summary>
    public async Task<(ViajeCreado? Creado, string? Error)> CrearAsync(
        NuevoViaje datos, IReadOnlyList<ParadaViajeEntrada> paradas, IReadOnlyList<DesglosePrecio?> precios,
        decimal? km, string origenCarga, CancellationToken ct)
    {
        var (ubicaciones, error) = await CargarUbicacionesAsync(paradas, ct);
        if (ubicaciones is null) return (null, error);
        var deposito = await origenes.PrincipalParaPedidosAsync(ct);

        ViajeCreado? creado = null;
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            // Todo se arma dentro del delegate: si la estrategia reintenta, parte de cero.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.PublicarActorAsync(datos.UsuarioId,
                origenCarga == "portal" ? "Alta de viaje desde el portal del cliente" : "Alta de viaje", ct);

            var viaje = new Viaje
            {
                ClienteId = datos.ClienteId,
                FechaEntrega = datos.FechaEntrega,
                OrigenUbicacionId = deposito.UbicacionId,
                TipoVehiculo = datos.TipoVehiculo,
                KmEstimados = km,
                CreadoPorClienteUsuarioId = datos.ClienteUsuarioId,
                CreadoPorUsuarioId = datos.UsuarioId,
                Observaciones = string.IsNullOrWhiteSpace(datos.Observaciones) ? null : datos.Observaciones.Trim(),
                CreadoEn = DateTimeOffset.UtcNow,
            };
            db.Viajes.Add(viaje);
            await db.SaveChangesAsync(ct);

            var pedidos = paradas.Select((p, i) =>
            {
                var u = ubicaciones[p.DestinoUbicacionId].U;
                var pedido = new Pedido
                {
                    ClienteId = datos.ClienteId,
                    OrigenUbicacionId = deposito.UbicacionId,
                    DestinoUbicacionId = u.Id,
                    DestinatarioNombre = p.DestinatarioNombre.Trim(),
                    DestinatarioTelefono = p.DestinatarioTelefono.Trim(),
                    Bultos = p.Bultos,
                    BultosDeclaradoCliente = origenCarga == "portal" ? p.Bultos : null,
                    FechaEntrega = datos.FechaEntrega,
                    Urgente = datos.Urgente,
                    ZonaId = u.Localidad!.ZonaId,
                    Estado = EstadoPedido.Borrador,
                    OrigenCarga = origenCarga,
                    Observaciones = string.IsNullOrWhiteSpace(p.Observaciones) ? null : p.Observaciones.Trim(),
                    CreadoEn = DateTimeOffset.UtcNow,
                    CreadoPorClienteUsuarioId = datos.ClienteUsuarioId,
                    ViajeId = viaje.Id,
                    OrdenEnViaje = i + 1,
                };
                // Precio vinculante del portal, igual que un envío suelto (B5 §1): el total queda como
                // precio_manual fijado por el login, y RutasController no lo vuelve a cotizar.
                if (precios[i] is { } d)
                {
                    pedido.PrecioBase = d.PrecioBase;
                    pedido.RecargoKm = d.RecargoKm;
                    pedido.KmCobrados = d.KmCobrados;
                    pedido.KmFuente = d.KmFuente;
                    pedido.RecargoUrgencia = d.RecargoUrgencia;
                    pedido.DescuentoRango = d.DescuentoRango;
                    pedido.PrecioManual = d.Total;
                    pedido.PrecioManualPor = datos.ClienteUsuarioId;
                    pedido.PrecioManualEn = DateTimeOffset.UtcNow;
                }
                return pedido;
            }).ToList();
            db.Pedidos.AddRange(pedidos);
            await db.SaveChangesAsync(ct);

            // Paradas de la ruta propuesta: una por dirección (RF-14), en el orden recibido.
            var grupos = pedidos
                .Where(p => ubicaciones[p.DestinoUbicacionId].Apta)
                .GroupBy(p => p.DestinoUbicacionId)
                .ToList();
            var fueraDeRuta = pedidos.Count(p => !ubicaciones[p.DestinoUbicacionId].Apta);

            if (grupos.Count > 0)
            {
                var ruta = new Ruta
                {
                    Fecha = datos.FechaEntrega,
                    OrigenUbicacionId = deposito.UbicacionId,
                    CapacidadParadas = Math.Max(24, grupos.Count),
                    CreadaEn = DateTimeOffset.UtcNow,
                };
                db.Rutas.Add(ruta);
                await db.SaveChangesAsync(ct);

                var nuevasParadas = grupos.Select((g, i) => new RutaParada
                {
                    RutaId = ruta.Id,
                    UbicacionId = g.Key,
                    Tipo = "entrega",
                    Orden = i + 1,
                }).ToList();
                db.RutaParadas.AddRange(nuevasParadas);
                viaje.RutaId = ruta.Id;
                await db.SaveChangesAsync(ct);

                for (var i = 0; i < grupos.Count; i++)
                    foreach (var pedido in grupos[i])
                        db.ParadaPedidos.Add(new ParadaPedido { ParadaId = nuevasParadas[i].Id, PedidoId = pedido.Id });
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            creado = new ViajeCreado(viaje.Id, viaje.RutaId, pedidos.Select(p => p.Id).ToList(), fueraDeRuta);
        });

        return (creado, null);
    }

    /// <summary>Detalle con las paradas en el orden de su ruta (el que Operación haya dejado) o, sin
    /// ruta, en el de carga. `clienteId` acota a un cliente (portal); `conPrecios` = dueño o BackOffice.</summary>
    public async Task<ViajeDetalle?> DetalleAsync(long id, int? clienteId, bool conPrecios, CancellationToken ct)
    {
        var v = await db.Viajes.AsNoTracking()
            .Where(x => x.Id == id && (clienteId == null || x.ClienteId == clienteId))
            .Select(x => new
            {
                x.Id, x.ClienteId, ClienteRazonSocial = x.Cliente.RazonSocial, x.FechaEntrega, x.TipoVehiculo,
                x.Estado, x.RutaId, RutaEstado = x.Ruta != null ? x.Ruta.Estado : null, x.KmEstimados,
                CreadoPor = x.CreadoPorClienteUsuario != null ? x.CreadoPorClienteUsuario.Nombre
                    : x.CreadoPorUsuario != null ? x.CreadoPorUsuario.Nombre : "—",
                x.CreadoEn, x.Observaciones,
                Origen = new OrigenViaje(x.OrigenUbicacionId, x.OrigenUbicacion.NombreDeposito ?? x.OrigenUbicacion.CalleNumero,
                    x.OrigenUbicacion.CalleNumero, x.OrigenUbicacion.Lat, x.OrigenUbicacion.Lng),
            })
            .SingleOrDefaultAsync(ct);
        if (v is null) return null;

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.ViajeId == id)
            .Select(p => new
            {
                p.Id, p.DestinatarioNombre, p.DestinatarioTelefono, p.DestinoUbicacion.CalleNumero,
                Localidad = p.DestinoUbicacion.Localidad != null ? p.DestinoUbicacion.Localidad.Nombre : null,
                p.DestinoUbicacion.Lat, p.DestinoUbicacion.Lng, p.Bultos, p.Observaciones, p.Estado,
                Precio = p.PrecioManual ?? p.Total, p.OrdenEnViaje,
                // Orden de la parada en la ruta del viaje, si está en ella.
                OrdenRuta = db.ParadaPedidos.Where(pp => pp.PedidoId == p.Id && pp.Parada.RutaId == v.RutaId)
                    .Select(pp => (int?)pp.Parada.Orden).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var ordenados = pedidos
            .OrderBy(p => p.OrdenRuta ?? int.MaxValue).ThenBy(p => p.OrdenEnViaje).ThenBy(p => p.Id)
            .ToList();
        var paradas = ordenados.Select((p, i) => new ParadaViajeDetalle(
            i + 1, p.Id, p.DestinatarioNombre, p.DestinatarioTelefono, p.CalleNumero, p.Localidad, p.Lat, p.Lng,
            p.Bultos, p.Observaciones, p.Estado.ToString(), p.OrdenRuta is not null,
            conPrecios ? p.Precio : null)).ToList();

        // Recorrido del orden actual (por calle; el proveedor lo cachea 30 min). Una dirección
        // repetida es un solo punto (RF-14).
        var puntos = ordenados.Where(p => p.Lat is not null)
            .DistinctBy(p => (p.Lat, p.Lng)).Select(p => ((decimal?)p.Lat, (decimal?)p.Lng)).ToList();
        var (_, _, linea) = await RecorridoAsync(v.Origen, puntos, 0, ct);

        var total = conPrecios && pedidos.All(p => p.Precio is not null)
            ? pedidos.Where(p => p.Estado != EstadoPedido.Cancelado).Sum(p => p.Precio!.Value)
            : (decimal?)null;

        return new ViajeDetalle(
            v.Id, v.ClienteId, v.ClienteRazonSocial, v.FechaEntrega, v.TipoVehiculo,
            EstadoVisible(v.Estado, v.RutaEstado), v.RutaId, v.RutaEstado, v.KmEstimados, v.CreadoPor, v.CreadoEn,
            v.Observaciones, v.Origen, paradas,
            pedidos.Count(p => p.Estado == EstadoPedido.Entregado),
            pedidos.Count(p => NegocioCliente.Grupo(p.Estado) == NegocioCliente.GrupoEstado.Fallido),
            total, linea);
    }

    public async Task<ListaPaginada<ViajeResumen>> ListarAsync(int? clienteId, int? pagina, int? tamanioPagina, CancellationToken ct)
    {
        var query = db.Viajes.AsNoTracking().Where(v => clienteId == null || v.ClienteId == clienteId);
        var total = await query.CountAsync(ct);

        var tamanio = Logistica.Web.Paginacion.TamanioEfectivo(tamanioPagina);
        var paginaActual = pagina is > 0 ? Math.Min(pagina.Value, 1_000_000) : 1;
        var filas = await query
            .OrderByDescending(v => v.FechaEntrega).ThenByDescending(v => v.Id)
            .Skip((paginaActual - 1) * tamanio).Take(tamanio)
            .Select(v => new
            {
                v.Id, v.ClienteId, v.Cliente.RazonSocial, v.FechaEntrega, v.Estado,
                RutaEstado = v.Ruta != null ? v.Ruta.Estado : null,
                Paradas = db.Pedidos.Count(p => p.ViajeId == v.Id),
                Entregadas = db.Pedidos.Count(p => p.ViajeId == v.Id && p.Estado == EstadoPedido.Entregado),
                Fallidas = db.Pedidos.Count(p => p.ViajeId == v.Id && (p.Estado == EstadoPedido.Fallido || p.Estado == EstadoPedido.Devuelto)),
                v.KmEstimados,
                CreadoPor = v.CreadoPorClienteUsuario != null ? v.CreadoPorClienteUsuario.Nombre
                    : v.CreadoPorUsuario != null ? v.CreadoPorUsuario.Nombre : "—",
                v.CreadoEn,
            })
            .ToListAsync(ct);

        return new ListaPaginada<ViajeResumen>(filas.Select(v => new ViajeResumen(
            v.Id, v.ClienteId, v.RazonSocial, v.FechaEntrega, EstadoVisible(v.Estado, v.RutaEstado),
            v.Paradas, v.Entregadas, v.Fallidas, v.KmEstimados, v.CreadoPor, v.CreadoEn)).ToList(), total);
    }

    /// <summary>Cancela el viaje entero mientras nada salió: la ruta propuesta sigue en planificación
    /// (o no hay) y todos sus envíos en Borrador. Cancelar en Borrador no tiene costo (§10.2-I). La ruta
    /// propuesta se elimina (sus paradas caen en cascada). Devuelve el error para un 409, o null.</summary>
    public async Task<string?> CancelarAsync(long id, int clienteId, Guid? usuarioId, string motivo, CancellationToken ct)
    {
        var viaje = await db.Viajes.Include(v => v.Ruta).SingleOrDefaultAsync(v => v.Id == id && v.ClienteId == clienteId, ct);
        if (viaje is null) return "no_existe";
        if (viaje.Estado == EstadosViaje.Cancelado) return "Este viaje ya está cancelado.";
        if (viaje.Ruta is { Estado: not "planificada" })
            return "Este viaje ya salió o está en camino; para cambiarlo, contactá a la Empresa.";

        var pedidos = await db.Pedidos.Where(p => p.ViajeId == id).ToListAsync(ct);
        if (pedidos.Any(p => p.Estado is not (EstadoPedido.Borrador or EstadoPedido.Cancelado)))
            return "Alguno de los envíos del viaje ya fue confirmado; para cambiarlo, contactá a la Empresa.";

        foreach (var p in pedidos.Where(p => p.Estado == EstadoPedido.Borrador)) p.Estado = EstadoPedido.Cancelado;
        viaje.Estado = EstadosViaje.Cancelado;
        if (viaje.Ruta is not null)
        {
            db.Rutas.Remove(viaje.Ruta);
            viaje.RutaId = null;
        }
        await db.GuardarComoAsync(usuarioId, motivo, ct);
        return null;
    }
}
