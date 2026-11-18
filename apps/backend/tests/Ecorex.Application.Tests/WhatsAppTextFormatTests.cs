using Ecorex.Application.Tenancy;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0186: formato de WhatsApp en las vistas previas (negrita, cursiva, tachado, monoespaciado).</summary>
public class WhatsAppTextFormatTests
{
    [Theory]
    [InlineData("Nuevo *Catalogo 2026* ya", "Nuevo <strong>Catalogo 2026</strong> ya")]
    [InlineData("_hola_ y ~viejo~", "<em>hola</em> y <s>viejo</s>")]
    [InlineData("codigo ```ABC-1```", "codigo <code>ABC-1</code>")]
    [InlineData("precio *$ 120.000*.", "precio <strong>$ 120.000</strong>.")]
    public void Aplica_el_formato_de_whatsapp(string input, string expected)
        => Assert.Equal(expected, WhatsAppTextFormat.ToHtml(input));

    [Theory]
    [InlineData("variable snake_case_name sin formato")]
    [InlineData("2*3*4 no es negrita")]
    [InlineData("* no * es negrita")]
    public void No_formatea_marcadores_dentro_de_palabras_o_con_espacios(string input)
        => Assert.Equal(input, WhatsAppTextFormat.ToHtml(input));

    [Fact]
    public void Codifica_el_html_antes_de_formatear()
        => Assert.Equal("&lt;script&gt; <strong>ok</strong>", WhatsAppTextFormat.ToHtml("<script> *ok*"));
}
