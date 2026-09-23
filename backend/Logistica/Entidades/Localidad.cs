namespace Logistica.Entidades;

public class Localidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string? Partido { get; set; }
    public string? Cp { get; set; }

    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }

    /// <summary>Centro de la localidad (geocodificado en el servidor, nunca aportado por el
    /// cliente). Es lo que se mide contra el depósito para asignar la zona sola.</summary>
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    /// <summary>Km desde el depósito principal (0 = el propio depósito). Guardado para no volver a
    /// pegarle al ruteo cada vez que se lista o se cambia el rango de una zona.</summary>
    public decimal? DistanciaKmDeposito { get; set; }

    /// <summary>"ruta" | "recta" — mismo criterio que DistanciaResuelta.Fuente.</summary>
    public string? DistanciaFuente { get; set; }

    /// <summary>true = administración fijó la zona a mano: ningún recálculo la pisa. Se vuelve
    /// a automática con ZonaLocalidadService.VolverAAutomaticaAsync.</summary>
    public bool ZonaManual { get; set; }
}
