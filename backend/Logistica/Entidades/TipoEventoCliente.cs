namespace Logistica.Entidades;

public class TipoEventoCliente
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;

    /// <summary>pago | trato | operacion</summary>
    public string Dimension { get; set; } = null!;
    public string Descripcion { get; set; } = null!;
}
