using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Tests;

/// <summary>
/// Allow-list del Navegador: coincidencia por sufijo de host y normalizacion de entradas. Caso real
/// (2026-11-12, DRON PLATAFORMAS INGENIOS): el operador guardo "https://apps.pdpartner.com" y esa
/// entrada nunca coincidia con el host "apps.pdpartner.com".
/// </summary>
public class BrowserPolicyTests
{
    [Theory]
    [InlineData("apps.pdpartner.com", "apps.pdpartner.com")]
    [InlineData("https://apps.pdpartner.com", "apps.pdpartner.com")]
    [InlineData("HTTPS://Apps.PdPartner.com/Proveedores/Forms/X.aspx?a=1#f", "apps.pdpartner.com")]
    [InlineData("http://user:pw@apps.pdpartner.com:8443/x", "apps.pdpartner.com")]
    [InlineData("*.pdpartner.com", "pdpartner.com")]
    [InlineData("  pdpartner.com.  ", "pdpartner.com")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeDomain_deja_solo_el_host(string entry, string expected)
    {
        Assert.Equal(expected, BrowserPolicy.NormalizeDomain(entry));
    }

    [Theory]
    [InlineData("apps.pdpartner.com", true)]
    [InlineData("www.apps.pdpartner.com", true)]
    [InlineData("pdpartner.com", false)]
    [InlineData("evilapps.pdpartner.com", false)]
    [InlineData("apps.pdpartner.com.evil.com", false)]
    [InlineData("", false)]
    public void Entrada_con_url_completa_coincide_por_host(string host, bool allowed)
    {
        var policy = new BrowserPolicy(true, ["https://apps.pdpartner.com/Proveedores"]);
        Assert.Equal(allowed, policy.IsAllowed(host));
    }

    [Fact]
    public void Deshabilitado_niega_aunque_el_dominio_este_en_la_lista()
    {
        var policy = new BrowserPolicy(false, ["apps.pdpartner.com"]);
        Assert.False(policy.IsAllowed("apps.pdpartner.com"));
    }

    [Fact]
    public void Lista_vacia_niega_todo()
    {
        Assert.False(BrowserPolicy.HostMatches("apps.pdpartner.com", []));
    }
}
