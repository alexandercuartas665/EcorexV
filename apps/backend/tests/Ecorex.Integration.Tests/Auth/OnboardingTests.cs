using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Auth;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests.Auth;

public sealed class OnboardingTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public OnboardingTests(EcorexApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Onboard_CreatesTenantAndAdmin_WhoCanLoginIntoTheNewTenant()
    {
        var admin = await SuperAdminClientAsync();

        // Crear un plan para asignarlo en el onboarding.
        var planResponse = await admin.PostAsJsonAsync("/admin/plans",
            new CreatePlanRequest("Plan Onboarding", null, 50000m, 500000m, "COP",
            [new PlanLimitInput("max_users", 5, "users")]));
        var plan = (await planResponse.Content.ReadFromJsonAsync<PlanDetail>())!;

        var adminEmail = $"owner-{Guid.NewGuid():N}@agencia.travels";
        const string adminPassword = "Owner123*";

        var onboard = await admin.PostAsJsonAsync("/admin/onboarding",
            new OnboardTenantRequest("Agencia Onboarded", adminEmail, adminPassword,
                AdminDisplayName: "Dueno Agencia", Country: "CO", Currency: "COP",
                PlanId: plan.Id, BillingFrequency: BillingFrequency.Monthly));

        Assert.Equal(HttpStatusCode.Created, onboard.StatusCode);
        var result = (await onboard.Content.ReadFromJsonAsync<OnboardingResult>())!;
        Assert.NotNull(result.SubscriptionId);

        // El administrador creado puede iniciar sesion y queda en su nuevo tenant.
        var newAdminClient = _factory.CreateClient();
        var login = await newAdminClient.PostAsJsonAsync("/connect/token", new LoginRequest(adminEmail, adminPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        Assert.Equal(result.TenantId, token.TenantId);
        Assert.False(token.TenantSelectionRequired);

        // Y su /connect/me lista la agencia como Owner.
        newAdminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await newAdminClient.GetFromJsonAsync<MeResponse>("/connect/me");
        Assert.Contains(me!.Tenants, t => t.TenantId == result.TenantId && t.TenantRole == nameof(TenantRole.Owner));
    }

    [Fact]
    public async Task Onboard_WithDuplicateEmail_Returns400()
    {
        var admin = await SuperAdminClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@agencia.travels";

        var first = await admin.PostAsJsonAsync("/admin/onboarding",
            new OnboardTenantRequest("Agencia Uno", email, "Owner123*"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsJsonAsync("/admin/onboarding",
            new OnboardTenantRequest("Agencia Dos", email, "Owner123*"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(EcorexApiFactory.SuperEmail, EcorexApiFactory.Password));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
