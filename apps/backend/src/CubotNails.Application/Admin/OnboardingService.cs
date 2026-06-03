using CubotNails.Application.Common;
using CubotNails.Application.Common.Auth;
using CubotNails.Domain.Entities;
using CubotNails.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Admin;

/// <summary>
/// Alta integral de una agencia (modulo 1.1): crea el tenant, su usuario administrador
/// (Owner) y, opcionalmente, una suscripcion, en una sola operacion con auditoria.
/// </summary>
public sealed class OnboardingService : IOnboardingService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;

    public OnboardingService(IApplicationDbContext db, IPasswordHasher passwordHasher, IAuditWriter audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<OnboardingOutcome> OnboardAsync(OnboardTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var email = request.AdminEmail.Trim().ToLowerInvariant();
        var isGoogle = !string.IsNullOrWhiteSpace(request.GoogleSubject);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new OnboardingOutcome(false, null, "El correo del administrador es obligatorio.");
        }
        if (!isGoogle && string.IsNullOrWhiteSpace(request.AdminPassword))
        {
            return new OnboardingOutcome(false, null, "Correo y clave del administrador son obligatorios.");
        }

        if (await _db.PlatformUsers.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return new OnboardingOutcome(false, null, "Ya existe un usuario con ese correo.");
        }

        if (request.PlanId is Guid planId && !await _db.SaasPlans.AnyAsync(p => p.Id == planId, cancellationToken))
        {
            return new OnboardingOutcome(false, null, "Plan inexistente.");
        }

        var tenant = new Tenant
        {
            Name = request.TenantName.Trim(),
            Country = request.Country?.Trim(),
            Currency = request.Currency?.Trim(),
            Status = TenantStatus.Active,
            Kind = TenantKind.Standard
        };

        var admin = new PlatformUser
        {
            Email = email,
            DisplayName = request.AdminDisplayName?.Trim(),
            EmailVerified = isGoogle,
            Status = PlatformUserStatus.Active,
            AuthProvider = isGoogle ? "google" : "local",
            GoogleSubject = isGoogle ? request.GoogleSubject : null,
            PasswordHash = isGoogle ? null : _passwordHasher.Hash(request.AdminPassword)
        };

        _db.Tenants.Add(tenant);
        _db.PlatformUsers.Add(admin);
        _db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            PlatformUserId = admin.Id,
            Email = email,
            TenantRole = TenantRole.Owner,
            Status = PlatformUserStatus.Active
        });

        Guid? subscriptionId = null;
        if (request.PlanId is Guid plan)
        {
            var startsAt = DateTimeOffset.UtcNow;
            var subscription = new TenantSubscription
            {
                TenantId = tenant.Id,
                PlanId = plan,
                Status = SubscriptionStatus.Active,
                BillingFrequency = request.BillingFrequency,
                StartsAt = startsAt,
                CurrentPeriodEndsAt = request.BillingFrequency == BillingFrequency.Yearly
                    ? startsAt.AddYears(1)
                    : startsAt.AddMonths(1)
            };
            _db.TenantSubscriptions.Add(subscription);
            subscriptionId = subscription.Id;
        }

        _audit.Write(actorUserId, "tenant.onboard", nameof(Tenant), tenant.Id,
            previousValue: null,
            newValue: new { tenant.Name, AdminEmail = email, HasSubscription = subscriptionId is not null },
            tenantId: tenant.Id);

        await _db.SaveChangesAsync(cancellationToken);

        return new OnboardingOutcome(true,
            new OnboardingResult(tenant.Id, tenant.Name, admin.Id, admin.Email, subscriptionId),
            null);
    }
}
