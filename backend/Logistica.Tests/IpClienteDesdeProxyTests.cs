using System.Net;
using Logistica.Web;
using Microsoft.AspNetCore.Http;

namespace Logistica.Tests;

// auditoria_seguridad.md hallazgo 14: la IP que informa el frontend se acepta solo con el secreto.
public class IpClienteDesdeProxyTests
{
    private const string Secreto = "secreto-de-prueba-123";

    private static HeaderDictionary Cabeceras(string? secreto, string? ip)
    {
        var h = new HeaderDictionary();
        if (secreto is not null) h[IpClienteDesdeProxy.CabeceraSecreto] = secreto;
        if (ip is not null) h[IpClienteDesdeProxy.CabeceraIp] = ip;
        return h;
    }

    [Fact]
    public void Con_secreto_valido_toma_la_ip_informada()
    {
        Assert.Equal(IPAddress.Parse("200.1.2.3"), IpClienteDesdeProxy.Resolver(Cabeceras(Secreto, "200.1.2.3"), Secreto));
    }

    [Fact]
    public void Acepta_ipv6()
    {
        Assert.Equal(IPAddress.Parse("2800:810::1"), IpClienteDesdeProxy.Resolver(Cabeceras(Secreto, " 2800:810::1 "), Secreto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("otro-secreto")]
    [InlineData("secreto-de-prueba-12")]
    public void Sin_secreto_valido_ignora_la_ip(string? secreto)
    {
        Assert.Null(IpClienteDesdeProxy.Resolver(Cabeceras(secreto, "200.1.2.3"), Secreto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-una-ip")]
    [InlineData("200.1.2.3, 10.0.0.1")]
    public void Ip_invalida_se_ignora(string? ip)
    {
        Assert.Null(IpClienteDesdeProxy.Resolver(Cabeceras(Secreto, ip), Secreto));
    }
}
