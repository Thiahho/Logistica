using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Resultado de intentar cerrar el ciclo de un cliente en una fecha de cierre puntual.
/// `FacturaId` null junto con `Omitido` no null = no había nada facturable (no es un error).</summary>
public record ResultadoCierreCliente(
    int ClienteId, string RazonSocial, string Ciclo,
    long? FacturaId, DateOnly PeriodoDesde, DateOnly PeriodoHasta,
    DateOnly FechaVencimiento, decimal Total, int CantidadItems,
    int AjustesPendientes, string? Omitido);

/// <summary>
/// E1 (Anexo I §5, B1). Único lugar del sistema que acuña un FacturaItem tipo='pedido' — lo
/// llaman PedidosController.CambiarEstado (cancelación de un Confirmado, §10.2-I) y
/// MisParadasController.Cerrar (entrega). Sin este servicio, dos controllers tendrían que copiar
/// la misma regla y se desincronizarían en la primera corrección.
///
/// AgregarItemDePedido NO guarda — solo agrega al change tracker. El caller decide si cierra con
/// GuardarComoAsync (si la escritura además toca `pedidos`, regla §3.7 de construccion_v1.md) o
/// SaveChangesAsync (el resto: pagos, ajustes, cierre, corte suspendido).
/// </summary>
public class CuentaCorrienteService(LogisticaDbContext db)
{
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
    public async Task<bool> ServicioCortadoAsync(int clienteId, DateOnly hoy, CancellationToken ct)
    {
        var corteSuspendidoHasta = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => c.CorteSuspendidoHasta)
            .SingleOrDefaultAsync(ct);
        if (corteSuspendidoHasta is { } hasta && hasta >= hoy) return false;

        return await DeudaVencidaAsync(clienteId, hoy, ct) > 0;
    }

    /// <summary>Acuña el ítem facturable de un pedido (entrega, o cancelación de un Confirmado —
    /// §10.2-I) en el momento exacto en que se vuelve facturable. Nace 'aprobado', sin pasar por
    /// el flujo de ajustes — nadie tiene que revisarlo.</summary>
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
    /// </summary>
    public async Task<List<ResultadoCierreCliente>> CerrarCiclosAsync(
        DateOnly fecha, int? clienteId, bool previsualizar, Guid actor, CancellationToken ct)
    {
        var clientesQuery = db.Clientes.AsNoTracking().Where(c => c.Activo);
        if (clienteId is not null) clientesQuery = clientesQuery.Where(c => c.Id == clienteId);
        var clientes = await clientesQuery
            .Select(c => new { c.Id, c.RazonSocial, c.CicloFacturacion })
            .ToListAsync(ct);

        var resultados = new List<ResultadoCierreCliente>();

        foreach (var cliente in clientes)
        {
            var ultimoCierre = await db.Facturas.AsNoTracking()
                .Where(f => f.ClienteId == cliente.Id)
                .Select(f => (DateOnly?)f.PeriodoHasta)
                .MaxAsync(ct);

            foreach (var cierre in CiclosFacturacion.CierresPendientes(cliente.CicloFacturacion, ultimoCierre, fecha))
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var periodoDesde = CiclosFacturacion.InicioDePeriodo(cliente.CicloFacturacion, cierre);

                // Reloj.ALaFechaLocal no traduce a SQL — se filtra en memoria sobre el candidato
                // acotado por cliente y aprobado; el volumen por cliente es chico (P6: dos
                // clientes cerrados hoy).
                var candidatos = await db.FacturaItems
                    .Where(i => i.FacturaId == null && i.Estado == "aprobado" && i.Pedido!.ClienteId == cliente.Id)
                    .ToListAsync(ct);
                var items = candidatos
                    .Where(i => Reloj.ALaFechaLocal(i.CreadoEn) <= cierre)
                    .OrderBy(i => i.CreadoEn)
                    .ToList();

                var ajustesPendientes = await db.FacturaItems.CountAsync(i =>
                    i.FacturaId == null && i.Estado == "pendiente" && i.Pedido!.ClienteId == cliente.Id, ct);

                if (items.Count == 0)
                {
                    resultados.Add(new ResultadoCierreCliente(
                        cliente.Id, cliente.RazonSocial, cliente.CicloFacturacion,
                        null, periodoDesde, cierre, default, 0m, 0, ajustesPendientes, "Sin ítems facturables en el período."));
                    await tx.RollbackAsync(ct);
                    continue;
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

                resultados.Add(new ResultadoCierreCliente(
                    cliente.Id, cliente.RazonSocial, cliente.CicloFacturacion,
                    factura.Id, periodoDesde, cierre, vencimiento, total, items.Count, ajustesPendientes, null));

                if (previsualizar) await tx.RollbackAsync(ct);
                else await tx.CommitAsync(ct);
            }
        }

        return resultados;
    }
}
