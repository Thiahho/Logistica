namespace Logistica.Auth;

public class OpcionesJwt
{
    public string Key { get; set; } = null!;
    public string Issuer { get; set; } = "logistica";
    public string Audience { get; set; } = "logistica-clientes";
    public int AccessMinutos { get; set; } = 15;
    public int RefreshDias { get; set; } = 30;
}
