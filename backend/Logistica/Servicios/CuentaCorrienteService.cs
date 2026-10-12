using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Resultado de intentar cerrar el ciclo de un cliente en una fecha de cierre puntual.
/// `FacturaId` null junto con `Omitido` no null = no se emitió factura: o no había nada facturable (no
/// es un error), o el cierre de ese cliente falló (`CuentaCorrienteService.OmitidoPorError`).</summary>
public record ResultadoCierreCliente(
    int ClienteId, string RazonSocial, string Ciclo,
    long? FacturaId, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
    DateOnly FechaVencimiento, decimal Total, int CantidadItems,
    int AjustesPendientes, string? Omitido);

/// <summary>Panel de cobranza: un cliente con deuda ya vencida ("vencido") o con una factura que
/// vence dentro de la ventana de preaviso ("por_vencer"). `ProximoVencimiento`/
/// `DiasHastaVencimiento` reflejan la factura no vencida más próxima, sea cual sea la categoría —
/// un cliente "vencido" puede no tener ninguna (si todo lo que debe ya venció) y entonces salen
/// null.</summary>
public record ClienteEnRiesgo(
    int ClienteId, string RazonSocial, string? Email, string? Telefono, bool Activo,
    string Categoria,                 // "vencido" | "por_vencer"
    decimal Saldo, decimal DeudaVencida, bool ServicioCortado, DateOnly? CorteSuspendidoHasta,
    DateOnly? ProximoVencimiento, decimal SaldoProximoAVencer, int? DiasHastaVencimiento,
    string ColorPago, string ColorTrato, string ColorOper);

/// <summary>
/// E1 (Anexo I §5, B1). Único lugar del sistema que acuña un FacturaItem tipo='pedido' — lo
/// llaman PedidosController.CambiarEstado (cancelación de un Confirmado, §10.2-I, y entrega
/// marcada desde el back-office) y MisParadasController.Cerrar (entrega del repartidor). Sin
/// este servicio, dos controllers tendrían que copiar la misma regla y se desincronizarían en la
/// primera corrección.
///
/// AgregarItemDePedido NO guarda — solo agrega al change tracker. El caller decide si cierra con
/// GuardarComoAsync (si la escritura además toca `pedidos`, regla §3.7 de construccion_v1.md) o
/// SaveChangesAsync (el resto: pagos, ajustes, cierre, corte suspendido).
/// </summary>
/// <param name="log">Opcional: el contenedor lo inyecta; las pruebas arman el servicio solo con la base.</param>
public class CuentaCorrienteService(LogisticaDbContext db, ILogger<CuentaCorrienteService>? log = null)
{
    /// <summary>Ventana de preaviso por defecto del panel de cobranza (RiesgoAsync) cuando el
    /// caller no especifica una — compartida entre ClientesController.ListarRiesgo y
    /// AvisosCobranzaService para que las dos vías usen el mismo criterio de "próximo a
    /// vencer".</summary>
    public const int DiasPreavisoDefault = 15;

    /// <summary>`Omitido` de un cliente cuyo cierre falló (CerrarCiclosAsync). Constante pública para
    /// que quien corre el cierre lo distinga de "sin ítems", que no es un error. El detalle técnico
    /// va al log, no a la pantalla.</summary>
    public const string OmitidoPorError =
        "No se pudo cerrar el ciclo de este cliente por un error: quedó sin facturar. El resto de los clientes se procesó igual.";

    /// <summary>Saldo total del cliente (facturado - pagado), incluye deuda todavía no vencida.
    /// Es lo que se muestra en pantalla — NUNCA lo que gatea el corte de servicio, ver
    /// DeudaVencidaAsync.</summary>
    public Task<decimal> SaldoAsync(int clienteId, CancellationToken ct) =>
        db.Clientes.Where(c => c.Id == clienteId)
            .Select(c => LogisticaDbContext.SaldoCliente(c.Id))
            .SingleAsync(ct);

    /// <summary>Deuda con vencimiento ya pasado a `fecha` — la que realmente gatea el corte
    /// (acta §10.2-L1/L3). Un cliente con una factura recién emitida y no vencida da 0 acá aunque
    /// SaldoAsync sea positivo.</summary>
    public Task<decimal> DeudaVencidaAsync(int clienteId, DateOnly fecha, CancellationToken ct) =>
        db.Clientes.Where(c => c.Id == clienteId)
            .Select(c => LogisticaDbContext.DeudaVencidaCliente(c.Id, fecha))
            .SingleAsync(ct);

    /// <summary>¿El cliente tiene el servicio cortado hoy? Deuda vencida &gt; 0, salvo que un
    /// plan de cuotas (§10.2-L4) lo esté suspendiendo.</summary>
    public Task<bool> ServicioCortadoAsync(int clienteId, DateOnly hoy, CancellationToken ct) =>
        ServicioCortadoAsync(clienteId, hoy, deudaVencidaConocida: null, ct);

    /// <summary>Overload que reutiliza una deuda vencida ya calculada por el caller, para no
    /// evaluar deuda_vencida_cliente() dos veces en el mismo request — ClientesController.
    /// CuentaCorriente y MiCuentaController.Detalle ya la necesitan para mostrarla en pantalla.
    /// Mantiene el mismo orden de evaluación que la versión original: si el corte está
    /// suspendido, ni siquiera hace falta la deuda (conocida o no).</summary>
    public async Task<bool> ServicioCortadoAsync(int clienteId, DateOnly hoy, decimal? deudaVencidaConocida, CancellationToken ct)
    {
        var corteSuspendidoHasta = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => c.CorteSuspendidoHasta)
            .SingleOrDefaultAsync(ct);
        if (corteSuspendidoHasta is { } hasta && hasta >= hoy) return false;

        var deudaVencida = deudaVencidaConocida ?? await DeudaVencidaAsync(clienteId, hoy, ct);
        return deudaVencida > 0;
    }

    /// <summary>Acuña el ítem facturable de un pedido (entrega, o cancelación de un Confirmado —
    /// §10.2-I) en el momento exacto en que se vuelve facturable. Nace 'aprobado', sin pasar por
    /// el flujo de ajustes — nadie tiene que revisarlo.</summary>
    /// <summary>Agrega (sin guardar) un pago imputado. Lo usan el alta manual de Administración y la
    /// confirmación de un pago informado por el cliente: los dos crean exactamente lo mismo.</summary>
    public Pago AgregarPago(int clienteId, decimal monto, DateOnly fechaPago, string medio, string? nota, Guid registradoPor)
    {
        var pago = new Pago
        {
            ClienteId = clienteId,
            Monto = monto,
            FechaPago = fechaPago,
            Medio = medio,
            Nota = nota,
            RegistradoPor = registradoPor,
            RegistradoEn = DateTimeOffset.UtcNow,
        };
        db.Pagos.Add(pago);
        return pago;
    }

    public void AgregarItemDePedido(Pedido pedido, string descripcion, decimal monto, Guid? actor) =>
        db.FacturaItems.Add(new FacturaItem
        {
            PedidoId = pedido.Id,
            Tipo = "pedido",
            Descripcion = descripcion,
            Monto = monto,
            Estado = "aprobado",
            CreadoPor = actor,
            CreadoEn = DateTimeOffset.UtcNow,
        });

    /// <summary>
    /// Cierra todos los ciclos pendientes hasta `fecha` (o solo los de `clienteId` si viene).
    /// Un cliente puede tener más de un cierre pendiente si el endpoint no se corrió en un
    /// tiempo — CiclosFacturacion.CierresPendientes los enumera todos, en orden.
    ///
    /// El barrido de ítems facturables está acotado SOLO por arriba (creado antes del cierre):
    /// nunca por abajo, para que un ajuste aprobado tarde no quede huérfano de un período ya
    /// pasado. `previsualizar=true` hace el mismo cómputo y revierte — la factura es inmutable,
    /// no hay deshacer, así que la previsualización no es un lujo.
    ///
    /// Cada cliente va aislado: si su cierre tira, se revierte lo suyo, sale en el resultado con
    /// `OmitidoPorError` y la corrida sigue. Sin eso, un solo cliente que falla deja sin facturar a
    /// todos los que vienen después, en cada corrida (la automática reintenta siempre igual). El
    /// orden es por id, para que una corrida y su reintento recorran lo mismo.
    /// </summary>
    public async Task<List<ResultadoCierreCliente>> CerrarCiclosAsync(
        DateOnly fecha, int? clienteId, bool previsualizar, Guid? actor, CancellationToken ct)
    {
        // Los activos, y los dados de baja que todavía tienen algo aprobado sin facturar: la baja
        // corta el servicio, no la deuda. Un cliente inactivo sin nada pendiente no aparece (ni
        // siquiera como "sin ítems"), para que el resultado no se llene de bajas viejas.
        var clientesQuery = db.Clientes.AsNoTracking().Where(c => c.Activo
            || db.FacturaItems.Any(i => i.FacturaId == null && i.Estado == "aprobado"
                && i.Pedido != null && i.Pedido.ClienteId == c.Id));
        if (clienteId is not null) clientesQuery = clientesQuery.Where(c => c.Id == clienteId);
        var clientes = await clientesQuery
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.RazonSocial, c.CicloFacturacion })
            .ToListAsync(ct);
        var clienteIds = clientes.Select(c => c.Id).ToList();

        // Precarga fuera del loop: antes eran ~3 round-trips POR CLIENTE (último cierre,
        // candidatos, ajustes pendientes) antes de escribir nada — con 200 clientes activos y un
        // Postgres remoto (10 ms de RTT) son ~6s de pura latencia de red en el día de cierre.
        var ultimosCierres = await db.Facturas.AsNoTracking()
            .Where(f => clienteIds.Contains(f.ClienteId))
            .GroupBy(f => f.ClienteId)
            .Select(g => new { ClienteId = g.Key, Ultimo = g.Max(f => f.PeriodoHasta) })
            .ToDictionaryAsync(x => x.ClienteId, x => (DateOnly?)x.Ultimo, ct);

        var ajustesPendientesPorCliente = await db.FacturaItems.AsNoTracking()
            .Where(i => i.FacturaId == null && i.Estado == "pendiente" && i.Pedido != null && clienteIds.Contains(i.Pedido.ClienteId))
            .GroupBy(i => i.Pedido!.ClienteId)
            .Select(g => new { ClienteId = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.ClienteId, x => x.Cantidad, ct);

        // NO AsNoTracking: estas entidades se mutan más abajo (item.FacturaId = factura.Id) y esa
        // mutación tiene que persistir vía SaveChangesAsync, igual que en la versión anterior.
        var candidatosPorCliente = (await db.FacturaItems
                .Where(i => i.FacturaId == null && i.Estado == "aprobado" && i.Pedido != null && clienteIds.Contains(i.Pedido.ClienteId))
                .Select(i => new { Item = i, ClienteId = i.Pedido!.ClienteId })
                .ToListAsync(ct))
            .GroupBy(x => x.ClienteId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Item).ToList());

        var resultados = new List<ResultadoCierreCliente>();

        foreach (var cliente in clientes)
        {
            var ultimoCierre = ultimosCierres.GetValueOrDefault(cliente.Id);
            var ajustesPendientes = ajustesPendientesPorCliente.GetValueOrDefault(cliente.Id);
            // Lista local, mutable por este método: un cliente con más de un cierre pendiente
            // (CierresPendientes puede devolver varios) no debe reusar en el segundo cierre un
            // ítem que el primero ya asignó a una factura — ver más abajo, se saca de acá
            // solo cuando ese primer cierre efectivamente confirma (no en previsualización,
            // igual que antes: un rollback deja el ítem con FacturaId null también en la base,
            // así que sigue siendo candidato del próximo cierre, tal como pasaba re-consultando
            // la base en cada iteración).
            var pendientesDelCliente = candidatosPorCliente.GetValueOrDefault(cliente.Id) ?? [];

            foreach (var cierre in CiclosFacturacion.CierresPendientes(cliente.CicloFacturacion, ultimoCierre, fecha))
            {
                var periodoDesde = CiclosFacturacion.InicioDePeriodo(cliente.CicloFacturacion, cierre);

                // CreateExecutionStrategy().ExecuteAsync envuelve la transacción manual, exigido
                // por EnableRetryOnFailure (RegistroDatos.cs). A diferencia de GuardarParadas/
                // CerrarPlanificacion, acá el delegate NO puede tocar `resultados` ni
                // `pendientesDelCliente` directamente — ambas son compartidas entre iteraciones y
                // un reintento las duplicaría/ensuciaría. El delegate solo lee/escribe la base y
                // devuelve lo que pasó; el caller (afuera, después de que ExecuteAsync ya no va a
                // reintentar más) decide qué hacer con eso.
                var estrategia = db.Database.CreateExecutionStrategy();
                ResultadoCierreCliente resultado;
                List<FacturaItem>? itemsFacturados;
                try
                {
                    (resultado, itemsFacturados) = await CerrarUnoAsync();
                }
                // Con el token del caller cancelado no se traga nada: el host se apaga o el request
                // se cortó, y lo que venga (OperationCanceledException o lo que el driver tire en su
                // lugar) tiene que propagar.
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    DescartarIntentoFallido(cliente.Id, pendientesDelCliente);
                    log?.LogError(ex,
                        "Cierre de ciclo: falló el cliente {ClienteId} ({RazonSocial}) en el período que cierra el {Cierre}; se omite y la corrida sigue",
                        cliente.Id, cliente.RazonSocial, cierre);
                    resultados.Add(new ResultadoCierreCliente(
                        cliente.Id, cliente.RazonSocial, cliente.CicloFacturacion,
                        null, periodoDesde, cierre, default, 0m, 0, ajustesPendientes, OmitidoPorError));
                    // Sus cierres siguientes también se saltean: barrerían los mismos ítems y, si
                    // llegaran a pasar, dejarían un período sin factura en el medio.
                    break;
                }

                resultados.Add(resultado);
                if (itemsFacturados is not null)
                {
                    // Recién confirmado: estos ítems ya no son candidatos para el próximo cierre
                    // pendiente de este mismo cliente, dentro de esta misma corrida.
                    pendientesDelCliente = pendientesDelCliente.Except(itemsFacturados).ToList();
                }

                Task<(ResultadoCierreCliente, List<FacturaItem>?)> CerrarUnoAsync() => estrategia.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);

                    // Reloj.ALaFechaLocal no traduce a SQL — se filtra en memoria sobre el
                    // candidato acotado por cliente y aprobado; el volumen por cliente es chico
                    // (P6: dos clientes cerrados hoy).
                    var items = pendientesDelCliente
                        .Where(i => Reloj.ALaFechaLocal(i.CreadoEn) <= cierre)
                        .OrderBy(i => i.CreadoEn)
                        .ToList();

                    if (items.Count == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return (
                            new ResultadoCierreCliente(
                                cliente.Id, cliente.RazonSocial, cliente.CicloFacturacion,
                                null, periodoDesde, cierre, default, 0m, 0, ajustesPendientes, "Sin ítems facturables en el período."),
                            (List<FacturaItem>?)null);
                    }

                    var total = items.Sum(i => i.Monto!.Value);
                    var vencimiento = cierre.AddDays(CiclosFacturacion.DiasVencimiento(cliente.CicloFacturacion));

                    var factura = new Factura
                    {
                        ClienteId = cliente.Id,
                        Ciclo = cliente.CicloFacturacion,
                        PeriodoDesde = periodoDesde,
                        PeriodoHasta = cierre,
                        FechaEmision = fecha,
                        FechaVencimiento = vencimiento,
                        Total = total,
                        EmitidaPor = actor,
                        CreadaEn = DateTimeOffset.UtcNow,
                    };
                    db.Facturas.Add(factura);
                    await db.SaveChangesAsync(ct); // asigna factura.Id

                    foreach (var item in items) item.FacturaId = factura.Id;
                    await db.SaveChangesAsync(ct);

                    var resultadoCierre = new ResultadoCierreCliente(
                        cliente.Id, cliente.RazonSocial, cliente.CicloFacturacion,
                        factura.Id, periodoDesde, cierre, vencimiento, total, items.Count, ajustesPendientes, null);

                    if (previsualizar)
                    {
                        await tx.RollbackAsync(ct);
                        return (resultadoCierre, (List<FacturaItem>?)null);
                    }

                    await tx.CommitAsync(ct);
                    return (resultadoCierre, (List<FacturaItem>?)items);
                });
            }
        }

        return resultados;
    }

    /// <summary>
    /// La transacción del cliente que falló ya se revirtió, pero el change tracker todavía recuerda el
    /// intento: la factura (por insertar, o insertada y revertida) y los ítems apuntándole. Si quedan
    /// así, el SaveChangesAsync del cliente siguiente vuelve a mandar esas mismas escrituras y falla
    /// él también. Se deja el contexto como está la base: sin esa factura y con los ítems sin facturar.
    /// </summary>
    private void DescartarIntentoFallido(int clienteId, List<FacturaItem> pendientesDelCliente)
    {
        foreach (var item in pendientesDelCliente)
        {
            item.FacturaId = null;
            db.Entry(item).State = EntityState.Unchanged;
        }
        foreach (var factura in db.ChangeTracker.Entries<Factura>().Where(f => f.Entity.ClienteId == clienteId).ToList())
            factura.State = EntityState.Detached;
    }

    /// <summary>
    /// Clientes con deuda ya vencida o con una factura que vence dentro de `diasPreaviso` días —
    /// la base del panel de cobranza. Dos round-trips, mismo patrón de precarga agrupada que
    /// CerrarCiclosAsync (que ya corrigió exactamente este problema: nunca N+1 por cliente).
    ///
    /// `clienteIds` null = todos los clientes de riesgo; no-null acota a una selección puntual —
    /// el mismo método sirve al listado completo del panel Y a AvisosCobranzaService, así el
    /// servidor SIEMPRE decide la categoría/plantilla de cada cliente con su estado real, nunca
    /// con lo que mande el front.
    ///
    /// DUPLICACIÓN A PROPÓSITO: `DeudaVencida` acá es un espejo LINQ del cuerpo SQL de
    /// deuda_vencida_cliente() (Migrations/..._AgregarCuentaCorriente.cs) — no se proyecta la
    /// función por fila porque, aunque sea `language sql stable` e inlineable, dispara la window
    /// function de v_facturas_saldo una vez POR CLIENTE (O(N × facturas)). Si se toca acá la
    /// regla de "qué cuenta como vencido", tocar también la función SQL — mismo criterio de
    /// duplicación documentada que ya usa `EstadoDe` en ClientesController/FacturasController.
    /// </summary>
    public async Task<List<ClienteEnRiesgo>> RiesgoAsync(
        DateOnly hoy, int diasPreaviso, IReadOnlyCollection<int>? clienteIds, bool soloActivos, CancellationToken ct)
    {
        var limite = hoy.AddDays(diasPreaviso);

        var clientesQuery = db.Clientes.AsNoTracking().AsQueryable();
        if (soloActivos) clientesQuery = clientesQuery.Where(c => c.Activo);
        if (clienteIds is not null) clientesQuery = clientesQuery.Where(c => clienteIds.Contains(c.Id));

        var clientes = await clientesQuery
            .Select(c => new
            {
                c.Id, c.RazonSocial, c.Email, c.Telefono, c.Activo,
                c.ColorPago, c.ColorTrato, c.ColorOper, c.CorteSuspendidoHasta,
            })
            .ToListAsync(ct);
        var ids = clientes.Select(c => c.Id).ToList();

        var saldosPorCliente = await db.Set<FacturaSaldo>().AsNoTracking()
            .Where(f => f.Saldo > 0 && ids.Contains(f.ClienteId))
            .GroupBy(f => f.ClienteId)
            .Select(g => new
            {
                ClienteId = g.Key,
                Saldo = g.Sum(f => f.Saldo),
                DeudaVencida = g.Sum(f => f.FechaVencimiento < hoy ? f.Saldo : 0m),
                ProximoVencimiento = g.Min(f => f.FechaVencimiento >= hoy ? (DateOnly?)f.FechaVencimiento : null),
                SaldoProximoAVencer = g.Sum(f => f.FechaVencimiento >= hoy && f.FechaVencimiento <= limite ? f.Saldo : 0m),
            })
            .ToDictionaryAsync(x => x.ClienteId, x => x, ct);

        var resultado = new List<ClienteEnRiesgo>();
        foreach (var cliente in clientes)
        {
            // Sin fila en v_facturas_saldo (con saldo > 0): no debe nada, no es de riesgo.
            if (!saldosPorCliente.TryGetValue(cliente.Id, out var saldo)) continue;

            var deudaVencida = saldo.DeudaVencida;
            // Mismo orden de evaluación que ServicioCortadoAsync: la suspensión (plan de cuotas)
            // gana aunque haya deuda vencida.
            var servicioCortado = deudaVencida > 0 && !(cliente.CorteSuspendidoHasta is { } h && h >= hoy);

            string categoria;
            if (deudaVencida > 0) categoria = "vencido"; // gana sobre "por_vencer" si aplican las dos
            else if (saldo.ProximoVencimiento is { } prox && prox <= limite) categoria = "por_vencer";
            else continue; // nada vencido y nada por vencer dentro de la ventana: no es de riesgo

            var dias = saldo.ProximoVencimiento is { } p ? p.DayNumber - hoy.DayNumber : (int?)null;

            resultado.Add(new ClienteEnRiesgo(
                cliente.Id, cliente.RazonSocial, cliente.Email, cliente.Telefono, cliente.Activo,
                categoria, saldo.Saldo, deudaVencida, servicioCortado, cliente.CorteSuspendidoHasta,
                saldo.ProximoVencimiento, saldo.SaldoProximoAVencer, dias,
                cliente.ColorPago, cliente.ColorTrato, cliente.ColorOper));
        }

        return resultado
            .OrderByDescending(r => r.DeudaVencida)
            .ThenBy(r => r.ProximoVencimiento)
            .ThenBy(r => r.RazonSocial)
            .ToList();
    }
}
