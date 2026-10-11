using Logistica.Datos;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Observaciones que ya se usaron en envíos de un cliente, para sugerirlas en la carga. Capa de
/// lectura sobre pedidos, igual que destinatarios-frecuentes (acta changelog 3.5): no persiste nada
/// nuevo. La comparten el BackOffice (PedidosController) y el portal (MiCuentaController); cada
/// puerta resuelve de quién es el clienteId.
/// </summary>
public static class ObservacionesFrecuentes
{
    private const int Tope = 10;

    /// <summary>Primero las usadas con ese destinatario, después el resto de las del cliente; dentro
    /// de cada grupo, la más reciente arriba.</summary>
    public static async Task<List<string>> ListarAsync(
        LogisticaDbContext db, int clienteId, string? destinatario, CancellationToken ct)
    {
        // Un nombre de destinatario nunca es vacío: sin destinatario no coincide ninguno.
        var nombre = destinatario?.Trim().ToLower() ?? "";

        var deEnvios = await db.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .Where(p => p.Estado != EstadoPedido.Cancelado)
            // Solo lo que escribió una persona al cargar: los reintentos y retornos nacen con una
            // observación armada por el sistema ("Retorno del pedido #…"), y la recepción con
            // diferencia de bultos le agrega su nota a la del pedido (PedidosController).
            .Where(p => p.Tipo == "entrega" || p.Tipo == "delivery")
            .Where(p => p.PedidoOrigenId == null)
            .Where(p => p.Observaciones != null && p.Observaciones != "" && !p.Observaciones.Contains("Recepción:"))
            // Una observación que el repartidor corrigió desde la calle y operación aceptó
            // (NovedadesController.Resolver) deja de sugerirse para esa dirección: queda la corregida
            // (más abajo). Si después alguien la vuelve a cargar, vuelve.
            .Where(p => !db.Novedades.Any(n =>
                n.Tipo == "cambio_propuesto" && n.Estado == "resuelta" && n.PropuestaCampo == "observaciones"
                && n.Pedido!.ClienteId == clienteId
                && n.Pedido.DestinoUbicacionId == p.DestinoUbicacionId
                && n.PropuestaValorAnterior == p.Observaciones
                && n.ResueltaEn > p.CreadoEn))
            .GroupBy(p => p.Observaciones!)
            .Select(g => new
            {
                Observaciones = g.Key,
                DelDestinatario = g.Max(p => p.DestinatarioNombre.ToLower() == nombre ? 1 : 0),
                Ultima = g.Max(p => p.CreadoEn),
            })
            .OrderByDescending(x => x.DelDestinatario)
            .ThenByDescending(x => x.Ultima)
            .Take(Tope)
            .ToListAsync(ct);

        // Las correcciones aceptadas también se sugieren: si el pedido ya estaba cerrado cuando se
        // aceptó, no se reescribió y la observación nueva no está en ningún envío todavía. Son pocas
        // filas por cliente: se traen todas y se descarta en memoria la que después se volvió a corregir.
        var correcciones = await db.Novedades.AsNoTracking()
            .Where(n => n.Tipo == "cambio_propuesto" && n.Estado == "resuelta" && n.PropuestaCampo == "observaciones")
            .Where(n => n.Pedido!.ClienteId == clienteId && n.PropuestaValorNuevo != null && n.ResueltaEn != null)
            .Select(n => new
            {
                Nueva = n.PropuestaValorNuevo!,
                Anterior = n.PropuestaValorAnterior,
                UbicacionId = n.Pedido!.DestinoUbicacionId,
                Destinatario = n.Pedido.DestinatarioNombre,
                ResueltaEn = n.ResueltaEn!.Value,
            })
            .ToListAsync(ct);
        var vigentes = correcciones
            .Where(c => !correcciones.Any(o =>
                o.UbicacionId == c.UbicacionId && o.Anterior == c.Nueva && o.ResueltaEn > c.ResueltaEn))
            .Select(c => new
            {
                Observaciones = c.Nueva,
                DelDestinatario = c.Destinatario.ToLower() == nombre ? 1 : 0,
                Ultima = c.ResueltaEn,
            });

        return deEnvios.Concat(vigentes)
            .GroupBy(x => x.Observaciones)
            .Select(g => new { Observaciones = g.Key, DelDestinatario = g.Max(x => x.DelDestinatario), Ultima = g.Max(x => x.Ultima) })
            .OrderByDescending(x => x.DelDestinatario)
            .ThenByDescending(x => x.Ultima)
            .Take(Tope)
            .Select(x => x.Observaciones)
            .ToList();
    }

    /// <summary>Lleva a "Mis clientes" una corrección de observaciones aceptada sobre un pedido: el
    /// destinatario guardado de ese cliente, con ese nombre y esa dirección, toma la observación nueva
    /// si la suya era la que se corrigió (o no tenía). Una nota distinta, escrita a mano en la libreta,
    /// no se pisa. No guarda: lo hace quien llama, en su misma transacción.</summary>
    public static async Task<int> CorregirGuardadaAsync(
        LogisticaDbContext db, Pedido pedido, string? anterior, string nueva, CancellationToken ct)
    {
        var previa = anterior ?? "";
        var nombre = pedido.DestinatarioNombre.ToLower();
        var guardados = await db.ClientesDestinatarios
            .Where(d => d.ClienteId == pedido.ClienteId && d.DestinoUbicacionId == pedido.DestinoUbicacionId)
            .Where(d => d.Nombre.ToLower() == nombre && (d.Observaciones ?? "") == previa)
            .ToListAsync(ct);
        foreach (var d in guardados) d.Observaciones = nueva;
        return guardados.Count;
    }
}
