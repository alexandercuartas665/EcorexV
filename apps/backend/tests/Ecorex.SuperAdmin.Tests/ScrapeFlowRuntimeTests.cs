using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Runtime determinista del flujo de extraccion (Ola 3). Cubre las dos piezas NUEVAS y delicadas sin
/// necesitar colmena ni BD: la COMPILACION (flujo -> BrowserAction[], con sustitucion de variables y
/// firma del JS) y el PARSEO de las filas que devuelve un Eval. El resto del lazo (despacho por hub,
/// ingesta) reusa piezas ya probadas (IRowIngestService, el patron de AgentImportService).
/// </summary>
public class ScrapeFlowRuntimeTests
{
    private const string Secret = "secreto-de-prueba-del-agente";
    private const string Corr = "abcd1234";

    private static ScrapeFlow FlowWith(params ScrapeStep[] steps)
    {
        var flow = new ScrapeFlow { Id = Guid.NewGuid(), Name = "F", StartUrl = "https://x", ContainerId = Guid.NewGuid() };
        var order = 0;
        foreach (var s in steps) { s.Order = order++; flow.Steps.Add(s); }
        return flow;
    }

    private static readonly IReadOnlyDictionary<string, string> NoVars = new Dictionary<string, string>();

    [Fact]
    public void Compile_Navigate_substitutes_variables_in_url()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Navigate, Name = "ir", Url = "https://x/p?n={{PAGINA}}" });
        var vars = new Dictionary<string, string> { ["PAGINA"] = "3" };

        var plan = ScrapeFlowCompiler.Compile(flow, vars, Corr, Secret);

        var nav = Assert.Single(plan.Actions);
        Assert.Equal(BrowserActionKind.Navigate, nav.Kind);
        Assert.Equal("https://x/p?n=3", nav.Url);
        Assert.Null(nav.Signature); // Navigate no lleva JS: no se firma.
    }

    // El JS del operador se envuelve en un IIFE (cuerpo de funcion) para que `return` sea legal en
    // WebView2 ExecuteScriptAsync. La firma cubre el JS YA envuelto (lo exacto que corre el agente).
    private static string Wrapped(string js) => "(function(){\n" + js + "\n})()";

    [Fact]
    public void Compile_InjectScript_wraps_and_signs_the_exact_substituted_js()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.InjectScript, Name = "login", Script = "login('{{USER}}')" });
        var vars = new Dictionary<string, string> { ["USER"] = "ana" };

        var plan = ScrapeFlowCompiler.Compile(flow, vars, Corr, Secret);

        var eval = Assert.Single(plan.Actions);
        Assert.Equal(BrowserActionKind.Eval, eval.Kind);
        Assert.Equal(Wrapped("login('ana')"), eval.Script); // variable sustituida Y envuelto en IIFE.
        // La firma cubre el JS envuelto+sustituido (anti-tamper) ligado al correlationId (anti-replay).
        Assert.Equal(AgentSign.SignJs(Secret, Corr, Wrapped("login('ana')")), eval.Signature);
        Assert.True(AgentSign.Verify(Secret, Corr, eval.Script!, eval.Signature));
    }

    [Fact]
    public void Compile_wraps_extract_js_in_iife_so_return_is_valid()
    {
        // Regresion: el operador escribe `return [...]` (mental model natural). Sin envolver, un `return`
        // en el nivel superior de ExecuteScriptAsync es SyntaxError -> el Navegador devuelve null -> 0 filas.
        const string body = "return Array.from(document.querySelectorAll('.q')).map(q => ({ N: q.innerText }));";
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Extract, Name = "sacar", Script = body });

        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);

        var eval = Assert.Single(plan.Actions);
        Assert.StartsWith("(function(){", eval.Script);
        Assert.EndsWith("})()", eval.Script);
        Assert.Contains(body, eval.Script); // el cuerpo del operador queda intacto dentro del IIFE.
        Assert.True(AgentSign.Verify(Secret, Corr, eval.Script!, eval.Signature)); // firma sobre el JS envuelto.
    }

    [Fact]
    public void Compile_Extract_binds_the_eval_index_and_target_container()
    {
        var target = Guid.NewGuid();
        var flow = FlowWith(
            new ScrapeStep { Kind = ScrapeStepKind.Navigate, Name = "ir", Url = "https://x" },
            new ScrapeStep { Kind = ScrapeStepKind.Extract, Name = "sacar", Script = "rows", TargetContainerId = target, MappingJson = "{\"sku\":\"CODIGO\"}" });

        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);

        Assert.Equal(2, plan.Actions.Count);
        var bind = Assert.Single(plan.Extracts);
        Assert.Equal(1, bind.ActionIndex); // el Eval del Extract es la accion #1 (la #0 es el Navigate).
        Assert.Equal(target, bind.TargetContainerId);
        Assert.Equal(BrowserActionKind.Eval, plan.Actions[bind.ActionIndex].Kind);
    }

    [Fact]
    public void Compile_Extract_without_target_falls_back_to_flow_container()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Extract, Name = "sacar", Script = "rows" });
        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);
        Assert.Equal(flow.ContainerId, Assert.Single(plan.Extracts).TargetContainerId);
    }

    [Fact]
    public void Compile_Wait_by_selector_emits_signed_condition()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Wait, Name = "esperar", Selector = ".grid", WaitMs = 500 });
        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);

        var wait = Assert.Single(plan.Actions);
        Assert.Equal(BrowserActionKind.Wait, wait.Kind);
        Assert.Equal(500, wait.WaitMs);
        Assert.Contains("querySelector", wait.ConditionScript);
        Assert.True(AgentSign.Verify(Secret, Corr, wait.ConditionScript!, wait.Signature));
    }

    [Fact]
    public void Compile_Click_emits_signed_mousebot_json()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Click, Name = "clic", Selector = "#next" });
        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);

        var click = Assert.Single(plan.Actions);
        Assert.Equal(BrowserActionKind.Mouse, click.Kind);
        Assert.Contains("\"action\":\"click\"", click.ScriptJson);
        Assert.Contains("#next", click.ScriptJson);
        Assert.True(AgentSign.Verify(Secret, Corr, click.ScriptJson!, click.Signature));
    }

    [Fact]
    public void Compile_appends_post_step_wait_for_non_wait_steps()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Navigate, Name = "ir", Url = "https://x", WaitMs = 800 });
        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret);

        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(BrowserActionKind.Navigate, plan.Actions[0].Kind);
        Assert.Equal(BrowserActionKind.Wait, plan.Actions[1].Kind);
        Assert.Equal(800, plan.Actions[1].WaitMs);
    }

    [Fact]
    public void Compile_without_secret_rejects_a_js_step()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.InjectScript, Name = "js", Script = "doThing()" });
        var ex = Assert.Throws<ScrapeCompileException>(() => ScrapeFlowCompiler.Compile(flow, NoVars, Corr, null));
        Assert.Contains("secreto", ex.Message);
    }

    [Fact]
    public void Compile_without_secret_allows_a_navigate_only_flow()
    {
        // Un flujo sin JS (solo navegar) NO necesita secreto: no debe fallar.
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Navigate, Name = "ir", Url = "https://x" });
        var plan = ScrapeFlowCompiler.Compile(flow, NoVars, Corr, null);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void Compile_rejects_an_ai_step_in_this_wave()
    {
        var flow = FlowWith(new ScrapeStep { Kind = ScrapeStepKind.Ai, Name = "ia", Instruction = "saca precios" });
        Assert.Throws<ScrapeCompileException>(() => ScrapeFlowCompiler.Compile(flow, NoVars, Corr, Secret));
    }

    // ---- ParseRows: lo que devuelve un Eval (WebView2 serializa el resultado a JSON) ----

    [Fact]
    public void ParseRows_reads_an_array_of_objects()
    {
        var rows = ScrapeRowIngest.ParseRows("[{\"sku\":\"A1\",\"precio\":100},{\"sku\":\"B2\",\"precio\":null}]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("A1", rows[0]["sku"]);
        Assert.Equal("100", rows[0]["precio"]); // numero -> texto para la ingesta EAV.
        Assert.Null(rows[1]["precio"]);
    }

    [Fact]
    public void ParseRows_unwraps_a_double_encoded_string()
    {
        // Si el script hizo JSON.stringify, el Value llega como cadena que CONTIENE el JSON del arreglo.
        var rows = ScrapeRowIngest.ParseRows("\"[{\\\"sku\\\":\\\"A1\\\"}]\"");
        Assert.Equal("A1", Assert.Single(rows)["sku"]);
    }

    [Fact]
    public void ParseRows_accepts_a_single_object()
    {
        var rows = ScrapeRowIngest.ParseRows("{\"n\":\"1\"}");
        Assert.Equal("1", Assert.Single(rows)["n"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("no es json")]
    public void ParseRows_returns_empty_on_nothing_useful(string? value)
    {
        Assert.Empty(ScrapeRowIngest.ParseRows(value));
    }
}
