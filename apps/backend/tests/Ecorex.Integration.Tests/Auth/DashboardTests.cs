using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Auth;
using Ecorex.Application.Tenancy;

namespace Ecorex.Integration.Tests.Auth;

public sealed class DashboardTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public DashboardTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_ReflectsSeededDataForAFreshTenant()
    {
        // Tenant nuevo y aislado para conteos deterministas.
        var super = await SuperAdminClientAsync();
        var adminEmail = $"dash-{Guid.NewGuid():N}@agencia.travels";
        const string password = "Dash123*";
        var onboard = await super.PostAsJsonAsync("/admin/onboarding",
            new OnboardTenantRequest("Agencia Dashboard", adminEmail, password));
        onboard.EnsureSuccessStatusCode();

        // Login como Owner del nuevo tenant.
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token", new LoginRequest(adminEmail, password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Etapas: una abierta y una terminal (won).
        await client.PostAsJsonAsync("/tenant/pipeline/stages", new CreatePipelineStageRequest("Nuevo", 1));
        var wonStageResp = await client.PostAsJsonAsync("/tenant/pipeline/stages", new CreatePipelineStageRequest("Ganado", 2, IsClosedWon: true));
        var wonStage = (await wonStageResp.Content.ReadFromJsonAsync<PipelineStageDto>())!;

        // 2 leads; uno se cierra ganado.
        var lead1Resp = await client.PostAsJsonAsync("/tenant/leads", new CreateLeadRequest("Lead Uno"));
        var lead1 = (await lead1Resp.Content.ReadFromJsonAsync<LeadDto>())!;
        await client.PostAsJsonAsync("/tenant/leads", new CreateLeadRequest("Lead Dos"));
        await client.PostAsJsonAsync($"/tenant/leads/{lead1.Id}/move", new MoveLeadRequest(wonStage.Id));

        // Un seguimiento pendiente.
        await client.PostAsJsonAsync("/tenant/follow-ups",
            new CreateFollowUpTaskRequest(lead1.Id, "Llamar", DateTimeOffset.UtcNow.AddDays(1)));

        var dashboard = await client.GetFromJsonAsync<TenantDashboardDto>("/tenant/dashboard");

        Assert.Equal(2, dashboard!.TotalLeads);
        Assert.Equal(1, dashboard.WonLeads);
        Assert.Equal(1, dashboard.OpenLeads);
        Assert.Equal(1, dashboard.PendingFollowUps);
        Assert.Equal(2, dashboard.LeadsByStage.Count);
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(EcorexApiFactory.SuperEmail, EcorexApiFactory.Password));
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
