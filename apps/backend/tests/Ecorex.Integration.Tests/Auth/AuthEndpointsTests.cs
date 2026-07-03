using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;

namespace Ecorex.Integration.Tests.Auth;

public sealed class AuthEndpointsTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public AuthEndpointsTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_SingleTenantUser_ReturnsTokenWithTenant()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, EcorexApiFactory.SingleEmail);

        Assert.False(token.TenantSelectionRequired);
        Assert.Equal(_factory.TenantAId, token.TenantId);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/connect/token",
            new LoginRequest(EcorexApiFactory.SingleEmail, "clave-incorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_MultiTenantUser_RequiresSelection_AndSwitchWorks()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, EcorexApiFactory.MultiEmail);

        Assert.True(token.TenantSelectionRequired);
        Assert.Null(token.TenantId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var switchResponse = await client.PostAsJsonAsync(
            "/connect/switch-tenant",
            new SwitchTenantRequest(_factory.TenantBId));

        switchResponse.EnsureSuccessStatusCode();
        var switched = await switchResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.Equal(_factory.TenantBId, switched!.TenantId);
    }

    [Fact]
    public async Task Me_ListsAvailableTenants()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, EcorexApiFactory.MultiEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var me = await client.GetFromJsonAsync<MeResponse>("/connect/me");

        Assert.NotNull(me);
        Assert.Equal(2, me!.Tenants.Count);
    }

    [Fact]
    public async Task TenantConfigurations_AreIsolatedByTokenTenant()
    {
        var client = _factory.CreateClient();

        // Usuario de A: solo ve la config de A.
        var tokenA = await LoginAsync(client, EcorexApiFactory.SingleEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA.AccessToken);
        var aItems = await client.GetFromJsonAsync<List<ConfigDto>>("/tenant/configurations");

        Assert.NotNull(aItems);
        Assert.Single(aItems!);
        Assert.All(aItems!, i => Assert.Equal(_factory.TenantAId, i.TenantId));

        // Usuario multi en B: ve las dos configs de B y ninguna de A.
        var multi = _factory.CreateClient();
        var tokenMulti = await LoginAsync(multi, EcorexApiFactory.MultiEmail);
        multi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenMulti.AccessToken);
        var switchResponse = await multi.PostAsJsonAsync("/connect/switch-tenant", new SwitchTenantRequest(_factory.TenantBId));
        var switched = await switchResponse.Content.ReadFromJsonAsync<TokenResponse>();
        multi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched!.AccessToken);

        var bItems = await multi.GetFromJsonAsync<List<ConfigDto>>("/tenant/configurations");
        Assert.Equal(2, bItems!.Count);
        Assert.All(bItems!, i => Assert.Equal(_factory.TenantBId, i.TenantId));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/tenant/configurations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformMe_ForTenantUser_Returns403()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, EcorexApiFactory.SingleEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/platform/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlatformMe_ForSuperAdmin_Returns200()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client, EcorexApiFactory.SuperEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/platform/me");
        response.EnsureSuccessStatusCode();
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email, Guid? tenantId = null)
    {
        var response = await client.PostAsJsonAsync("/connect/token", new LoginRequest(email, EcorexApiFactory.Password, tenantId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed record ConfigDto(Guid Id, Guid TenantId, string ConfigKey, string? ConfigValue);
}
