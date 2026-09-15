using Ecorex.Application.Directorio;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests de la homologacion RUT -> Directorio publico (Capa 8, regla 2.2, Ola 3). Logica PURA: deduce
/// naturaleza desde la casilla 24, mapea nombre/ide/correo/ciudad y convierte el NIT a IDE sin DV (O3-2).
/// </summary>
public class HomologacionRutTests
{
    private const string Trib = "mod_tributaria";

    private static Dictionary<string, Dictionary<string, string>> Ficha(params (string k, string v)[] campos)
    {
        var sec = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in campos) { sec[k] = v; }
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal) { [Trib] = sec };
    }

    [Fact]
    public void Juridica_mapea_razon_social_y_nit_sin_dv()
    {
        var f = Ficha(
            ("tipo_contribuyente", "Persona juridica"),
            ("razon_social", "ACME S.A.S."),
            ("nit", "900123456"),
            ("dv", "7"),
            ("correo_rut", "info@acme.co"),
            ("ciudad_rut", "Medellin"));

        var r = HomologacionRut.Mapear(f);

        Assert.Equal(TerceroTipo.Empresa, r.Tipo);
        Assert.Equal("ACME S.A.S.", r.Publico["nombre_empresa"]);
        Assert.Equal("900123456", r.Publico["ide"]);
        Assert.Equal("info@acme.co", r.Publico["correo"]);
        Assert.Equal("Medellin", r.Publico["ciudad"]);
    }

    [Fact]
    public void Natural_mapea_nombres_apellidos_y_cedula()
    {
        var f = Ficha(
            ("tipo_contribuyente", "Persona natural"),
            ("primer_nombre", "Ana"),
            ("otros_nombres", "Maria"),
            ("primer_apellido", "Gomez"),
            ("segundo_apellido", "Ruiz"),
            ("numero_identificacion", "43123456"));

        var r = HomologacionRut.Mapear(f);

        Assert.Equal(TerceroTipo.Persona, r.Tipo);
        Assert.Equal("Ana Maria Gomez Ruiz", r.Publico["contacto"]);
        Assert.Equal("43123456", r.Publico["ide"]);
    }

    [Fact]
    public void Nit_con_dv_pegado_se_recorta()
    {
        Assert.Equal("900123456", HomologacionRut.NitAIde("900123456-7"));
        Assert.Equal("900123456", HomologacionRut.NitAIde("900123456"));
        Assert.Equal("900123456", HomologacionRut.NitAIde("  900123456-7  "));
        Assert.Null(HomologacionRut.NitAIde(null));
        Assert.Null(HomologacionRut.NitAIde("  "));
    }

    [Fact]
    public void Sin_tipo_contribuyente_infiere_por_lo_diligenciado()
    {
        var empresa = HomologacionRut.Mapear(Ficha(("razon_social", "XYZ Ltda")));
        Assert.Equal(TerceroTipo.Empresa, empresa.Tipo);

        var persona = HomologacionRut.Mapear(Ficha(("primer_nombre", "Pedro"), ("primer_apellido", "Perez")));
        Assert.Equal(TerceroTipo.Persona, persona.Tipo);
    }

    [Fact]
    public void Tipo_contribuyente_con_acento_se_reconoce()
    {
        var r = HomologacionRut.Mapear(Ficha(("tipo_contribuyente", "Persona jurídica"), ("razon_social", "ACME")));
        Assert.Equal(TerceroTipo.Empresa, r.Tipo);
    }

    [Fact]
    public void Aplicar_sobrescribe_la_seccion_destino()
    {
        var f = Ficha(("tipo_contribuyente", "Persona juridica"), ("razon_social", "ACME"), ("nit", "900123456-7"));
        var r = HomologacionRut.Aplicar(f, "mod_publica");

        Assert.True(r.Etiquetas.Count > 0);
        Assert.Equal("ACME", f["mod_publica"]["nombre_empresa"]);
        Assert.Equal("900123456", f["mod_publica"]["ide"]);
    }

    [Fact]
    public void Ficha_vacia_no_deduce_ni_mapea()
    {
        var r = HomologacionRut.Mapear(Ficha());
        Assert.Null(r.Tipo);
        Assert.Empty(r.Publico);
        Assert.Empty(r.Etiquetas);
    }
}
