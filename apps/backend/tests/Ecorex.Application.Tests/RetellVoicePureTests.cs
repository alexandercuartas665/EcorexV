using Ecorex.Application.Voice;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

// Funciones PURAS del motor de voz (sin llamadas reales ni BD): validacion E.164, composicion del prompt
// (el de ECOREX reemplaza el del agente Retell), directiva de objetivo, variables dinamicas y hash.
public class RetellVoicePureTests
{
    [Theory]
    [InlineData("+573001234567", true)]
    [InlineData("+12025550100", true)]
    [InlineData("3001234567", false)]     // sin +
    [InlineData("+0123", false)]          // empieza en 0
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("+57 300 123", false)]    // espacios
    public void IsE164(string? number, bool expected)
        => Assert.Equal(expected, RetellVoiceService.IsE164(number));

    [Fact]
    public void ComposePrompt_Includes_System_Extra_And_Objetivo()
    {
        var prompt = RetellVoiceService.ComposePrompt(
            systemPrompt: "Eres SARA, asesora comercial.",
            promptExtra: "Ofrece el plan Pro con 20% de descuento.",
            objetivo: nameof(ContactCallObjetivo.OfrecerProducto));

        Assert.Contains("Eres SARA", prompt);
        Assert.Contains("plan Pro", prompt);
        Assert.Contains("ofrecer el producto", prompt); // directiva del objetivo
    }

    [Fact]
    public void ComposePrompt_Empty_System_Still_Uses_Extra()
    {
        var prompt = RetellVoiceService.ComposePrompt(systemPrompt: "", promptExtra: "Solo esto.", objetivo: null);
        Assert.Contains("Solo esto.", prompt);
    }

    [Fact]
    public void ObjetivoDirective_LlenarFormulario_MentionsForm()
    {
        var d = RetellVoiceService.ObjetivoDirective(nameof(ContactCallObjetivo.LlenarFormulario));
        Assert.Contains("formulario", d);
    }

    [Fact]
    public void ObjetivoDirective_Personalizado_IsEmpty()
        => Assert.Equal("", RetellVoiceService.ObjetivoDirective(nameof(ContactCallObjetivo.Personalizado)));

    [Fact]
    public void BuildDynamicVariables_MapsContactAndObjetivo_SkipsBlanks()
    {
        var vars = RetellVoiceService.BuildDynamicVariables(
            new Dictionary<string, string?> { ["nombre"] = "Juan", ["empresa"] = "ACME", ["cargo"] = null, ["ciudad"] = "" },
            objetivo: "OfrecerProducto");

        Assert.Equal("Juan", vars["nombre"]);
        Assert.Equal("ACME", vars["empresa"]);
        Assert.Equal("OfrecerProducto", vars["objetivo"]);
        Assert.False(vars.ContainsKey("cargo"));  // null se omite
        Assert.False(vars.ContainsKey("ciudad")); // vacio se omite
    }

    [Fact]
    public void PromptHash_IsStable_AndSensitiveToChanges()
    {
        var a = RetellVoiceService.PromptHash("prompt", "voice", "es-419");
        var b = RetellVoiceService.PromptHash("prompt", "voice", "es-419");
        var c = RetellVoiceService.PromptHash("prompt-distinto", "voice", "es-419");
        var d = RetellVoiceService.PromptHash("prompt", "voice", "en-US");

        Assert.Equal(a, b);          // determinista
        Assert.NotEqual(a, c);       // cambia el prompt
        Assert.NotEqual(a, d);       // cambia el idioma
        Assert.Equal(64, a.Length);  // sha256 hex
    }
}
