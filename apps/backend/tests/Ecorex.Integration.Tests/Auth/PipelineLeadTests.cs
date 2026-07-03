using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests.Auth;

public sealed class PipelineLeadTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public PipelineLeadTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task FullPipelineFlow_CreateStages_Lead_Move_Close_WithHistory()
    {
        var client = await TenantAdminClientForBAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Crear etapas
        var lead = await CreateStage(client, $"Lead-{suffix}", 1);
        var quote = await CreateStage(client, $"Cotizacion-{suffix}", 2);
        var won = await CreateStage(client, $"Cierre-{suffix}", 3, isClosedWon: true);

        // Crear lead (sin StageId -> entra en la etapa de menor orden)
        var createLead = await client.PostAsJsonAsync("/tenant/leads",
            new CreateLeadRequest("Familia Perez", "+573009998877", "Cartagena", 3500000m, "COP"));
        Assert.Equal(HttpStatusCode.Created, createLead.StatusCode);
        var created = (await createLead.Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Equal(LeadStatus.Open, created.Status);

        // Mover a Cotizacion
        var move1 = await client.PostAsJsonAsync($"/tenant/leads/{created.Id}/move", new MoveLeadRequest(quote.Id));
        move1.EnsureSuccessStatusCode();
        var moved = (await move1.Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Equal(quote.Id, moved.StageId);
        Assert.Equal(LeadStatus.Open, moved.Status);

        // Mover a Cierre (won) -> Status Won
        var move2 = await client.PostAsJsonAsync($"/tenant/leads/{created.Id}/move", new MoveLeadRequest(won.Id));
        move2.EnsureSuccessStatusCode();
        var closed = (await move2.Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Equal(LeadStatus.Won, closed.Status);

        // Historial: created + 2 movimientos
        var detail = await client.GetFromJsonAsync<LeadDetailDto>($"/tenant/leads/{created.Id}");
        Assert.Equal(created.Id, detail!.Lead.Id);
        Assert.Contains(detail.Activities, a => a.ActivityType == "lead.created");
        Assert.Equal(2, detail.Activities.Count(a => a.ActivityType == "lead.stage.changed"));
    }

    [Fact]
    public async Task Leads_AreVisibleToTenantMembers()
    {
        // Un Advisor (TenantMember) puede listar leads de su tenant.
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(EcorexApiFactory.SingleEmail, EcorexApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/leads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PipelineStageDto> CreateStage(HttpClient client, string name, int order, bool isClosedWon = false, bool isClosedLost = false)
    {
        var response = await client.PostAsJsonAsync("/tenant/pipeline/stages",
            new CreatePipelineStageRequest(name, order, isClosedWon, isClosedLost));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PipelineStageDto>())!;
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
