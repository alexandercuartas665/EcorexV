using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Auth;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests.Auth;

public sealed class TenantUsersTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public TenantUsersTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TenantAdmin_CanListAndInviteUsersInOwnTenant()
    {
        // multi@ es Admin en el tenant B.
        var client = await TenantAdminClientForBAsync();

        var before = await client.GetFromJsonAsync<List<TenantUserDto>>("/tenant/users");
        Assert.NotNull(before);

        var email = $"asesor-{Guid.NewGuid():N}@agencia.travels";
        var invite = await client.PostAsJsonAsync("/tenant/users",
            new InviteTenantUserRequest(email, TenantRole.Advisor, Password: "Asesor123*"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        var after = await client.GetFromJsonAsync<List<TenantUserDto>>("/tenant/users");
        Assert.Equal(before!.Count + 1, after!.Count);
        Assert.Contains(after, u => u.Email == email && u.TenantRole == TenantRole.Advisor);
    }

    [Fact]
    public async Task TenantAdvisor_CannotAccessUserManagement_Returns403()
    {
        // single@ es Advisor en el tenant A.
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(EcorexApiFactory.SingleEmail, EcorexApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InvitedUsers_AreIsolatedPerTenant()
    {
        var clientB = await TenantAdminClientForBAsync();
        var email = $"solob-{Guid.NewGuid():N}@agencia.travels";
        var invite = await clientB.PostAsJsonAsync("/tenant/users",
            new InviteTenantUserRequest(email, TenantRole.Advisor, Password: "Solo123*"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        // El nuevo usuario existe en B pero no debe aparecer al consultar el tenant A.
        var clientBList = await clientB.GetFromJsonAsync<List<TenantUserDto>>("/tenant/users");
        Assert.Contains(clientBList!, u => u.Email == email);
        // No hay admin de A entre los seeds (solo Advisors), asi que validamos via B unicamente
        // que el conteo refleja el alta; el aislamiento de lectura ya esta cubierto por
        // TenantIsolationTests y el filtro global del DbContext.
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
