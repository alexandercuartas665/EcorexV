using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Voice;
using Ecorex.Infrastructure.Persistence;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Ola 5 (ADR-0190): forma del agente Retell que crea ECOREX en modo compuesto. Se valida el CUERPO HTTP saliente
/// (sin red real) de /create-retell-llm y /create-agent:
///   - con opciones de llamada de flujo: end_call en general_tools, voicemail_option hangup y topes de
///     silencio/duracion (acotados a los minimos/maximos de Retell);
///   - sin opciones: el payload de siempre (no altera a los demas consumidores).
/// RetellApiClient es internal: se instancia por reflexion y se usa por su interfaz publica IRetellApiClient.
/// </summary>
public class RetellApiClientAgentShapeTests
{
    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private static IRetellApiClient NewClient(HttpMessageHandler handler)
    {
        var type = typeof(EcorexDbContext).Assembly.GetType("Ecorex.Infrastructure.Retell.RetellApiClient", throwOnError: true)!;
        return (IRetellApiClient)Activator.CreateInstance(
            type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { new HttpClient(handler) }, null)!;
    }

    [Fact]
    public async Task CreateLlm_con_EndCallTool_agrega_end_call_en_general_tools()
    {
        var handler = new CapturingHandler("{\"llm_id\":\"llm_1\"}");
        var res = await NewClient(handler).CreateLlmAsync("key", new RetellCreateLlmRequest("prompt", EndCallTool: true));

        Assert.True(res.Ok);
        Assert.EndsWith("/create-retell-llm", handler.LastUrl);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var tools = doc.RootElement.GetProperty("general_tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("end_call", tools[0].GetProperty("type").GetString());
        Assert.Equal("end_call", tools[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateLlm_sin_opciones_no_manda_general_tools()
    {
        var handler = new CapturingHandler("{\"llm_id\":\"llm_1\"}");
        await NewClient(handler).CreateLlmAsync("key", new RetellCreateLlmRequest("prompt"));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.False(doc.RootElement.TryGetProperty("general_tools", out _));
    }

    [Fact]
    public async Task CreateAgent_forma_de_flujo_cuelga_en_buzon_y_pone_topes()
    {
        var handler = new CapturingHandler("{\"agent_id\":\"ag_1\"}");
        var o = RetellAgentOptions.WorkflowCall;
        var res = await NewClient(handler).CreateAgentAsync("key", new RetellCreateAgentRequest(
            "llm_1", "voice", "es-419", "ECOREX voz",
            VoicemailHangup: o.VoicemailHangup, EndCallAfterSilenceMs: o.EndCallAfterSilenceMs, MaxCallDurationMs: o.MaxCallDurationMs));

        Assert.True(res.Ok);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        Assert.Equal("hangup", root.GetProperty("voicemail_option").GetProperty("action").GetProperty("type").GetString());
        Assert.Equal(20_000, root.GetProperty("end_call_after_silence_ms").GetInt32());
        Assert.Equal(600_000, root.GetProperty("max_call_duration_ms").GetInt32());
    }

    [Fact]
    public async Task CreateAgent_acota_los_topes_a_los_limites_de_Retell()
    {
        var handler = new CapturingHandler("{\"agent_id\":\"ag_1\"}");
        await NewClient(handler).CreateAgentAsync("key", new RetellCreateAgentRequest(
            "llm_1", "voice", EndCallAfterSilenceMs: 1_000, MaxCallDurationMs: 99_000_000));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(10_000, doc.RootElement.GetProperty("end_call_after_silence_ms").GetInt32());
        Assert.Equal(7_200_000, doc.RootElement.GetProperty("max_call_duration_ms").GetInt32());
    }

    [Fact]
    public async Task CreateAgent_sin_opciones_mantiene_el_payload_de_siempre()
    {
        var handler = new CapturingHandler("{\"agent_id\":\"ag_1\"}");
        await NewClient(handler).CreateAgentAsync("key", new RetellCreateAgentRequest("llm_1", "voice"));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("voicemail_option", out _));
        Assert.False(root.TryGetProperty("end_call_after_silence_ms", out _));
        Assert.False(root.TryGetProperty("max_call_duration_ms", out _));
    }
}
