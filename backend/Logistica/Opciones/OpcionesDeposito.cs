namespace Logistica.Opciones;

/// <summary>Origen fijo de todos los pedidos (Pedido.OrigenUbicacionId). Sembrado en desarrollo
/// por DatosSemilla; en producción es la dirección real del depósito.</summary>
public class OpcionesDeposito
{
    public string CalleNumero { get; set; } = null!;
    public string Localidad { get; set; } = null!;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
}
