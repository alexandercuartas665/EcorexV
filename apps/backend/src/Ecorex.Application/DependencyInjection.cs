using Ecorex.Application.Admin;
using Ecorex.Application.Auth;
using Ecorex.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Application;

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
        services.AddScoped<IPlatformOperatorService, PlatformOperatorService>();
        services.AddScoped<ISelfSignupService, SelfSignupService>();
        services.AddScoped<IAccountActivationService, AccountActivationService>();
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
        services.AddScoped<Tenancy.IBlockedNumberService, Tenancy.BlockedNumberService>();
        services.AddScoped<Tenancy.IMessageTemplateService, Tenancy.MessageTemplateService>();
        services.AddScoped<Tenancy.IQuoteTemplateService, Tenancy.QuoteTemplateService>();
        services.AddScoped<Tenancy.ITemplateAssetService, Tenancy.TemplateAssetService>();
        services.AddScoped<Tenancy.IQuoteRenderService, Tenancy.QuoteRenderService>();
        // Broadcaster por defecto (no-op); la app host con SignalR lo reemplaza.
        services.AddScoped<Tenancy.IChatBroadcaster, Tenancy.NoOpChatBroadcaster>();
        // Broadcaster del nucleo de tareas por defecto (no-op); la app host con SignalR lo reemplaza.
        services.AddScoped<Tenancy.ITaskBroadcaster, Tenancy.NoOpTaskBroadcaster>();
        services.AddScoped<Tenancy.IWebhookAdminService, Tenancy.WebhookAdminService>();
        // Tunel por defecto (no-op); la app host con cloudflared lo reemplaza por singleton.
        services.AddSingleton<Tenancy.IDevTunnel, Tenancy.NoOpDevTunnel>();
        services.AddScoped<Tenancy.IChatIngestService, Tenancy.ChatIngestService>();
        services.AddScoped<Tenancy.IDashboardService, Tenancy.DashboardService>();
        services.AddScoped<Tenancy.IAiAgentService, Tenancy.AiAgentService>();
        services.AddScoped<Tenancy.IAiAgentCacheService, Tenancy.AiAgentCacheService>();
        services.AddScoped<Tenancy.IAiUsageService, Tenancy.AiUsageService>();
        services.AddScoped<Tenancy.IAiInferenceService, Tenancy.AiInferenceService>();
        services.AddScoped<Tenancy.IAutomationService, Tenancy.AutomationService>();
        services.AddScoped<Tenancy.ITaskBoardService, Tenancy.TaskBoardService>();
        services.AddScoped<Tenancy.ITaskCardService, Tenancy.TaskCardService>();
        // Nucleo de tareas/proyectos (FASE 3, ADR-0013).
        services.AddScoped<Tenancy.ISequenceService, Tenancy.SequenceService>();
        services.AddScoped<Tenancy.IActivityTypeService, Tenancy.ActivityTypeService>();
        services.AddScoped<Tenancy.IProjectService, Tenancy.ProjectService>();
        services.AddScoped<Tenancy.ITaskItemService, Tenancy.TaskItemService>();
        services.AddScoped<Tenancy.IBusinessUnitService, Tenancy.BusinessUnitService>();
        // Motor de flujos BPMN (FASE 4, ADR-0014). El hook de reglas es No-Op hasta que
        // llegue la ola RulesEngine, que lo reemplazara en este registro.
        services.AddScoped<Workflows.IWorkflowEngine, Workflows.WorkflowEngine>();
        services.AddScoped<Workflows.IWorkflowRuleHook, Workflows.NoOpWorkflowRuleHook>();
        // Formularios dinamicos (FASE 4 ola 2, ADR-0015): definiciones, respuestas y tokens.
        services.AddScoped<Forms.IFormDefinitionService, Forms.FormDefinitionService>();
        services.AddScoped<Forms.IFormResponseService, Forms.FormResponseService>();
        services.AddScoped<Forms.IFormTokenService, Forms.FormTokenService>();
        // Herramientas (function calling / "MCP") que el agente de IA puede usar. Cada toolset se registra
        // tambien como IAgentToolset para que el motor de inferencia los agregue todos y filtre por agente.
        services.AddScoped<Tenancy.PipelineToolset>();
        services.AddScoped<Tenancy.IPipelineToolset>(sp => sp.GetRequiredService<Tenancy.PipelineToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.PipelineToolset>());
        // Atencion del agente por lineas de WhatsApp (binding, orquestacion, bitacora).
        services.AddScoped<Tenancy.IAiAgentLineService, Tenancy.AiAgentLineService>();
        services.AddScoped<Tenancy.IAgentConversationService, Tenancy.AgentConversationService>();
        // Cola de auto-respuesta No-Op por defecto; el host con webhook (SuperAdmin) la reemplaza.
        services.AddSingleton<Tenancy.IAgentReplyQueue, Tenancy.NoOpAgentReplyQueue>();
        return services;
    }
}
