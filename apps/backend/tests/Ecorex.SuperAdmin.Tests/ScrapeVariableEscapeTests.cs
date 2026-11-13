using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Escape de variables al sustituirlas en el JS de un paso (2026-11-12). Antes el valor se pegaba crudo:
/// una clave con comilla o barra invertida rompia el script del login del DRON PLATAFORMAS INGENIOS.
/// Todos los scripts usan los placeholders entre comillas, asi que escapar para literal de cadena es seguro.
/// </summary>
public class ScrapeVariableEscapeTests
{
    private const string Secret = "secreto-de-prueba-del-agente";
    private const string Corr = "abcd1234";

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("o'brien", "o\\'brien")]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData("c:\\ruta", "c:\\\\ruta")]
    [InlineData("tick`x", "tick\\`x")]
    [InlineData("${x}", "\\${x}")]
    [InlineData("l1\nl2\r", "l1\\nl2\\r")]
    [InlineData("</script>", "<\\/script>")]
    [InlineData("", "")]
    public void JsEscape_escapa_para_literal_de_cadena(string raw, string expected)
    {
        Assert.Equal(expected, ScrapeRunContext.JsEscape(raw));
    }

    [Fact]
    public void SubstituteScript_escapa_valores_y_deja_placeholders_sin_resolver()
    {
        var ctx = new ScrapeRunContext(new Dictionary<string, string> { ["Clave"] = "p'a\\ss" });

        var js = ctx.SubstituteScript("var c='{{Clave}}';var x='@@NO_EXISTE@@';");

        Assert.Equal("var c='p\\'a\\\\ss';var x='@@NO_EXISTE@@';", js);
    }

    [Fact]
    public void SubstituteScript_navega_json_y_escapa_el_resultado()
    {
        var ctx = new ScrapeRunContext();
        ctx.Set("ROW", "{\"peticionOferta\":\"15018'92\",\"fila\":3}");

        Assert.Equal("var p='15018\\'92';var f='3';", ctx.SubstituteScript("var p='@@ROW.peticionOferta@@';var f='@@ROW.fila@@';"));
    }

    [Fact]
    public void Substitute_sin_escape_queda_igual_para_url_y_selector()
    {
        var ctx = new ScrapeRunContext(new Dictionary<string, string> { ["Q"] = "a'b" });

        Assert.Equal("https://x/?q=a'b", ctx.Substitute("https://x/?q={{Q}}"));
    }

    [Fact]
    public void Compile_InjectScript_escapa_la_variable_del_flujo()
    {
        var flow = new ScrapeFlow { Id = Guid.NewGuid(), Name = "F", StartUrl = "https://x", ContainerId = Guid.NewGuid() };
        flow.Steps.Add(new ScrapeStep { Kind = ScrapeStepKind.InjectScript, Name = "login", Script = "login('{{CLAVE}}')" });
        var vars = new Dictionary<string, string> { ["CLAVE"] = "a'b\\c" };

        var plan = ScrapeFlowCompiler.Compile(flow, vars, Corr, Secret);

        var eval = Assert.Single(plan.Actions);
        Assert.Equal(BrowserActionKind.Eval, eval.Kind);
        Assert.Contains("login('a\\'b\\\\c')", eval.Script);
    }

    [Fact]
    public void Compile_Navigate_no_escapa_la_url()
    {
        var flow = new ScrapeFlow { Id = Guid.NewGuid(), Name = "F", StartUrl = "https://x", ContainerId = Guid.NewGuid() };
        flow.Steps.Add(new ScrapeStep { Kind = ScrapeStepKind.Navigate, Name = "ir", Url = "https://x/{{P}}" });
        var vars = new Dictionary<string, string> { ["P"] = "a'b" };

        var plan = ScrapeFlowCompiler.Compile(flow, vars, Corr, Secret);

        Assert.Equal("https://x/a'b", Assert.Single(plan.Actions).Url);
    }
}
