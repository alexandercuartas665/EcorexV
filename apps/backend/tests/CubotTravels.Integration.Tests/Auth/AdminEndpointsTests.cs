using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CubotTravels.Application.Admin;
using CubotTravels.Application.Auth;
using CubotTravels.Domain.Enums;
using CubotTravels.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CubotTravels.Integration.Tests.Auth;

public sealed class AdminEndpointsTests : IClassFixture<CubotApiFactory>
{
    private readonly CubotApiFactory _factory;

    public AdminEndpointsTests(CubotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task FullSuperAdminFlow_CreatesTenantPlanSubscriptionPayment_AndAudits()
    {
        var client = await SuperAdminClientAsync();

        // Crear tenant
        var createTenant = await client.PostAsJsonAsync("/admin/tenants",
            new CreateTenantRequest("Agencia Nocturna", LegalName: "Nocturna SAS", Country: "CO", Currency: "COP"));
        Assert.Equal(HttpStatusCode.Created, createTenant.StatusCode);
        var tenant = (await createTenant.Content.ReadFromJsonAsync<TenantDetail>())!;
        Assert.Equal(TenantStatus.Trial, tenant.Status);

        // Crear plan con limites
        var createPlan = await client.PostAsJsonAsync("/admin/plans",
            new CreatePlanRequest("Plan Pro", "Plan avanzado", 199000m, 1990000m, "COP",
            [
                new PlanLimitInput("max_users", 50, "users"),
                new PlanLimitInput("max_ai_tokens_monthly", 500000, "tokens", LimitEnforcementMode.Soft)
            ]));
        Assert.Equal(HttpStatusCode.Created, createPlan.StatusCode);
        var plan = (await createPlan.Content.ReadFromJsonAsync<PlanDetail>())!;
        Assert.Equal(2, plan.Limits.Count);

        // Asignar suscripcion
        var assign = await client.PostAsJsonAsync("/admin/subscriptions",
            new AssignSubscriptionRequest(tenant.Id, plan.Id, BillingFrequency.Monthly));
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
        var subscription = (await assign.Content.ReadFromJsonAsync<SubscriptionDetail>())!;
        Assert.Equal(tenant.Id, subscription.TenantId);

        // Registrar pago aprobado
        var register = await client.PostAsJsonAsync("/admin/payments",
            new RegisterPaymentRequest(tenant.Id, subscription.Id, 199000m, "COP", PaymentStatus.Approved,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1)));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var payment = (await register.Content.ReadFromJsonAsync<PaymentDetail>())!;
        Assert.NotNull(payment.ConfirmedAt);

        // Cambiar estado del tenant
        var changeStatus = await client.PostAsJsonAsync($"/admin/tenants/{tenant.Id}/status",
            new ChangeTenantStatusRequest(TenantStatus.Active, "Pago confirmado"));
        changeStatus.EnsureSuccessStatusCode();
        var updated = (await changeStatus.Content.ReadFromJsonAsync<TenantDetail>())!;
        Assert.Equal(TenantStatus.Active, updated.Status);

        // La lista incluye el tenant creado
        var list = await client.GetFromJsonAsync<List<TenantListItem>>("/admin/tenants");
        Assert.Contains(list!, t => t.Id == tenant.Id);

        // Auditoria: deben existir registros de las acciones sensibles sobre este tenant
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CubotTravelsDbContext>();
        var actions = await db.SuperAdminAuditLogs
            .Where(a => a.TenantId == tenant.Id)
            .Select(a => a.ActionName)
            .ToListAsync();

        Assert.Contains("tenant.create", actions);
        Assert.Contains("subscription.assign", actions);
        Assert.Contains("payment.register", actions);
        Assert.Contains("tenant.change-status", actions);
    }

    [Fact]
    public async Task AdminEndpoint_ForTenantUser_Returns403()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(CubotApiFactory.SingleEmail, CubotApiFactory.Password));
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var forbidden = await client.GetAsync("/admin/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task AssignSubscription_WithUnknownTenant_Returns400()
    {
        var client = await SuperAdminClientAsync();
        var response = await client.PostAsJsonAsync("/admin/subscriptions",
            new AssignSubscriptionRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), BillingFrequency.Monthly));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(CubotApiFactory.SuperEmail, CubotApiFactory.Password));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
