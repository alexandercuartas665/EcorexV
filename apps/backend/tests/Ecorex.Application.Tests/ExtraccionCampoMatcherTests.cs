using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0191: el validador decide que candidatos de la IA existen de verdad en el contenedor.
/// </summary>
public class ExtraccionCampoMatcherTests
{
    private static ExtraccionCampoMatcher Crear(string? prefijos = null) => new(new (string?, string?)[]
    {
        ("OMP0012387", "OMP0012387"),
        ("OSE0012387", "OSE0012387"),
        ("OMP0012371", "OMP0012371"),
        ("CPT0014661", "CPT0014661"),
        ("OMP0000042", "OMP0000042"),
        ("", "VACIO"),
        ("OMP0099999", ""),
    }, prefijos);

    [Fact]
    public void Exacto_ignora_guiones_espacios_y_mayusculas()
    {
        var m = Crear();
        Assert.Equal(new[] { "OMP0012371" }, m.Match(new[] { "omp-00 12371" }));
    }

    [Fact]
    public void Sin_prefijo_con_un_solo_prefijo_valido_valida_unico()
    {
        var m = Crear("OMP");
        Assert.Equal(new[] { "OMP0012387" }, m.Match(new[] { "12387" }));
    }

    [Fact]
    public void Sin_prefijo_y_sin_filtro_devuelve_varias_para_elegir()
    {
        var m = Crear();
        var res = m.Match(new[] { "OC 12387" });
        Assert.Equal(2, res.Count);
        Assert.Contains("OMP0012387", res);
        Assert.Contains("OSE0012387", res);
    }

    [Fact]
    public void Prefijo_del_candidato_que_existe_en_el_contenedor_desempata()
    {
        var m = Crear();
        Assert.Equal(new[] { "OSE0012387" }, m.Match(new[] { "OSE 12387" }));
    }

    [Fact]
    public void Numero_corto_no_empareja_por_numero()
    {
        var m = Crear();
        Assert.Empty(m.Match(new[] { "42" }));
        Assert.Equal(new[] { "OMP0000042" }, m.Match(new[] { "OMP0000042" }));   // exacto si
    }

    [Fact]
    public void No_existe_o_vacio_no_devuelve_nada()
    {
        var m = Crear("OMP,OSE");
        Assert.Empty(m.Match(new[] { "99999" }));          // fila sin valor a devolver no cuenta
        Assert.Empty(m.Match(new[] { "OMP0055555" }));
        Assert.Empty(m.Match(new string?[] { null, "", "  " }));
    }

    [Fact]
    public void Varios_candidatos_unen_coincidencias_sin_repetir()
    {
        var m = Crear("OMP");
        Assert.Equal(new[] { "OMP0012387", "OMP0012371" }, m.Match(new[] { "12387", "OMP0012387", "12371" }));
    }

    [Fact]
    public void Prefijos_validos_tambien_acotan_el_match_exacto()
    {
        Assert.Empty(Crear("OMP").Match(new[] { "OSE0012387" }));
        Assert.Equal(new[] { "OSE0012387" }, Crear("OMP, OSE").Match(new[] { "OSE0012387" }));
    }

    [Fact]
    public void Prefijo_no_permitido_queda_fuera()
    {
        var m = Crear("OSE");
        Assert.Equal(new[] { "OSE0012387" }, m.Match(new[] { "12387" }));
        Assert.Empty(m.Match(new[] { "14661" }));             // CPT no esta en los permitidos
    }
}
