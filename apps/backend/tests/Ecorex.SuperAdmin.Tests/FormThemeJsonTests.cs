using Ecorex.SuperAdmin.Components.Shared.Forms;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Apariencia / tema del formulario (FormBuilder OLA 5). Build/Read del theme_json + ToInnerCss (variables de
/// marca + ocultar chips). El color solo se acepta como hex (no inyeccion en el bloque style).
/// </summary>
public class FormThemeJsonTests
{
    [Fact]
    public void Build_todo_por_defecto_devuelve_null()
        => Assert.Null(FormThemeJson.Build("clasico", null, false, null, false, false));

    [Fact]
    public void Build_prototipo_con_color_y_flags()
    {
        var json = FormThemeJson.Build("prototipo", "#4f46e5", hero: true, eyebrow: "  Asesoria  ", hideChips: true, cards: true);
        Assert.NotNull(json);
        var t = FormThemeJson.Read(json);
        Assert.True(t.IsPrototipo);
        Assert.Equal("#4f46e5", t.Color);
        Assert.True(t.Hero);
        Assert.Equal("Asesoria", t.Eyebrow);   // trim
        Assert.True(t.HideChips);
        Assert.True(t.Cards);
    }

    [Fact]
    public void Build_color_invalido_se_descarta()
    {
        // Un color no-hex no debe entrar al JSON (evita inyeccion); si es el unico ajuste => null.
        Assert.Null(FormThemeJson.Build("clasico", "red; } body{display:none}", false, null, false, false));
        // Con otro ajuste presente, el color invalido simplemente no aparece.
        var json = FormThemeJson.Build("prototipo", "javascript:evil", false, null, false, false);
        Assert.NotNull(json);
        Assert.Null(FormThemeJson.Read(json).Color);
    }

    [Fact]
    public void Read_default_para_json_vacio_o_ilegible()
    {
        foreach (var bad in new[] { null, "", "   ", "no es json", "[1,2]" })
        {
            var t = FormThemeJson.Read(bad);
            Assert.False(t.IsPrototipo);
            Assert.Null(t.Color);
            Assert.False(t.Hero);
            Assert.False(t.Cards);
        }
    }

    [Fact]
    public void ToInnerCss_emite_variables_de_marca()
    {
        var css = FormThemeJson.ToInnerCss(FormThemeJson.Read(FormThemeJson.Build("prototipo", "#0d9488", false, null, false, false)));
        Assert.NotNull(css);
        Assert.Contains("--brand: #0d9488", css);
        Assert.Contains("--brand-soft: color-mix(in srgb, #0d9488 14%, white)", css);
    }

    [Fact]
    public void ToInnerCss_oculta_chips_tecnicos()
    {
        var css = FormThemeJson.ToInnerCss(FormThemeJson.Read(FormThemeJson.Build("clasico", null, false, null, hideChips: true, false)));
        Assert.NotNull(css);
        Assert.Contains(".dfr-chip-tech { display: none; }", css);
    }

    [Fact]
    public void ToInnerCss_null_sin_color_ni_hideChips()
    {
        // Prototipo + cards no producen CSS dinamico (el look es CSS estatico por clase; cards es flag del renderer).
        var css = FormThemeJson.ToInnerCss(FormThemeJson.Read(FormThemeJson.Build("prototipo", null, hero: true, "x", hideChips: false, cards: true)));
        Assert.Null(css);
    }
}
