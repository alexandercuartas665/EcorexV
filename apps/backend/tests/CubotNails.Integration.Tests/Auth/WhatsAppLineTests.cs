using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CubotNails.Application.Auth;
using CubotNails.Application.Tenancy;
using CubotNails.Domain.Enums;

namespace CubotNails.Integration.Tests.Auth;

public sealed class WhatsAppLineTests : IClassFixture<CubotApiFactory>
{
    private readonly CubotApiFactory _factory;

    public WhatsAppLineTests(CubotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TenantAdmin_CanCreateConnectAndAssignLine()
    {
        var client = await TenantAdminClientForBAsync();

        // Crear linea
        var create = await client.PostAsJsonAsync("/tenant/whatsapp-lines",
            new CreateWhatsAppLineRequest($"linea-{Guid.NewGuid():N}", "+573001112233"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var line = (await create.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(WhatsAppLineStatus.Created, line.Status);

        // Conectar (simula callback Evolution)
        var connect = await client.PostAsJsonAsync($"/tenant/whatsapp-lines/{line.Id}/status",
            new ChangeLineStatusRequest(WhatsAppLineStatus.Connected));
        connect.EnsureSuccessStatusCode();
        var connected = (await connect.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(WhatsAppLineStatus.Connected, connected.Status);
        Assert.NotNull(connected.LastConnectedAt);

        // Asignar a un usuario del tenant B (multi@ es TenantUser en B)
        var users = await client.GetFromJsonAsync<List<CubotNails.Application.Tenancy.TenantUserDto>>("/tenant/users");
        var someUser = users!.First();
        var assign = await client.PostAsJsonAsync($"/tenant/whatsapp-lines/{line.Id}/assignment",
            new AssignLineRequest(someUser.Id));
        assign.EnsureSuccessStatusCode();
        var assigned = (await assign.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(someUser.Id, assigned.AssignedToTenantUserId);
    }

    [Fact]
    public async Task Advisor_CannotManageLines_Returns403()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(CubotApiFactory.SingleEmail, CubotApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/whatsapp-lines");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> TenantAdminClientForBAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(CubotApiFactory.MultiEmail, CubotApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var switchResponse = await client.PostAsJsonAsync("/connect/switch-tenant", new SwitchTenantRequest(_factory.TenantBId));
        var switched = (await switchResponse.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.AccessToken);
        return client;
    }
}
