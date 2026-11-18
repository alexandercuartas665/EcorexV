using Ecorex.SuperAdmin.Maqueta;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>Ola 4 del cerebro digital (ADR-0183): guia para chatear con la maqueta desde Claude via el MCP.</summary>
public sealed class MaquetaClaudeGuideTests
{
    [Fact]
    public void Prompts_SePersonalizanConProyectoNotaYPantalla()
    {
        var prompts = MaquetaClaudeGuide.Prompts("tronox-sgdea", "Decisiones de menu", "rq07-terceros.html");

        Assert.All(prompts, p => Assert.Contains("tronox-sgdea", p.Prompt));
        Assert.Contains(prompts, p => p.Prompt.Contains("'Decisiones de menu'"));
        Assert.Contains(prompts, p => p.Prompt.Contains("[[rq07-terceros.html]]"));
        Assert.DoesNotContain(prompts, p => p.Prompt.Contains("{proyecto}"));
    }

    [Fact]
    public void Prompts_SinProyecto_UsanMarcador_YSinNotaNoIncluyenEseEjemplo()
    {
        var prompts = MaquetaClaudeGuide.Prompts(null);

        Assert.All(prompts, p => Assert.Contains("{proyecto}", p.Prompt));
        Assert.Equal(4, prompts.Count);
    }

    [Fact]
    public void ComandoClaudeCode_NoTraeTokenReal()
    {
        var cmd = MaquetaClaudeGuide.ClaudeCodeCommand("https://app.test/api/mcp/maquetas");

        Assert.Equal("claude mcp add --transport http ecorex-maquetas https://app.test/api/mcp/maquetas --header \"Authorization: Bearer <TU_TOKEN_DE_MAQUETAS>\"", cmd);
    }
}
