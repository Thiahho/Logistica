using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Logistica.Opciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Logistica.Servicios;

public record DesglosePrecio(
    decimal PrecioBase,
    decimal RecargoKm,
    decimal RecargoUrgencia,
    decimal DescuentoRuta,
    decimal Peajes,
    decimal Total,
    decimal? KmCobrados,
    string? KmFuente,
    // B3, definición J (acta changelog 4.21): descuento del rango efectivo del cliente.
    decimal DescuentoRango = 0m);

/// <summary>De qué depende la tarifa de un pedido, una vez fijado el vehículo de la ruta.</summary>
public record ClaveTarifa(int ClienteId, int ZonaId, DateOnly Fecha);

/// <summary>
/// construccion_v1.md §6. Resuelve precio_base con la función tarifa_vigente ya presente en la
/// base (Migrations/20260828170859_ReglasDeBaseDeDatos.cs) en vez de reimplementar en C# la
/// resolución cliente→lista general: sería lógica de negocio duplicada (regla §3.3).
/// </summary>
public class PrecioService(LogisticaDbContext db, IOptions<OpcionesPrecio> opciones)
{
    public async Task<DesglosePrecio> CotizarAsync(
        int clienteId,
        int zonaId,
        DateOnly fecha,
        bool urgente,
        decimal peajes,
        bool descuentoRuta,
        string tipoVehiculo,
        decimal? precioManual = null,
        DistanciaResuelta? distancia = null,
        CancellationToken ct = default)
    {
        ValidarPeajes(peajes);

        // B9 (Anexo I §4, "+40 km → Cotización"): precio_manual reemplaza solo el origen de
        // precio_base cuando la zona no tiene tarifa cargada — recargo/descuento/peajes se
        // siguen aplicando encima igual, la fórmula de construccion_v1.md §6 no cambia.
        var precioBase = precioManual ?? await db.Database
            .SqlQuery<decimal?>($"select tarifa_vigente({clienteId}, {zonaId}, {fecha}, {tipoVehiculo}) as \"Value\"")
            .SingleAsync(ct);

        if (precioBase is null) throw SinTarifa(zonaId, tipoVehiculo);

        // Un precio manual (B9) no es tarifa general: no suma descuento por rango.
        decimal? pctRango = precioManual is null
            ? await PctDescuentoRangoAsync(clienteId, zonaId, fecha, tipoVehiculo, ct)
            : null;
        return Desglosar(precioBase.Value, urgente, peajes, descuentoRuta, pctRango, distancia, opciones.Value);
    }

    private static void ValidarPeajes(decimal peajes)
    {
        if (peajes < 0)
            throw new InvalidOperationException("Peajes no puede ser negativo.");
    }

    private static InvalidOperationException SinTarifa(int zonaId, string tipoVehiculo) => new(
        $"No hay tarifa vigente para la zona {zonaId} en {tipoVehiculo}. Cargá la tarifa en /tarifas o fijá un precio manual en el pedido.");

    private record TarifaDeClave(int ClienteId, int ZonaId, DateOnly Fecha, decimal? Precio);
    internal record TarifaPropia(int ClienteId, int ZonaId, DateOnly VigenteDesde, DateOnly? VigenteHasta);
    internal record RangoDeCliente(int Id, string RangoCalculado, short RangoAjuste, DateOnly? RangoAjusteVence);

    /// <summary>
    /// Lo que CotizarAsync le pregunta a la base por cada pedido (tarifa vigente, si el cliente tiene
    /// tarifa propia, su rango y la tabla de rangos), leído una sola vez para todas las `claves`: cuatro
    /// consultas en total, sea cual sea la cantidad de pedidos, en vez de hasta cuatro por pedido. Con
    /// Postgres local no se nota; contra un Postgres gestionado cada ida y vuelta es latencia de red
    /// (mismo motivo que TarifaService.PreciosGeneralesAsync). Las claves son las de los pedidos SIN
    /// precio manual: los otros no consultan nada, igual que en CotizarAsync.
    ///
    /// La tarifa sale de la misma función tarifa_vigente, no de una reimplementación.
    /// </summary>
    public async Task<LoteDePrecios> PrepararLoteAsync(
        IEnumerable<ClaveTarifa> claves, string tipoVehiculo, CancellationToken ct = default)
    {
        var distintas = claves.Distinct().ToList();
        if (distintas.Count == 0)
            return new LoteDePrecios(tipoVehiculo, opciones.Value, [], [], [], []);

        var clientes = distintas.Select(c => c.ClienteId).ToArray();
        var zonas = distintas.Select(c => c.ZonaId).ToArray();
        var fechas = distintas.Select(c => c.Fecha).ToArray();
        var tarifas = await db.Database
            .SqlQuery<TarifaDeClave>($"""
                select k.cliente_id as "ClienteId", k.zona_id as "ZonaId", k.fecha as "Fecha",
                       tarifa_vigente(k.cliente_id, k.zona_id, k.fecha, {tipoVehiculo}) as "Precio"
                from unnest({clientes}, {zonas}, {fechas}) as k(cliente_id, zona_id, fecha)
                """)
            .ToListAsync(ct);

        // Candidatas a tarifa propia de cualquiera de las claves; cuál aplica a cada una (cliente, zona
        // y vigencia a su fecha) se decide en memoria con la misma condición que PctDescuentoRangoAsync.
        var idsClientes = clientes.Distinct().ToList();
        var idsZonas = zonas.Distinct().ToList();
        var primera = fechas.Min();
        var ultima = fechas.Max();
        var propias = await db.Tarifas.AsNoTracking()
            .Where(t => t.ClienteId != null && idsClientes.Contains(t.ClienteId.Value) && idsZonas.Contains(t.ZonaId)
                && t.TipoVehiculo == tipoVehiculo && t.VigenteDesde <= ultima
                && (t.VigenteHasta == null || t.VigenteHasta >= primera))
            .Select(t => new TarifaPropia(t.ClienteId!.Value, t.ZonaId, t.VigenteDesde, t.VigenteHasta))
            .ToListAsync(ct);

        var rangosDeClientes = await db.Clientes.AsNoTracking()
            .Where(c => idsClientes.Contains(c.Id))
            .Select(c => new RangoDeCliente(c.Id, c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence))
            .ToListAsync(ct);
        var rangos = await db.Rangos.AsNoTracking().ToListAsync(ct);

        return new LoteDePrecios(
            tipoVehiculo, opciones.Value,
            tarifas.ToDictionary(t => new ClaveTarifa(t.ClienteId, t.ZonaId, t.Fecha), t => t.Precio),
            propias, rangosDeClientes.ToDictionary(c => c.Id), rangos);
    }

    /// <summary>
    /// Cotiza en memoria, sin tocar la base, los pedidos cuyas claves se leyeron en PrepararLoteAsync.
    /// Mismo resultado y mismas fallas (InvalidOperationException con el mismo mensaje, en el mismo orden
    /// de validación) que CotizarAsync para el mismo pedido.
    /// </summary>
    public sealed class LoteDePrecios
    {
        private readonly string _tipoVehiculo;
        private readonly OpcionesPrecio _factores;
        private readonly Dictionary<ClaveTarifa, decimal?> _tarifas;
        private readonly List<TarifaPropia> _propias;
        private readonly Dictionary<int, RangoDeCliente> _clientes;
        private readonly List<Rango> _rangos;

        internal LoteDePrecios(
            string tipoVehiculo, OpcionesPrecio factores, Dictionary<ClaveTarifa, decimal?> tarifas,
            List<TarifaPropia> propias, Dictionary<int, RangoDeCliente> clientes, List<Rango> rangos)
        {
            _tipoVehiculo = tipoVehiculo;
            _factores = factores;
            _tarifas = tarifas;
            _propias = propias;
            _clientes = clientes;
            _rangos = rangos;
        }

        public DesglosePrecio Cotizar(
            int clienteId, int zonaId, DateOnly fecha, bool urgente, decimal peajes, bool descuentoRuta,
            decimal? precioManual = null, DistanciaResuelta? distancia = null)
        {
            ValidarPeajes(peajes);

            var precioBase = precioManual ?? _tarifas[new ClaveTarifa(clienteId, zonaId, fecha)];
            if (precioBase is null) throw SinTarifa(zonaId, _tipoVehiculo);

            decimal? pctRango = precioManual is null ? PctDescuentoRango(clienteId, zonaId, fecha) : null;
            return Desglosar(precioBase.Value, urgente, peajes, descuentoRuta, pctRango, distancia, _factores);
        }

        private decimal PctDescuentoRango(int clienteId, int zonaId, DateOnly fecha)
        {
            var tarifaPropia = _propias.Any(t => t.ClienteId == clienteId && t.ZonaId == zonaId
                && t.VigenteDesde <= fecha && (t.VigenteHasta == null || t.VigenteHasta >= fecha));
            if (tarifaPropia) return 0m;

            return _clientes.TryGetValue(clienteId, out var cliente)
                ? PctDelRango(cliente.RangoCalculado, cliente.RangoAjuste, cliente.RangoAjusteVence, _rangos)
                : 0m;
        }
    }

    /// <summary>La fórmula de construccion_v1.md §6 sobre datos ya resueltos, sin tocar la base: la
    /// comparten CotizarAsync y el lote del cierre de planificación, así que un pedido cotizado solo y el
    /// mismo pedido cotizado en lote dan el mismo desglose. `pctRango` null = sin descuento por rango.</summary>
    private static DesglosePrecio Desglosar(
        decimal precioBase, bool urgente, decimal peajes, bool descuentoRuta, decimal? pctRango,
        DistanciaResuelta? distancia, OpcionesPrecio factores)
    {
        // Cada importe se redondea a centavos ANTES de sumar: las columnas son numeric(12,2) y
        // Postgres redondea cada una por separado, así que un total calculado sobre los productos
        // crudos puede quedar guardado a un centavo de la suma de sus propios componentes. Los
        // factores se aplican sobre el precio base ya redondeado, que es el que queda guardado.
        var basePrecio = ACentavos(precioBase);
        var peajesCobrados = ACentavos(peajes);
        // Anexo I §10.2-N lectura (ii): recargo proporcional al kilometraje recorrido, encima del
        // precio de zona vigente — no lo reemplaza. TramosKm vacío o distancia null (sin
        // coordenadas utilizables) => recargo_km = 0, fórmula idéntica a la de hoy.
        var recargoKm = distancia is not null
            ? ACentavos(basePrecio * FactorKm(distancia.Km, factores.TramosKm))
            : 0m;
        var recargoUrgencia = urgente ? ACentavos(basePrecio * factores.FactorUrgencia) : 0m;
        var descuento = descuentoRuta ? ACentavos(basePrecio * factores.FactorDescuentoRuta) : 0m;
        var descuentoRango = pctRango is { } pct ? ACentavos(basePrecio * pct / 100m) : 0m;
        var total = basePrecio + recargoKm + recargoUrgencia - descuento - descuentoRango + peajesCobrados;

        return new DesglosePrecio(
            basePrecio, recargoKm, recargoUrgencia, descuento, peajesCobrados, total,
            distancia?.Km, distancia?.Fuente, descuentoRango);
    }

    /// <summary>Mismo redondeo que numeric(12,2) de Postgres: el medio centavo se aleja del cero. El
    /// de decimal.Round por defecto es el bancario (al par), que no coincide con el de la base.</summary>
    private static decimal ACentavos(decimal importe) =>
        decimal.Round(importe, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Definición J (acta changelog 4.21; diseño_e2_rangos_liquidacion.md §3.5): el % de descuento del
    /// rango efectivo del cliente, hoy. Solo si el precio sale de la lista general: un cliente con
    /// tarifa propia para esa zona y vehículo ya tiene su precio negociado.
    /// </summary>
    private async Task<decimal> PctDescuentoRangoAsync(
        int clienteId, int zonaId, DateOnly fecha, string tipoVehiculo, CancellationToken ct)
    {
        var tarifaPropia = await db.Tarifas.AnyAsync(t => t.ClienteId == clienteId && t.ZonaId == zonaId
            && t.TipoVehiculo == tipoVehiculo && t.VigenteDesde <= fecha
            && (t.VigenteHasta == null || t.VigenteHasta >= fecha), ct);
        if (tarifaPropia) return 0m;

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => new { c.RangoCalculado, c.RangoAjuste, c.RangoAjusteVence })
            .SingleOrDefaultAsync(ct);
        if (cliente is null) return 0m;

        var rangos = await db.Rangos.AsNoTracking().ToListAsync(ct);
        return PctDelRango(cliente.RangoCalculado, cliente.RangoAjuste, cliente.RangoAjusteVence, rangos);
    }

    private static decimal PctDelRango(string calculado, short ajuste, DateOnly? ajusteVence, IReadOnlyList<Rango> rangos)
    {
        var efectivo = RangosCliente.Efectivo(calculado, ajuste, ajusteVence, Reloj.HoyLocal(), rangos);
        return rangos.SingleOrDefault(r => r.Codigo == efectivo)?.DescuentoPct ?? 0m;
    }

    /// <summary>Último tramo cuyo DesdeKm &lt;= km — semiabierto [DesdeKm, siguiente), mismo
    /// criterio que Zona.KmDesde/KmHasta. Pura y estática: testeable sin base, sin tirar nunca
    /// (tramos vacíos o km por debajo del primer tramo => 0).</summary>
    private static decimal FactorKm(decimal km, TramoKm[] tramos)
    {
        var vigente = tramos
            .Where(t => t.DesdeKm <= km)
            .OrderByDescending(t => t.DesdeKm)
            .FirstOrDefault();
        return vigente?.Porcentaje ?? 0m;
    }
}
