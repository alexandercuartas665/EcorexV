using System.Text.Json;
using Ecorex.Application.Forms;

namespace Ecorex.Application.Tests;

/// <summary>Flag opt-in "allowFreeText" del lookup (OptionsJson): lectura tolerante y escritura que conserva
/// las demas claves.</summary>
public class FormLookupFreeTextTests
{
    [Theory]
    [InlineData("""{"allowFreeText":true}""", true)]
    [InlineData("""{"allowFreeText":true,"otra":1}""", true)]
    [InlineData("""{"allowFreeText":false}""", false)]
    [InlineData("""{"allowFreeText":"true"}""", false)]
    [InlineData("""[{"value":"a","label":"A"}]""", false)]
    [InlineData("no es json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEnabled_SoloConTrueBooleano(string? json, bool esperado)
        => Assert.Equal(esperado, FormLookupFreeText.IsEnabled(json));

    [Fact]
    public void Set_PoneYQuita_ConservandoOtrasClaves()
    {
        var on = FormLookupFreeText.Set("""{"height":300}""", true)!;
        Assert.True(FormLookupFreeText.IsEnabled(on));
        using (var doc = JsonDocument.Parse(on))
        {
            Assert.Equal(300, doc.RootElement.GetProperty("height").GetInt32());
        }

        var off = FormLookupFreeText.Set(on, false)!;
        Assert.False(FormLookupFreeText.IsEnabled(off));
        Assert.Equal("""{"height":300}""", off);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""[{"value":"a"}]""")]
    [InlineData("roto")]
    public void Set_SobreVacioOArreglo_ParteDeObjetoNuevo(string? json)
    {
        Assert.Equal("""{"allowFreeText":true}""", FormLookupFreeText.Set(json, true));
        Assert.Null(FormLookupFreeText.Set(json, false));
    }
}
