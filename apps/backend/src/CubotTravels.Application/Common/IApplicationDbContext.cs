using CubotTravels.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Common;

/// <summary>
/// Abstraccion del DbContext para los casos de uso de Application, sin acoplar a la
/// implementacion concreta de Infrastructure. Expone solo los conjuntos que la capa necesita.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<PlatformUser> PlatformUsers { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantConfiguration> TenantConfigurations { get; }
    DbSet<TenantEvolutionConfig> TenantEvolutionConfigs { get; }
    DbSet<WhatsAppLine> WhatsAppLines { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<PipelineFieldDefinition> PipelineFieldDefinitions { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadActivity> LeadActivities { get; }
    DbSet<LeadNote> LeadNotes { get; }
    DbSet<LeadFile> LeadFiles { get; }
    DbSet<FollowUpTask> FollowUpTasks { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<AiAgent> AiAgents { get; }
    DbSet<AiAgentResource> AiAgentResources { get; }
    DbSet<AiAgentPrompt> AiAgentPrompts { get; }
    DbSet<AiUsageLog> AiUsageLogs { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<SaasPlan> SaasPlans { get; }
    DbSet<SaasPlanLimit> SaasPlanLimits { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<TenantPayment> TenantPayments { get; }
    DbSet<WompiMasterConfig> WompiMasterConfigs { get; }
    DbSet<WompiWebhookEvent> WompiWebhookEvents { get; }
    DbSet<EvolutionMasterConfig> EvolutionMasterConfigs { get; }
    DbSet<AiProviderConfig> AiProviderConfigs { get; }
    DbSet<PlatformBranding> PlatformBrandings { get; }
    DbSet<EmailConfig> EmailConfigs { get; }
    DbSet<GoogleAuthConfig> GoogleAuthConfigs { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<SuperAdminAuditLog> SuperAdminAuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
