using Logistica.Dominio;
using System.ComponentModel.DataAnnotations;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Controllers;

/// <summary>
/// Lista general de precios (tarifas.cliente_id null — RF-09). Las tarifas por cliente se fijan
/// desde /api/clientes/{id}/tarifas/{zonaId} (ClientesController); ambos comparten TarifaService
/// para no duplicar la regla de vigencia.
/// </summary>
[ApiController]
[Route("api/tarifas")]
[Authorize(Policy = "Administracion")]
public class TarifasController(LogisticaDbContext db, TarifaService tarifas) : ControllerBase
{
    public record TarifaGeneral(
        int ZonaId, string ZonaCodigo, string ZonaNombre, int? KmDesde, int? KmHasta,
        decimal? PrecioCamioneta, decimal? PrecioMoto);

    /// <summary>Rango de km sin ninguna zona activa que lo cubra (auditoría §7, coherencia de
    /// km). HastaKm null = hueco abierto hasta el final (la zona más lejana no tiene KmHasta,
    /// caso ya cubierto — este hueco solo aparece cuando falta esa zona por completo).</summary>
    public record HuecoKm(int DesdeKm, int? HastaKm);

    public record TarifasResponse(List<TarifaGeneral> Zonas, List<HuecoKm> Huecos);

    public record FijarTarifaRequest(
        string TipoVehiculo,
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")] decimal? Precio);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var hoy = Reloj.HoyLocal();
        var zonas = await db.Zonas.AsNoTracking().OrderBy(z => z.Codigo).ToListAsync(ct);

        // Antes eran 2 round-trips por zona (tarifa_vigente para camioneta y para moto) dentro
        // del foreach. Ver TarifaService.PreciosGeneralesAsync.
        var precios = await tarifas.PreciosGeneralesAsync(hoy, ct);
        var resultado = zonas.Select(zona => new TarifaGeneral(
                zona.Id, zona.Codigo, zona.Nombre, zona.KmDesde, zona.KmHasta,
                precios.GetValueOrDefault((zona.Id, "camioneta")),
                precios.GetValueOrDefault((zona.Id, "moto"))))
            .ToList();

        var huecos = CalcularHuecos(zonas.Where(z => z.Activa));

        return Ok(new TarifasResponse(resultado, huecos));
    }

    /// <summary>Auditoría §7 (coherencia de km): un hueco es un tramo de km sin ninguna zona
    /// activa que lo cubra — distinto de "sin datos" (localidad sin dirección geocodificada,
    /// que no tiene nada que ver con esto). No bloquea nada; solo se muestra como aviso, mismo
    /// criterio que RF-16 (alertar sin impedir). El solapamiento, en cambio, sí se rechaza —
    /// ver ZonasController.ActualizarKm.</summary>
    private static List<HuecoKm> CalcularHuecos(IEnumerable<Zona> zonasActivas)
    {
        var ordenadas = zonasActivas.Where(z => z.KmDesde is not null).OrderBy(z => z.KmDesde).ToList();
        var huecos = new List<HuecoKm>();
        if (ordenadas.Count == 0) return huecos;

        if (ordenadas[0].KmDesde > 0) huecos.Add(new HuecoKm(0, ordenadas[0].KmDesde));

        for (var i = 0; i < ordenadas.Count - 1; i++)
        {
            var actual = ordenadas[i];
            var siguiente = ordenadas[i + 1];
            if (actual.KmHasta is null) break; // cobertura abierta hasta el final: no puede haber más huecos
            if (siguiente.KmDesde > actual.KmHasta)
                huecos.Add(new HuecoKm(actual.KmHasta.Value, siguiente.KmDesde));
        }

        if (ordenadas[^1].KmHasta is not null)
            huecos.Add(new HuecoKm(ordenadas[^1].KmHasta!.Value, null));

        return huecos;
    }

    [HttpPut("{zonaId:int}")]
    public async Task<IActionResult> Fijar(int zonaId, FijarTarifaRequest req, CancellationToken ct)
    {
        if (req.TipoVehiculo is not ("camioneta" or "moto")) return BadRequest("Tipo de vehículo inválido.");
        await tarifas.FijarAsync(clienteId: null, zonaId, req.TipoVehiculo, req.Precio, ct);
        return NoContent();
    }
}
