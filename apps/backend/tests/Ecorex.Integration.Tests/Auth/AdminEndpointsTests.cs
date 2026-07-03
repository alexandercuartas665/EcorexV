using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Auth;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Integration.Tests.Auth;

public sealed class AdminEndpointsTests : IClassFixture<EcorexApiFactory>
{
    private readonly EcorexApiFactory _factory;

    public AdminEndpointsTests(EcorexApiFactory factory) => _factory = factory;

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
        var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
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
            new LoginRequest(EcorexApiFactory.SingleEmail, EcorexApiFactory.Password));
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

    [Fact]
    public async Task UpdatePlan_ChangesNameAndLimits()
    {
        var client = await SuperAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/admin/plans",
            new CreatePlanRequest("Plan Editable", "v1", 10000m, 100000m, "COP",
            [new PlanLimitInput("max_users", 5, "users")]));
        var plan = (await createResponse.Content.ReadFromJsonAsync<PlanDetail>())!;

        var update = await client.PutAsJsonAsync($"/admin/plans/{plan.Id}",
            new CreatePlanRequest("Plan Editable Pro", "v2", 25000m, 250000m, "COP",
            [
                new PlanLimitInput("max_users", 25, "users"),
                new PlanLimitInput("max_whatsapp_lines", 4, "lines")
            ]));
        update.EnsureSuccessStatusCode();
        var updated = (await update.Content.ReadFromJsonAsync<PlanDetail>())!;

        Assert.Equal("Plan Editable Pro", updated.Name);
        Assert.Equal(25000m, updated.MonthlyPrice);
        Assert.Equal(2, updated.Limits.Count);
        Assert.Contains(updated.Limits, l => l.LimitKey == "max_users" && l.LimitValue == 25);
        Assert.Contains(updated.Limits, l => l.LimitKey == "max_whatsapp_lines" && l.LimitValue == 4);

        // Persistencia: GET refleja los cambios.
        var fetched = await client.GetFromJsonAsync<PlanDetail>($"/admin/plans/{plan.Id}");
        Assert.Equal("Plan Editable Pro", fetched!.Name);
        Assert.Equal(2, fetched.Limits.Count);
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
