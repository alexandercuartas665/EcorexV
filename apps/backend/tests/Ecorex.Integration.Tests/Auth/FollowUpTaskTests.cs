using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests.Auth;

public sealed class FollowUpTaskTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public FollowUpTaskTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateAndComplete_FollowUpTaskOnLead()
    {
        var client = await TenantAdminClientForBAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Necesitamos una etapa y un lead.
        var stageResponse = await client.PostAsJsonAsync("/tenant/pipeline/stages",
            new CreatePipelineStageRequest($"Etapa-{suffix}", 1));
        stageResponse.EnsureSuccessStatusCode();

        var leadResponse = await client.PostAsJsonAsync("/tenant/leads", new CreateLeadRequest("Cliente Seguimiento"));
        var lead = (await leadResponse.Content.ReadFromJsonAsync<LeadDto>())!;

        // Crear seguimiento
        var create = await client.PostAsJsonAsync("/tenant/follow-ups",
            new CreateFollowUpTaskRequest(lead.Id, "Llamar al cliente", DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var task = (await create.Content.ReadFromJsonAsync<FollowUpTaskDto>())!;
        Assert.Equal(FollowUpTaskStatus.Pending, task.Status);

        // Listar pendientes del lead
        var pending = await client.GetFromJsonAsync<List<FollowUpTaskDto>>($"/tenant/follow-ups?leadId={lead.Id}&status={FollowUpTaskStatus.Pending}");
        Assert.Contains(pending!, t => t.Id == task.Id);

        // Completar
        var complete = await client.PostAsync($"/tenant/follow-ups/{task.Id}/complete", null);
        complete.EnsureSuccessStatusCode();
        var completed = (await complete.Content.ReadFromJsonAsync<FollowUpTaskDto>())!;
        Assert.Equal(FollowUpTaskStatus.Done, completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task CreateFollowUp_ForUnknownLead_Returns400()
    {
        var client = await TenantAdminClientForBAsync();
        var response = await client.PostAsJsonAsync("/tenant/follow-ups",
            new CreateFollowUpTaskRequest(Guid.CreateVersion7(), "Tarea huerfana", DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
