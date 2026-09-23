using Logistica.Datos;
using Logistica.Dominio;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Servicios;

public record ResultadoRecalculo(int Asignadas, int SinZona, int SinCoordenadas);

/// <summary>Precio orientativo al elegir la localidad: solo la tarifa de la zona (sin recargos), por
/// tipo de vehículo. Nunca se persiste; el precio vinculante se fija al crear el envío.</summary>
public record PrecioSugerido(
    string? ZonaCodigo, string? ZonaNombre, decimal? DistanciaKm,
    decimal? Camioneta, decimal? Moto, bool RequiereCotizacion);

/// <summary>
/// Zona automática de una localidad: se mide la distancia del depósito principal (km 0) al centro de
/// la localidad y se le asigna la zona activa cuyo rango [KmDesde, KmHasta) la cubre — los rangos los
/// carga administración en /tarifas. Una zona fijada a mano (`ZonaManual`) no se pisa nunca.
/// Cascada sin tirar, como DistanciaService: sin depósito, sin coordenadas o fuera de todo rango la
/// localidad queda sin zona (en "pendientes" de /tarifas), nunca bloquea un alta.
/// </summary>
public class ZonaLocalidadService(
    LogisticaDbContext db, GeocodificacionService geocodificador, DistanciaService distancias,
    OrigenRutaService origenes, PrecioService precios)
{
    /// <summary>Nominatim admite ~1 req/s (construccion_v1.md §1): entre geocodificaciones de un
    /// recálculo masivo se espera este margen.</summary>
    private static readonly TimeSpan EsperaEntreGeocodificaciones = TimeSpan.FromMilliseconds(1100);

    /// <summary>Semiabierto [KmDesde, KmHasta): una distancia justo en la frontera es de la zona de
    /// arriba (auditoría §7). `zonas` ya viene filtrada a activas con KmDesde cargado.</summary>
    public static Zona? ZonaParaKm(IEnumerable<Zona> zonas, decimal km) =>
        zonas.FirstOrDefault(z => km >= z.KmDesde!.Value && (z.KmHasta is null || km < z.KmHasta));

    private Task<List<Zona>> ZonasConRangoAsync(CancellationToken ct) =>
        db.Zonas.AsNoTracking().Where(z => z.Activa && z.KmDesde != null).OrderBy(z => z.KmDesde).ToListAsync(ct);

    private async Task<Deposito?> DepositoAsync(CancellationToken ct)
    {
        try
        {
            return await origenes.PrincipalParaPedidosAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Resolver-o-crear una localidad (mismo criterio nombre+partido sin distinguir
    /// mayúsculas que tenía el alta manual) y, si es nueva, medirla y asignarle zona.</summary>
    public async Task<Localidad> CrearAsync(string nombre, string? partido, CancellationToken ct = default)
    {
        var existente = await db.Localidades.FirstOrDefaultAsync(l =>
            l.Nombre.ToLower() == nombre.ToLower() &&
            (partido == null ? l.Partido == null : l.Partido!.ToLower() == partido.ToLower()), ct);
        if (existente is not null) return existente;

        var nueva = new Localidad { Nombre = nombre, Partido = partido };
        db.Localidades.Add(nueva);
        await ActualizarAsync(nueva, ct);
        return nueva;
    }

    /// <summary>Mide y asigna zona (si no es manual) a una sola localidad y guarda.</summary>
    public async Task ActualizarAsync(Localidad localidad, CancellationToken ct = default)
    {
        var deposito = await DepositoAsync(ct);
        var zonas = await ZonasConRangoAsync(ct);
        await MedirAsync(localidad, deposito, ct);
        AsignarZona(localidad, zonas);
        await db.SaveChangesAsync(ct);
    }

    public async Task VolverAAutomaticaAsync(Localidad localidad, CancellationToken ct = default)
    {
        localidad.ZonaManual = false;
        await ActualizarAsync(localidad, ct);
    }

    /// <summary>`soloZona`: reasigna con el km ya guardado (cambió un rango — no toca red). Completo:
    /// vuelve a medir todo contra el depósito actual (cambió el depósito, o hay localidades sin
    /// coordenadas). Las manuales no se tocan en ningún caso.</summary>
    public async Task<ResultadoRecalculo> RecalcularTodasAsync(bool soloZona, CancellationToken ct = default)
    {
        var zonas = await ZonasConRangoAsync(ct);
        var deposito = soloZona ? null : await DepositoAsync(ct);
        var automaticas = await db.Localidades.Where(l => !l.ZonaManual).ToListAsync(ct);

        var geocodifico = false;
        foreach (var l in automaticas)
        {
            if (!soloZona)
            {
                var necesitaGeocodificar = l.Lat is null && !EsDelDeposito(l, deposito);
                if (necesitaGeocodificar && geocodifico) await Task.Delay(EsperaEntreGeocodificaciones, ct);
                await MedirAsync(l, deposito, ct);
                geocodifico |= necesitaGeocodificar;
            }
            AsignarZona(l, zonas);
        }
        await db.SaveChangesAsync(ct);

        return new ResultadoRecalculo(
            Asignadas: automaticas.Count(l => l.ZonaId is not null),
            SinZona: automaticas.Count(l => l.ZonaId is null),
            SinCoordenadas: automaticas.Count(l => l.DistanciaKmDeposito is null));
    }

    private static bool EsDelDeposito(Localidad l, Deposito? deposito) =>
        deposito?.LocalidadId is not null && deposito.LocalidadId == l.Id;

    private async Task MedirAsync(Localidad l, Deposito? deposito, CancellationToken ct)
    {
        // El propio depósito es el km 0 — no hace falta geocodificar ni rutear nada.
        if (EsDelDeposito(l, deposito))
        {
            l.DistanciaKmDeposito = 0m;
            l.DistanciaFuente = "deposito";
            return;
        }

        if (l.Lat is null || l.Lng is null)
        {
            var centro = await geocodificador.GeocodificarLocalidadAsync(l.Nombre, l.Partido, ct);
            if (centro is not null)
            {
                l.Lat = centro.Value.Lat;
                l.Lng = centro.Value.Lng;
            }
        }

        if (deposito?.Lat is null || deposito.Lng is null || l.Lat is null || l.Lng is null)
        {
            l.DistanciaKmDeposito = null;
            l.DistanciaFuente = null;
            return;
        }

        var d = await distancias.ResolverAsync(deposito.Lat, deposito.Lng, l.Lat, l.Lng, kmManual: null, ct);
        l.DistanciaKmDeposito = d is null ? null : Math.Round(d.Km, 1);
        l.DistanciaFuente = d?.Fuente;
    }

    private static void AsignarZona(Localidad l, IReadOnlyList<Zona> zonas)
    {
        if (l.ZonaManual) return;
        l.ZonaId = l.DistanciaKmDeposito is { } km ? ZonaParaKm(zonas, km)?.Id : null;
    }

    /// <summary>Precio orientativo por localidad, con la lista del cliente si tiene (`clienteId` 0 =
    /// lista general). Cada tipo de vehículo que no tenga tarifa cargada viene null.</summary>
    public async Task<PrecioSugerido?> PrecioSugeridoAsync(int localidadId, int clienteId, CancellationToken ct = default)
    {
        var l = await db.Localidades.AsNoTracking().Include(x => x.Zona)
            .SingleOrDefaultAsync(x => x.Id == localidadId, ct);
        if (l is null) return null;
        if (l.Zona is null)
            return new PrecioSugerido(null, null, l.DistanciaKmDeposito, null, null, RequiereCotizacion: true);

        var hoy = Reloj.HoyLocal();
        async Task<decimal?> Base(string tipoVehiculo)
        {
            try
            {
                return (await precios.CotizarAsync(
                    clienteId, l.Zona.Id, hoy, urgente: false, peajes: 0m, descuentoRuta: false,
                    tipoVehiculo, ct: ct)).PrecioBase;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var camioneta = await Base("camioneta");
        var moto = await Base("moto");
        return new PrecioSugerido(
            l.Zona.Codigo, l.Zona.Nombre, l.DistanciaKmDeposito, camioneta, moto,
            RequiereCotizacion: camioneta is null && moto is null);
    }
}
