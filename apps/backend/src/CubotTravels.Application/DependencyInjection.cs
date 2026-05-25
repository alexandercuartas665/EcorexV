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
        services.AddScoped<IPaymentReceiptService, PaymentReceiptService>();
        services.AddScoped<IAuditAdminService, AuditAdminService>();
        services.AddScoped<IWompiConfigService, WompiConfigService>();
        services.AddScoped<IEvolutionMasterConfigService, EvolutionMasterConfigService>();
        services.AddScoped<IAiServerConfigService, AiServerConfigService>();
        services.AddScoped<IWompiWebhookService, WompiWebhookService>();
        services.AddScoped<IWompiCheckoutService, WompiCheckoutService>();
        services.AddScoped<IRecurringBillingService, RecurringBillingService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<ISelfSignupService, SelfSignupService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();
        services.AddScoped<IPlatformBrandingService, PlatformBrandingService>();
        services.AddScoped<IEmailConfigService, EmailConfigService>();
        services.AddScoped<IGoogleAuthConfigService, GoogleAuthConfigService>();
        services.AddScoped<Tenancy.ITenantUserService, Tenancy.TenantUserService>();
        services.AddScoped<Tenancy.IAdvisorService, Tenancy.AdvisorService>();
        services.AddScoped<Tenancy.IEvolutionConfigService, Tenancy.EvolutionConfigService>();
        services.AddScoped<Tenancy.IWhatsAppLineService, Tenancy.WhatsAppLineService>();
        services.AddScoped<Tenancy.IWhatsAppConnectorService, Tenancy.WhatsAppConnectorService>();
        services.AddScoped<Tenancy.IPipelineService, Tenancy.PipelineService>();
        services.AddScoped<Tenancy.ILeadService, Tenancy.LeadService>();
        services.AddScoped<Tenancy.ITenantApiService, Tenancy.TenantApiService>();
        services.AddScoped<Tenancy.IFollowUpTaskService, Tenancy.FollowUpTaskService>();
        services.AddScoped<Tenancy.IChatService, Tenancy.ChatService>();
        services.AddScoped<Tenancy.IMessageTemplateService, Tenancy.MessageTemplateService>();
        services.AddScoped<Tenancy.IQuoteTemplateService, Tenancy.QuoteTemplateService>();
        services.AddScoped<Tenancy.ITemplateAssetService, Tenancy.TemplateAssetService>();
        services.AddScoped<Tenancy.IQuoteRenderService, Tenancy.QuoteRenderService>();
        // Broadcaster por defecto (no-op); la app host con SignalR lo reemplaza.
        services.AddScoped<Tenancy.IChatBroadcaster, Tenancy.NoOpChatBroadcaster>();
        services.AddScoped<Tenancy.IWebhookAdminService, Tenancy.WebhookAdminService>();
        // Tunel por defecto (no-op); la app host con cloudflared lo reemplaza por singleton.
        services.AddSingleton<Tenancy.IDevTunnel, Tenancy.NoOpDevTunnel>();
        services.AddScoped<Tenancy.IChatIngestService, Tenancy.ChatIngestService>();
        services.AddScoped<Tenancy.IDashboardService, Tenancy.DashboardService>();
        services.AddScoped<Tenancy.IAiAgentService, Tenancy.AiAgentService>();
        services.AddScoped<Tenancy.IAiUsageService, Tenancy.AiUsageService>();
        services.AddScoped<Tenancy.IAiInferenceService, Tenancy.AiInferenceService>();
        services.AddScoped<Tenancy.IAutomationService, Tenancy.AutomationService>();
        return services;
    }
}
