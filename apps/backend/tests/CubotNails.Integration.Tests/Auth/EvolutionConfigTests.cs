using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CubotNails.Application.Auth;
using CubotNails.Application.Common;
using CubotNails.Application.Tenancy;
using CubotNails.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CubotNails.Integration.Tests.Auth;

public sealed class EvolutionConfigTests : IClassFixture<CubotApiFactory>
{
    private readonly CubotApiFactory _factory;

    public EvolutionConfigTests(CubotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UpsertAndGet_MasksToken_AndStoresItEncrypted()
    {
        const string token = "evo-secret-abcdef123456";
        var client = await TenantAdminClientForBAsync();

        var put = await client.PutAsJsonAsync("/tenant/evolution",
            new UpsertEvolutionConfigRequest("https://evo.example.com", "agencia-b", token, "https://hooks.example.com/wa"));
        put.EnsureSuccessStatusCode();
        var config = (await put.Content.ReadFromJsonAsync<EvolutionConfigDto>())!;

        Assert.EndsWith("3456", config.MaskedToken);
        Assert.Contains("*", config.MaskedToken);
        Assert.DoesNotContain(token, config.MaskedToken);

        var get = await client.GetFromJsonAsync<EvolutionConfigDto>("/tenant/evolution");
        Assert.Equal("agencia-b", get!.InstanceName);

        // El token se guarda cifrado en la BD (nunca en texto plano) y se puede recuperar.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CubotNailsDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var stored = await db.TenantEvolutionConfigs
            .IgnoreQueryFilters()
            .FirstAsync(c => c.TenantId == _factory.TenantBId);

        Assert.NotEqual(token, stored.ApiTokenEncrypted);
        Assert.Equal(token, protector.Unprotect(stored.ApiTokenEncrypted));
    }

    [Fact]
    public async Task Advisor_CannotAccessEvolutionConfig_Returns403()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(CubotApiFactory.SingleEmail, CubotApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/evolution");
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
