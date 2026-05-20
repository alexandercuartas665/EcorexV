using CubotTravels.Application.Admin;
using CubotTravels.Application.Auth;
using CubotTravels.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CubotTravels.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ITenantAdminService, TenantAdminService>();
        services.AddScoped<IPlanAdminService, PlanAdminService>();
        services.AddScoped<ISubscriptionAdminService, SubscriptionAdminService>();
        services.AddScoped<IPaymentAdminService, PaymentAdminService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        return services;
    }
}
