using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;
using Ecorex.Application.Tenancy;

namespace Ecorex.Integration.Tests.Auth;

public sealed class ChatIngestTests : IClassFixture<EcorexApiFactory>
{
    private const string EvolutionToken = "evo-webhook-token-xyz";
    private readonly EcorexApiFactory _factory;

    public ChatIngestTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Webhook_IngestsMessageIdempotently_AndAdvisorCanRead()
    {
        var admin = await TenantAdminClientForBAsync();

        // Configurar Evolution (define el token del webhook) para el tenant B.
        var put = await admin.PutAsJsonAsync("/tenant/evolution",
            new UpsertEvolutionConfigRequest("https://evo.example.com", "agencia-b-chat", EvolutionToken));
        put.EnsureSuccessStatusCode();

        var anon = _factory.CreateClient();
        var externalId = $"msg-{Guid.NewGuid():N}";
        var payload = new IngestMessageRequest("+573001234567", "Cliente Chat", externalId, "Hola, quiero info de un viaje");

        // Primera ingesta -> Accepted
        var first = await PostWebhook(anon, payload, EvolutionToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Reenvio del mismo evento -> idempotente (no duplica)
        var second = await PostWebhook(anon, payload, EvolutionToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Token invalido -> 401
        var bad = await PostWebhook(anon, payload with { ExternalMessageId = $"msg-{Guid.NewGuid():N}" }, "token-malo");
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        // El asesor autenticado ve la conversacion con un solo mensaje (idempotencia)
        var conversations = await admin.GetFromJsonAsync<List<ConversationDto>>("/tenant/conversations");
        var convo = conversations!.Single(c => c.ContactPhone == "+573001234567");
        var messages = await admin.GetFromJsonAsync<List<MessageDto>>($"/tenant/conversations/{convo.Id}/messages");
        Assert.Single(messages!);
    }

    private Task<HttpResponseMessage> PostWebhook(HttpClient client, IngestMessageRequest payload, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/evolution/{_factory.TenantBId}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Webhook-Token", token);
        return client.SendAsync(request);
    }

    private async Task<HttpClient> TenantAdminClientForBAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(EcorexApiFactory.MultiEmail, EcorexApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var switchResponse = await client.PostAsJsonAsync("/connect/switch-tenant", new SwitchTenantRequest(_factory.TenantBId));
        var switched = (await switchResponse.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.AccessToken);
        return client;
    }
}
