namespace Logistica.Entidades;

public class Cliente
{
    public int Id { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string? Cuit { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }

    // Indicadores internos (RF-32/RF-33). Nunca se exponen al rol 'cliente'.
    public string ColorPago { get; set; } = "rojo";
    public string ColorTrato { get; set; } = "amarillo";
    public string ColorOper { get; set; } = "amarillo";

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
