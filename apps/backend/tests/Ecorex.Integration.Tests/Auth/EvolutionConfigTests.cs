using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Integration.Tests.Auth;

public sealed class EvolutionConfigTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public EvolutionConfigTests(EcorexApiFactory factory) => _factory = factory;

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
        var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
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
            new LoginRequest(EcorexApiFactory.SingleEmail, EcorexApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/evolution");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
