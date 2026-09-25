namespace Logistica.Dominio;

/// <summary>Dónde entra una urgencia: en una parada existente (consolidada, RF-14) o como parada nueva
/// en la posición Orden, desplazando a las pendientes que quedan después.</summary>
public record Insercion(long? ParadaConsolidadaId, int Orden, int Desplazadas);

/// <summary>Una parada pendiente de la ruta, lo único que la inserción necesita saber de ella.</summary>
public record ParadaPendiente(long Id, long UbicacionId, int Orden);

/// <summary>
/// Urgencias en una ruta en curso (acta RF-45, changelog 4.26). Lógica pura, sin base: la ventana, dónde
/// entra la urgencia y cuántas paradas desplaza. RutasController.InsertarUrgencia la usa y escribe.
/// </summary>
public static class Urgencias
{
    /// <summary>[desde, desde + minutos). A las 13:00 en punto está abierta; a las 14:00 en punto, cerrada.
    /// No cruza la medianoche: la ventana de reagrupamiento es de mediodía.</summary>
    public static bool VentanaAbierta(TimeOnly hora, TimeOnly desde, int minutos) =>
        hora >= desde && hora < desde.AddMinutes(minutos) && desde.AddMinutes(minutos) > desde;

    /// <summary>
    /// Si el destino ya es una parada pendiente de la ruta, se consolida ahí y no desplaza a nadie. Si no,
    /// entra antes de la parada indicada (desplaza a esa y a todas las pendientes que siguen) o, sin
    /// indicación, al final de las pendientes (no desplaza a nadie). null si la parada indicada no es una
    /// pendiente de la ruta.
    /// </summary>
    public static Insercion? Resolver(
        IReadOnlyList<ParadaPendiente> pendientes, long destinoUbicacionId, long? antesDeParadaId, int ultimoOrden)
    {
        var mismoDestino = pendientes.FirstOrDefault(p => p.UbicacionId == destinoUbicacionId);
        if (mismoDestino is not null) return new Insercion(mismoDestino.Id, mismoDestino.Orden, 0);

        if (antesDeParadaId is null) return new Insercion(null, ultimoOrden + 1, 0);

        var antes = pendientes.FirstOrDefault(p => p.Id == antesDeParadaId);
        if (antes is null) return null;
        return new Insercion(null, antes.Orden, pendientes.Count(p => p.Orden >= antes.Orden));
    }
}
