using System.Security.Claims;
using Ecorex.Application.Admin;
using Ecorex.Domain.Enums;

namespace Ecorex.Api.Endpoints;

/// <summary>Endpoints de la consola Super Admin. Todos exigen la politica SuperAdminOnly.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("SuperAdminOnly");

        // --- Tenants ---
        admin.MapGet("/tenants", async (ITenantAdminService svc, TenantStatus? status, string? search, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(status, search, ct)));

        admin.MapGet("/tenants/{id:guid}", async (Guid id, ITenantAdminService svc, CancellationToken ct) =>
        {
            var tenant = await svc.GetAsync(id, ct);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        admin.MapPost("/tenants", async (CreateTenantRequest request, ClaimsPrincipal user, ITenantAdminService svc, CancellationToken ct) =>
        {
            var tenant = await svc.CreateAsync(request, ActorId(user), ct);
            return Results.Created($"/admin/tenants/{tenant.Id}", tenant);
        });

        admin.MapPost("/tenants/{id:guid}/status", async (Guid id, ChangeTenantStatusRequest request, ClaimsPrincipal user, ITenantAdminService svc, CancellationToken ct) =>
        {
            var tenant = await svc.ChangeStatusAsync(id, request, ActorId(user), ct);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        // --- Onboarding (alta integral de agencia) ---
        admin.MapPost("/onboarding", async (OnboardTenantRequest request, ClaimsPrincipal user, IOnboardingService svc, CancellationToken ct) =>
        {
            var outcome = await svc.OnboardAsync(request, ActorId(user), ct);
            return outcome.Success
                ? Results.Created($"/admin/tenants/{outcome.Result!.TenantId}", outcome.Result)
                : Results.BadRequest(new { error = outcome.Error });
        });

        // --- Planes ---
        admin.MapGet("/plans", async (IPlanAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        admin.MapGet("/plans/{id:guid}", async (Guid id, IPlanAdminService svc, CancellationToken ct) =>
        {
            var plan = await svc.GetAsync(id, ct);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        admin.MapPost("/plans", async (CreatePlanRequest request, ClaimsPrincipal user, IPlanAdminService svc, CancellationToken ct) =>
        {
            var plan = await svc.CreateAsync(request, ActorId(user), ct);
            return Results.Created($"/admin/plans/{plan.Id}", plan);
        });

        admin.MapPut("/plans/{id:guid}", async (Guid id, CreatePlanRequest request, ClaimsPrincipal user, IPlanAdminService svc, CancellationToken ct) =>
        {
            var plan = await svc.UpdateAsync(id, request, ActorId(user), ct);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        admin.MapPost("/plans/{id:guid}/active", async (Guid id, SetPlanActiveRequest request, ClaimsPrincipal user, IPlanAdminService svc, CancellationToken ct) =>
        {
            var plan = await svc.SetActiveAsync(id, request.IsActive, ActorId(user), ct);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        // --- Suscripciones ---
        admin.MapPost("/subscriptions", async (AssignSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionAdminService svc, CancellationToken ct) =>
        {
            var subscription = await svc.AssignAsync(request, ActorId(user), ct);
            return subscription is null
                ? Results.BadRequest(new { error = "Tenant o plan inexistente." })
                : Results.Created($"/admin/subscriptions/{subscription.Id}", subscription);
        });

        admin.MapGet("/tenants/{tenantId:guid}/subscriptions", async (Guid tenantId, ISubscriptionAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListByTenantAsync(tenantId, ct)));

        // --- Pagos ---
        admin.MapPost("/payments", async (RegisterPaymentRequest request, ClaimsPrincipal user, IPaymentAdminService svc, CancellationToken ct) =>
        {
            var payment = await svc.RegisterAsync(request, ActorId(user), ct);
            return payment is null
                ? Results.BadRequest(new { error = "Suscripcion inexistente para el tenant." })
                : Results.Created($"/admin/payments/{payment.Id}", payment);
        });

        admin.MapGet("/payments", async (IPaymentAdminService svc, Guid? tenantId, PaymentStatus? status, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(tenantId, status, ct)));
    }

    private static Guid ActorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    private sealed record SetPlanActiveRequest(bool IsActive);
}
