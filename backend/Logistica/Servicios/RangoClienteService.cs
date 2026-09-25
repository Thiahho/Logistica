using System.Globalization;
using System.Text.Json;
using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>Lo que el recálculo haría (o hizo) con un cliente.</summary>
public record RangoRecalculado(
    int ClienteId, string RazonSocial, CriteriosRango Criterios,
    string CalculadoAnterior, string CalculadoNuevo, string EfectivoAnterior, string EfectivoNuevo,
    bool AjusteVencido);

/// <summary>
/// B3 — rangos de cliente (acta RF-42, changelog 4.21; diseño_e2_rangos_liquidacion.md §3). El
/// recálculo es trimestral (D4) y lo dispara administración: no hay scheduler. La lógica pura (qué
/// rango corresponde, el efectivo con el ajuste, el % de pagos en término) vive en
/// Dominio/RangosCliente.cs; acá se mide desde la base y se escribe.
/// </summary>
public class RangoClienteService(LogisticaDbContext db)
{
    public async Task<List<RangoRecalculado>> MedirAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var rangos = await db.Rangos.AsNoTracking().ToListAsync(ct);
        var hoy = Reloj.HoyLocal();
        var (desdeUtc, _) = Reloj.RangoLocalUtc(desde);
        var (_, hastaUtc) = Reloj.RangoLocalUtc(hasta);

        var clientes = await db.Clientes.AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.RazonSocial)
            .Select(c => new { c.Id, c.RazonSocial, c.CreadoEn, c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence })
            .ToListAsync(ct);

        // Envíos entregados del trimestre, por fecha de entrega: cuentan y dan las semanas activas.
        var entregas = (await db.Pedidos.AsNoTracking()
                .Where(p => p.Estado == EstadoPedido.Entregado && p.FechaEntrega >= desde && p.FechaEntrega <= hasta)
                .Select(p => new { p.ClienteId, p.FechaEntrega })
                .ToListAsync(ct))
            .ToLookup(p => p.ClienteId, p => p.FechaEntrega);

        // Lo facturable nace en el momento en que se vuelve facturable (FacturaItem): cuenta en el
        // trimestre en que nació, se haya emitido la factura o no.
        var facturacion = (await db.FacturaItems.AsNoTracking()
                .Where(i => i.Estado == "aprobado" && i.Monto != null && i.CreadoEn >= desdeUtc && i.CreadoEn < hastaUtc)
                .Select(i => new
                {
                    ClienteId = i.Factura != null ? (int?)i.Factura.ClienteId : i.Pedido != null ? (int?)i.Pedido.ClienteId : null,
                    i.Monto,
                })
                .ToListAsync(ct))
            .Where(i => i.ClienteId is not null)
            .GroupBy(i => i.ClienteId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Monto!.Value));

        // FIFO de D12: hacen falta todas las facturas y pagos hasta el fin del trimestre, no solo los del trimestre.
        var facturas = (await db.Facturas.AsNoTracking()
                .Where(f => f.FechaEmision <= hasta)
                .Select(f => new { f.ClienteId, f.Id, f.FechaEmision, f.FechaVencimiento, f.Total })
                .ToListAsync(ct))
            .ToLookup(f => f.ClienteId);
        var pagos = (await db.Pagos.AsNoTracking()
                .Where(p => p.FechaPago <= hasta)
                .Select(p => new { p.ClienteId, p.FechaPago, p.Monto })
                .ToListAsync(ct))
            .ToLookup(p => p.ClienteId);

        return clientes.Select(c =>
        {
            var fechas = entregas[c.Id].ToList();
            var (pct, vencidas) = RangosCliente.PctPagosEnTermino(
                facturas[c.Id].Select(f => (f.Id, f.FechaEmision, f.FechaVencimiento, f.Total)),
                pagos[c.Id].Select(p => (p.FechaPago, p.Monto)),
                desde, hasta);
            var criterios = new CriteriosRango(
                Envios: fechas.Count,
                Facturacion: facturacion.GetValueOrDefault(c.Id),
                AntiguedadMeses: RangosCliente.MesesEntre(Reloj.ALaFechaLocal(c.CreadoEn), hasta),
                SemanasActivas: fechas.Select(SemanaIso).Distinct().Count(),
                PctPagosEnTermino: pct,
                FacturasVencidas: vencidas);

            var calculadoNuevo = RangosCliente.Calcular(criterios, rangos);
            var ajusteVencido = c.RangoAjuste != 0 && c.RangoAjusteVence < hoy;
            var efectivoAnterior = RangosCliente.Efectivo(c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence, hoy, rangos);
            var efectivoNuevo = RangosCliente.Efectivo(calculadoNuevo, c.RangoAjuste, c.RangoAjusteVence, hoy, rangos);
            return new RangoRecalculado(c.Id, c.RazonSocial, criterios, c.RangoCalculado, calculadoNuevo,
                efectivoAnterior, efectivoNuevo, ajusteVencido);
        }).ToList();
    }

    /// <summary>Aplica el recálculo: rango calculado de cada cliente, limpia los ajustes vencidos y deja
    /// una fila de historial por cada cliente cuyo rango cambió. Idempotente: recalcular el mismo
    /// trimestre de nuevo no cambia nada ni escribe historial.</summary>
    public async Task<List<RangoRecalculado>> AplicarAsync(string trimestre, DateOnly desde, DateOnly hasta, Guid actor, CancellationToken ct)
    {
        var resultado = await MedirAsync(desde, hasta, ct);
        var ids = resultado.Select(r => r.ClienteId).ToList();
        var clientes = await db.Clientes.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var ahora = DateTimeOffset.UtcNow;

        foreach (var r in resultado)
        {
            var c = clientes[r.ClienteId];
            c.RangoCalculado = r.CalculadoNuevo;
            c.RangoCalculadoEn = ahora;
            if (r.AjusteVencido) LimpiarAjuste(c);

            if (r.CalculadoAnterior != r.CalculadoNuevo || r.EfectivoAnterior != r.EfectivoNuevo)
            {
                db.ClienteRangos.Add(new ClienteRango
                {
                    ClienteId = c.Id,
                    RangoAnterior = r.EfectivoAnterior,
                    RangoNuevo = r.EfectivoNuevo,
                    Origen = "recalculo",
                    Trimestre = trimestre,
                    Criterios = JsonSerializer.Serialize(r.Criterios),
                    Motivo = r.AjusteVencido ? "El ajuste manual venció." : null,
                    RegistradoPor = actor,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return resultado;
    }

    /// <summary>Definición F: ±1 rango con motivo y vencimiento, o 0 para quitarlo. Deja historial en el acto.</summary>
    public async Task<string?> AjustarAsync(int clienteId, short ajuste, string? motivo, DateOnly? vence, Guid actor, CancellationToken ct)
    {
        var hoy = Reloj.HoyLocal();
        if (ajuste is not (-1 or 0 or 1)) return "El ajuste es de un rango como máximo: -1, 0 o +1.";
        if (ajuste != 0 && string.IsNullOrWhiteSpace(motivo)) return "El ajuste manual exige un motivo escrito.";
        if (ajuste != 0 && (vence is null || vence <= hoy)) return "El ajuste manual exige una fecha de vencimiento posterior a hoy.";

        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null) return "El cliente no existe.";

        var rangos = await db.Rangos.AsNoTracking().ToListAsync(ct);
        var anterior = RangosCliente.Efectivo(cliente.RangoCalculado, cliente.RangoAjuste, cliente.RangoAjusteVence, hoy, rangos);

        if (ajuste == 0) LimpiarAjuste(cliente);
        else
        {
            cliente.RangoAjuste = ajuste;
            cliente.RangoAjusteMotivo = motivo!.Trim();
            cliente.RangoAjusteVence = vence;
            cliente.RangoAjustePor = actor;
            cliente.RangoAjusteEn = DateTimeOffset.UtcNow;
        }

        db.ClienteRangos.Add(new ClienteRango
        {
            ClienteId = clienteId,
            RangoAnterior = anterior,
            RangoNuevo = RangosCliente.Efectivo(cliente.RangoCalculado, cliente.RangoAjuste, cliente.RangoAjusteVence, hoy, rangos),
            Origen = "ajuste",
            Motivo = ajuste == 0 ? "Se quitó el ajuste manual." : $"{(ajuste > 0 ? "+1" : "-1")} hasta {vence:dd/MM/yyyy}: {motivo!.Trim()}",
            RegistradoPor = actor,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>El rango efectivo de hoy de un cliente (nombre, límite de crédito y prioridad).</summary>
    public async Task<Rango?> EfectivoAsync(int clienteId, CancellationToken ct)
    {
        var c = await db.Clientes.AsNoTracking()
            .Where(x => x.Id == clienteId)
            .Select(x => new { x.RangoCalculado, x.RangoAjuste, x.RangoAjusteVence })
            .SingleOrDefaultAsync(ct);
        if (c is null) return null;
        var lista = await db.Rangos.AsNoTracking().ToListAsync(ct);
        var codigo = RangosCliente.Efectivo(c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence, Reloj.HoyLocal(), lista);
        return lista.Single(r => r.Codigo == codigo);
    }

    /// <summary>
    /// Límite de crédito del rango (acta 4.21): solo avisa, no bloquea — el bloqueo es el corte por deuda
    /// vencida de D11. Devuelve el texto del aviso si el saldo del cliente ya supera el límite de su rango.
    /// </summary>
    public async Task<string?> AvisoLimiteCreditoAsync(int clienteId, decimal saldo, CancellationToken ct)
    {
        var rango = await EfectivoAsync(clienteId, ct);
        if (rango?.LimiteCredito is not { } limite || saldo <= limite) return null;
        var ar = CultureInfo.GetCultureInfo("es-AR");
        return $"El saldo del cliente (${saldo.ToString("N2", ar)}) supera el límite de crédito de su rango {rango.Nombre} " +
               $"(${limite.ToString("N2", ar)}). El pedido se cargó igual: el límite solo avisa.";
    }

    private static void LimpiarAjuste(Cliente c)
    {
        c.RangoAjuste = 0;
        c.RangoAjusteMotivo = null;
        c.RangoAjusteVence = null;
        c.RangoAjustePor = null;
        c.RangoAjusteEn = null;
    }

    private static (int, int) SemanaIso(DateOnly d)
    {
        var fecha = d.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(fecha), ISOWeek.GetWeekOfYear(fecha));
    }
}
