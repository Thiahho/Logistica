using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

/// <summary>
/// Baja de una empresa por falta de pago: el servicio se corta de verdad. Sus pedidos pendientes
/// (TransicionesPedido.Retenibles) pasan a Retenido y dejan de estar en una ruta o por entregar; lo ya
/// resuelto (Entregado, Fallido, Devuelto, Cancelado) no se toca. Retener no cobra nada: no acuña
/// factura_items, no es un intento fallido y no consume una reprogramación sin cargo.
///
/// Un solo lugar para los dos caminos que escriben Cliente.Activo (ClientesController.Actualizar y
/// CambiarActivo). Reactivar la empresa no reanuda nada: eso lo decide operación pedido por pedido
/// (PedidosController.CambiarEstado).
/// </summary>
public class RetencionPedidosService(LogisticaDbContext db)
{
    /// <summary>El motivo que fn_log_estado_pedido deja en pedido_eventos por cada pedido retenido.</summary>
    public const string MotivoBaja = "Retenido: la empresa fue dada de baja por falta de pago.";

    /// <summary>
    /// Guarda lo que el contexto tenga pendiente — la baja de la empresa — junto con la retención de sus
    /// pedidos, en una sola transacción (GuardarComoAsync): o quedan las dos cosas o ninguna. Devuelve
    /// cuántos pedidos retuvo. Si la empresa ya estaba inactiva vuelve a pasar el rastrillo: no hace
    /// nada cuando no queda nada pendiente, y recoge lo que haya quedado de una baja anterior a esta regla.
    ///
    /// Qué pasa con la ruta de cada pedido retenido:
    ///  - `planificada`: sale del armado (se borra su fila de parada_pedidos). La parada que queda sin
    ///    ningún pedido se elimina y el resto se renumera, así el cierre de planificación sigue
    ///    funcionando para los pedidos de las demás empresas.
    ///  - `en_curso`: igual que una cancelación con el repartidor en la calle (acta changelog 4.8). La
    ///    fila queda como historia, el repartidor recibe el aviso y, si la parada se queda sin ningún
    ///    pedido por entregar, pasa a `cancelada` — así no bloquea el cierre de la ruta.
    ///  - ruta propuesta de un viaje todavía `planificada`: se elimina entera, como en
    ///    ViajeService.CancelarAsync (es solo de esa empresa). El viaje sigue activo y sin ruta, para
    ///    rearmarla cuando sus envíos se reanuden. Un viaje con la ruta ya `en_curso` no pierde la
    ///    ruta — tiene retiro firmado y puede tener entregas hechas —: sus envíos pendientes se retienen
    ///    como los de cualquier otra ruta en curso.
    /// </summary>
    public async Task<int> GuardarBajaAsync(int clienteId, Guid actorId, CancellationToken ct)
    {
        var pedidos = await db.Pedidos
            .Where(p => p.ClienteId == clienteId && TransicionesPedido.Retenibles.Contains(p.Estado))
            .ToListAsync(ct);
        var ids = pedidos.Select(p => p.Id).ToList();

        var rutasDeViaje = await QuitarRutasPropuestasAsync(clienteId, ct);

        // Las filas de una ruta cerrada son historia y no se tocan. Las de una ruta propuesta que se
        // elimina más arriba caen en cascada con ella.
        var filas = await db.ParadaPedidos
            .Include(pp => pp.Parada).ThenInclude(p => p.Ruta)
            .Where(pp => ids.Contains(pp.PedidoId) && pp.Parada.Ruta.Estado != "cerrada")
            .ToListAsync(ct);
        filas = filas.Where(pp => !rutasDeViaje.Contains(pp.Parada.RutaId)).ToList();

        await SacarDelArmadoAsync(filas.Where(pp => pp.Parada.Ruta.Estado == "planificada").ToList(), ids, ct);
        // Solo la parada todavía pendiente es el intento vigente: la fila que un reprogramado conserva
        // en la parada donde falló (D13) no le dice nada al repartidor.
        await AvisarAlRepartidorAsync(
            filas.Where(pp => pp.Parada.Ruta.Estado == "en_curso" && pp.Parada.Estado == "pendiente").ToList(),
            pedidos.ToDictionary(p => p.Id), actorId, ct);

        foreach (var pedido in pedidos) pedido.Estado = EstadoPedido.Retenido;

        // GuardarComoAsync (regla 7): cambia Pedido.Estado, fn_log_estado_pedido tiene que registrar
        // quién dio la baja y por qué quedó retenido cada pedido.
        await db.GuardarComoAsync(actorId, MotivoBaja, ct);
        return pedidos.Count;
    }

    /// <summary>Elimina la ruta propuesta, todavía en planificación, de cada viaje activo de la empresa
    /// (sus paradas caen en cascada). Devuelve los ids de las rutas eliminadas.</summary>
    private async Task<HashSet<long>> QuitarRutasPropuestasAsync(int clienteId, CancellationToken ct)
    {
        var viajes = await db.Viajes
            .Include(v => v.Ruta)
            .Where(v => v.ClienteId == clienteId && v.Estado == EstadosViaje.Activo && v.Ruta!.Estado == "planificada")
            .ToListAsync(ct);

        var eliminadas = new HashSet<long>();
        foreach (var viaje in viajes)
        {
            eliminadas.Add(viaje.Ruta!.Id);
            db.Rutas.Remove(viaje.Ruta);
            viaje.RutaId = null;
        }
        return eliminadas;
    }

    /// <summary>Saca los pedidos retenidos de las rutas en planificación. Una parada que solo los tenía a
    /// ellos se elimina, y las que quedan en esa ruta se renumeran sin huecos (el unique (ruta_id, orden)
    /// es deferrable: no choca a mitad del guardado).</summary>
    private async Task SacarDelArmadoAsync(List<ParadaPedido> filas, List<long> retenidos, CancellationToken ct)
    {
        if (filas.Count == 0) return;
        db.ParadaPedidos.RemoveRange(filas);

        var tocadas = filas.Select(pp => pp.ParadaId).ToHashSet();
        var rutaIds = filas.Select(pp => pp.Parada.RutaId).Distinct().ToList();
        var paradas = await db.RutaParadas
            .Where(p => rutaIds.Contains(p.RutaId))
            .Select(p => new
            {
                Parada = p,
                Quedan = db.ParadaPedidos.Count(pp => pp.ParadaId == p.Id && !retenidos.Contains(pp.PedidoId)),
            })
            .ToListAsync(ct);

        foreach (var deLaRuta in paradas.GroupBy(x => x.Parada.RutaId))
        {
            var vacias = deLaRuta.Where(x => x.Quedan == 0 && tocadas.Contains(x.Parada.Id)).Select(x => x.Parada).ToList();
            if (vacias.Count == 0) continue;
            db.RutaParadas.RemoveRange(vacias);

            var orden = 1;
            foreach (var parada in deLaRuta.Select(x => x.Parada).Except(vacias).OrderBy(p => p.Orden))
                parada.Orden = orden++;
        }
    }

    /// <summary>Por cada pedido retenido que el repartidor todavía lleva: el aviso (Novedad) y, si la parada
    /// se queda sin ningún pedido por entregar, la parada pasa a `cancelada`.</summary>
    private async Task AvisarAlRepartidorAsync(
        List<ParadaPedido> filas, Dictionary<long, Pedido> pedidos, Guid actorId, CancellationToken ct)
    {
        if (filas.Count == 0) return;

        var paradaIds = filas.Select(pp => pp.ParadaId).Distinct().ToList();
        var retenidos = pedidos.Keys.ToList();
        var conAlgoPorEntregar = (await db.ParadaPedidos
            .Where(pp => paradaIds.Contains(pp.ParadaId) && !retenidos.Contains(pp.PedidoId)
                && pp.Pedido.Estado != EstadoPedido.Cancelado && pp.Pedido.Estado != EstadoPedido.Retenido)
            .Select(pp => pp.ParadaId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        foreach (var fila in filas)
        {
            if (!conAlgoPorEntregar.Contains(fila.ParadaId)) fila.Parada.Estado = "cancelada";

            var pedido = pedidos[fila.PedidoId];
            db.Novedades.Add(new Novedad
            {
                RutaId = fila.Parada.RutaId,
                ParadaId = fila.ParadaId,
                PedidoId = pedido.Id,
                // Sin tipo propio: ck_novedades_tipo es una lista cerrada y para el repartidor es lo
                // mismo que una cancelación — ese pedido ya no se entrega. El texto dice por qué.
                Tipo = "cancelacion",
                Origen = "operacion",
                Descripcion = $"Operación suspendió el pedido #{pedido.Id} ({pedido.DestinatarioNombre}): " +
                              "la empresa fue dada de baja por falta de pago. No se entrega.",
                CreadaPor = actorId,
                CreadaEn = DateTimeOffset.UtcNow,
            });
        }
    }
}
