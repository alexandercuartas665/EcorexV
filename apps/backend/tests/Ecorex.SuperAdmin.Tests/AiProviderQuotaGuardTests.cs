using System.Net;
using System.Text;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Ai;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Freno por cupo/saldo agotado del proveedor de IA (2026-11-16). Caso real: Gemini con el tope mensual de gasto
/// agotado respondia 429 "exceeded its monthly spending cap" y el agente reintentaba cada 2 min por conversacion
/// (3.634 errores en una noche). Ahora: el 429 de cupo NO se reintenta, el proveedor queda pausado (sin llamadas
/// HTTP) y al vencer la pausa se sondea una vez.
/// </summary>
public class AiProviderQuotaGuardTests
{
    private const string SpendingCapBody =
        "{\"error\":{\"code\":429,\"message\":\"Your project has exceeded its monthly spending cap. Please go to AI Studio to manage your project spend cap.\",\"status\":\"RESOURCE_EXHAUSTED\"}}";

    private const string OkChatBody =
        "{\"choices\":[{\"message\":{\"content\":\"hola\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}";

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpStatusCode Status { get; set; } = (HttpStatusCode)429;
        public string Body { get; set; } = SpendingCapBody;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    private static Task<AiCompletion> Ask(AiProviderClient client) =>
        client.CompleteWithToolsAsync(AiProvider.Gemini, "key", null, "gemini-2.5-flash", "sys",
            new[] { new AiToolMessage("user", "hola") }, Array.Empty<AiToolSpec>());

    [Theory]
    [InlineData(429, SpendingCapBody, true)]
    [InlineData(429, "{\"error\":{\"type\":\"insufficient_quota\",\"message\":\"You exceeded your current quota, please check your plan and billing details.\"}}", true)]
    [InlineData(400, "{\"error\":{\"message\":\"Your credit balance is too low to access the Anthropic API.\"}}", true)]
    // Limite POR MINUTO de Gemini: tambien RESOURCE_EXHAUSTED pero transitorio -> se sigue reintentando.
    [InlineData(429, "{\"error\":{\"code\":429,\"message\":\"Quota exceeded for metric generate_content_requests per minute\",\"status\":\"RESOURCE_EXHAUSTED\"}}", false)]
    [InlineData(503, "{\"error\":{\"message\":\"The model is overloaded\"}}", false)]
    public void Clasifica_cupo_agotado_vs_saturacion(int status, string body, bool expected)
    {
        Assert.Equal(expected, AiQuotaErrors.IsQuotaExhausted(status, body));
    }

    [Fact]
    public async Task Tope_de_gasto_no_se_reintenta_y_pausa_al_proveedor_sin_llamar_la_api()
    {
        var clock = new ManualClock();
        var guard = new AiProviderQuotaGuard(clock);
        var handler = new CountingHandler();
        var client = new AiProviderClient(new HttpClient(handler), guard);

        var first = await Ask(client);
        Assert.False(first.Ok);
        Assert.Equal(1, handler.Calls);                 // sin la cadena de 4 reintentos del 429 transitorio
        Assert.True(guard.IsPaused(AiProvider.Gemini, out _));
        Assert.Single(guard.ActivePauses());

        var second = await Ask(client);
        Assert.False(second.Ok);
        Assert.Contains("pausadas", second.Error);
        Assert.Equal(1, handler.Calls);                 // pausado: no sale ninguna llamada HTTP

        // Otro proveedor no se afecta.
        Assert.False(guard.IsPaused(AiProvider.Claude, out _));
    }

    [Fact]
    public async Task Al_vencer_la_pausa_sondea_y_si_hay_cupo_se_recupera()
    {
        var clock = new ManualClock();
        var guard = new AiProviderQuotaGuard(clock);
        var handler = new CountingHandler();
        var client = new AiProviderClient(new HttpClient(handler), guard);

        await Ask(client);
        Assert.True(guard.IsPaused(AiProvider.Gemini, out _));

        clock.Now += AiProviderQuotaGuard.PauseFor + TimeSpan.FromSeconds(1);
        handler.Status = HttpStatusCode.OK;
        handler.Body = OkChatBody;

        var probe = await Ask(client);
        Assert.True(probe.Ok);
        Assert.Equal(2, handler.Calls);
        Assert.False(guard.IsPaused(AiProvider.Gemini, out _));
        Assert.Empty(guard.ActivePauses());
    }

    [Fact]
    public void Trip_avisa_solo_al_inicio_de_la_caida()
    {
        var clock = new ManualClock();
        var guard = new AiProviderQuotaGuard(clock);

        Assert.True(guard.Trip(AiProvider.Gemini, "cap"));      // arranca la caida: se avisa
        clock.Now += AiProviderQuotaGuard.PauseFor + TimeSpan.FromMinutes(1);
        Assert.False(guard.Trip(AiProvider.Gemini, "cap"));     // sondeo fallido: misma caida, sin nuevo aviso
        guard.MarkHealthy(AiProvider.Gemini);
        Assert.True(guard.Trip(AiProvider.Gemini, "cap"));      // nueva caida tras recuperarse
    }
}
