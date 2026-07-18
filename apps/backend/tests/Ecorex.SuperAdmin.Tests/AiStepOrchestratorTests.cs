using Ecorex.Application.Tenancy;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Orquestador del paso de IA (Ola 4). Prueba el BUCLE acotado, la traduccion tool->accion de navegador,
/// la firma del JS, la allow-list de tools y la ingesta del resultado, todo con fakes (sin colmena, sin
/// proveedor real, sin BD): el AI Gateway, el canal del navegador y el sumidero de filas son seams.
/// </summary>
public class AiStepOrchestratorTests
{
    private const string Secret = "secreto-agente";

    // ---- Fakes ----

    private sealed class FakeAi : IAiProviderClient
    {
        private readonly Queue<AiCompletion> _script;
        public FakeAi(IEnumerable<AiCompletion> script) => _script = new(script);
        public IReadOnlyList<AiToolSpec>? LastTools { get; private set; }
        public int Calls { get; private set; }

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl,
            string model, string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTools = tools;
            // Si el guion se acaba, el modelo "no llama nada" (respuesta final vacia).
            var c = _script.Count > 0 ? _script.Dequeue() : new AiCompletion(true, "listo", null, 1, 1, Array.Empty<AiToolCall>());
            return Task.FromResult(c);
        }

        public Task<AiChatResult> CompleteAsync(AiProvider p, string k, string? b, string m, string s,
            IReadOnlyList<AiChatTurn> t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiChatResult> CompleteVisionAsync(AiProvider p, string k, string? b, string m, string s,
            IReadOnlyList<AiVisionPart> c, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeChannel : IBrowserActionChannel
    {
        public List<BrowserRequestMsg> Requests { get; } = new();
        public Task<BrowserResultMsg> ExecuteAsync(string clientId, BrowserRequestMsg request, TimeSpan timeout, CancellationToken ct = default)
        {
            Requests.Add(request);
            var results = request.Actions.Select((a, i) => new BrowserActionResult(i, a.Kind, true, "<html>ok</html>", null, null)).ToList();
            return Task.FromResult(new BrowserResultMsg(request.CorrelationId, true, results, null));
        }
        public bool TryResolve(BrowserResultMsg msg) => true;
    }

    private sealed class FakeSink : IScrapeRowSink
    {
        public int TotalRows { get; private set; }
        public Task<(int Inserted, int Updated, int Deleted)> IngestAsync(Guid containerId, Guid tenantId,
            string? mappingJson, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default)
        {
            TotalRows += rows.Count;
            return Task.FromResult((rows.Count, 0, 0));
        }
    }

    private sealed class FakeResolver(AiProviderChoice? choice, string? error) : IAiProviderResolver
    {
        public Task<(AiProviderChoice? Choice, string? Error)> ResolveAsync(Guid? providerConfigId, CancellationToken ct = default)
            => Task.FromResult((choice, error));
    }

    private sealed class FakeUsage : IAiUsageService
    {
        public Task RecordAsync(Guid? a, AiProvider p, string m, int i, int o, string s, bool ok, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiQuotaDto> GetQuotaAsync(CancellationToken ct = default) => Task.FromResult(new AiQuotaDto(0, 0, false)); // sin limite
    }

    private static AiCompletion Tool(string name, string args) =>
        new(true, null, null, 5, 5, new[] { new AiToolCall(Guid.NewGuid().ToString("N")[..6], name, args) });

    private static AiStepContext Ctx(IReadOnlyList<string> allow, int maxSteps = 8) =>
        new("bot-1", Guid.NewGuid(), "saca la tabla de precios", Guid.NewGuid(), allow, maxSteps, 90, Guid.NewGuid(), Secret);

    private static AiStepOrchestrator New(FakeAi ai, FakeChannel channel, FakeSink sink,
        AiProviderChoice? choice = null, string? providerError = null)
    {
        // Si hay error de proveedor, el resolver devuelve (null, error). Si no, un choice valido por
        // defecto (a menos que el test pase uno).
        var resolver = providerError is not null
            ? new FakeResolver(null, providerError)
            : new FakeResolver(choice ?? new AiProviderChoice(AiProvider.Claude, "k", null, "claude-x"), null);
        return new(ai, new FakeUsage(), resolver, channel, sink, NullLogger<AiStepOrchestrator>.Instance);
    }

    // ---- Tests ----

    [Fact]
    public async Task Runs_the_loop_navigating_then_saving_rows()
    {
        var ai = new FakeAi(new[]
        {
            Tool("navegar", "{\"url\":\"https://x\"}"),
            Tool("guardar_filas", "{\"filas\":[{\"sku\":\"A1\"},{\"sku\":\"B2\"}]}"),
        });
        var channel = new FakeChannel();
        var sink = new FakeSink();

        var outcome = await New(ai, channel, sink).RunAsync(Ctx(new[] { "navigate" }));

        Assert.True(outcome.Ok);
        Assert.Equal(2, outcome.Inserted);
        Assert.Equal(2, sink.TotalRows);
        Assert.Single(channel.Requests); // solo el navegar fue al navegador (guardar_filas es local).
        Assert.Equal(BrowserActionKind.Navigate, channel.Requests[0].Actions[0].Kind);
    }

    [Fact]
    public async Task Fails_clearly_when_no_ai_provider_is_configured()
    {
        var outcome = await New(new FakeAi(Array.Empty<AiCompletion>()), new FakeChannel(), new FakeSink(),
            choice: null, providerError: "No hay un proveedor de IA habilitado.").RunAsync(Ctx(Array.Empty<string>()));

        Assert.False(outcome.Ok);
        Assert.Contains("proveedor de IA", outcome.Error);
    }

    [Fact]
    public async Task Stops_at_the_step_cap_without_saving()
    {
        // El modelo insiste en navegar y nunca guarda: debe cortar en el tope y reportar que no guardo.
        var script = Enumerable.Range(0, 20).Select(_ => Tool("navegar", "{\"url\":\"https://x\"}"));
        var ai = new FakeAi(script);
        var sink = new FakeSink();

        var outcome = await New(ai, new FakeChannel(), sink).RunAsync(Ctx(new[] { "navigate" }, maxSteps: 3));

        Assert.False(outcome.Ok);
        Assert.Equal(0, sink.TotalRows);
        Assert.True(ai.Calls <= 3, $"no debe pasar del tope de pasos (fueron {ai.Calls}).");
        Assert.Contains("sin guardar", outcome.Error);
    }

    [Fact]
    public async Task Only_offers_powerful_tools_when_allow_listed()
    {
        var ai = new FakeAi(new[] { Tool("guardar_filas", "{\"filas\":[{\"n\":\"1\"}]}") });

        // Allow-list SIN eval/mouse: evaluar_js y clic NO deben ofrecerse.
        await New(ai, new FakeChannel(), new FakeSink()).RunAsync(Ctx(new[] { "navigate", "html" }));
        var offered = ai.LastTools!.Select(t => t.Name).ToHashSet();
        Assert.Contains("navegar", offered);
        Assert.Contains("leer_html", offered);
        Assert.Contains("guardar_filas", offered); // la salida siempre esta.
        Assert.DoesNotContain("evaluar_js", offered);
        Assert.DoesNotContain("clic", offered);
    }

    [Fact]
    public async Task Signs_the_js_of_an_eval_tool_before_sending_it()
    {
        var ai = new FakeAi(new[]
        {
            Tool("evaluar_js", "{\"script\":\"document.title\"}"),
            Tool("guardar_filas", "{\"filas\":[{\"t\":\"x\"}]}"),
        });
        var channel = new FakeChannel();

        var outcome = await New(ai, channel, new FakeSink()).RunAsync(Ctx(new[] { "eval" }));

        Assert.True(outcome.Ok);
        var evalReq = channel.Requests.Single();
        var action = evalReq.Actions[0];
        Assert.Equal(BrowserActionKind.Eval, action.Kind);
        // El servidor firma el JS de la IA (el agente lo rechazaria sin firma, fail-closed).
        Assert.False(string.IsNullOrEmpty(action.Signature));
        Assert.True(AgentSign.Verify(Secret, evalReq.CorrelationId, action.Script!, action.Signature));
    }
}
