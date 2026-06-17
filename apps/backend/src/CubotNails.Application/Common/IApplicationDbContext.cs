using CubotNails.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Common;

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
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadActivity> LeadActivities { get; }
    DbSet<LeadNote> LeadNotes { get; }
    DbSet<LeadFile> LeadFiles { get; }
    DbSet<FollowUpTask> FollowUpTasks { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<TenantBlockedNumber> TenantBlockedNumbers { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<QuoteTemplate> QuoteTemplates { get; }
    DbSet<TemplateAsset> TemplateAssets { get; }
    DbSet<AiAgent> AiAgents { get; }
    DbSet<AiAgentResource> AiAgentResources { get; }
    DbSet<AiAgentPrompt> AiAgentPrompts { get; }
    DbSet<AiAgentCacheField> AiAgentCacheFields { get; }
    DbSet<AiAgentCacheValue> AiAgentCacheValues { get; }
    DbSet<AiAgentLineBinding> AiAgentLineBindings { get; }
    DbSet<AiAgentRunLog> AiAgentRunLogs { get; }
    DbSet<AiUsageLog> AiUsageLogs { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<TaskBoard> TaskBoards { get; }
    DbSet<TaskBoardColumn> TaskBoardColumns { get; }
    DbSet<TaskCard> TaskCards { get; }
    DbSet<TaskCardAssignment> TaskCardAssignments { get; }
    DbSet<TaskCardTag> TaskCardTags { get; }
    DbSet<TaskCardTagAssignment> TaskCardTagAssignments { get; }
    DbSet<TaskCardChecklistItem> TaskCardChecklistItems { get; }
    DbSet<TaskCardActivity> TaskCardActivities { get; }
    DbSet<TaskCardAttachment> TaskCardAttachments { get; }
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
    DbSet<TenantApiConfig> TenantApiConfigs { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<AccountActivationCode> AccountActivationCodes { get; }
    DbSet<SuperAdminAuditLog> SuperAdminAuditLogs { get; }

    // Configuracion del salon (Capa 2).
    DbSet<Service> Services { get; }
    DbSet<ServiceImage> ServiceImages { get; }
    DbSet<ServicePriceTier> ServicePriceTiers { get; }
    DbSet<Resource> Resources { get; }
    DbSet<Sede> Sedes { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductStock> ProductStocks { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseRegistration> CourseRegistrations { get; }
    DbSet<SalonFieldDefinition> SalonFieldDefinitions { get; }
    DbSet<ResourceServiceLink> ResourceServiceLinks { get; }
    DbSet<ShiftTemplate> ShiftTemplates { get; }
    DbSet<ScheduleException> ScheduleExceptions { get; }

    // Citas / Agenda (Capa 2).
    DbSet<Client> Clients { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AppointmentServiceItem> AppointmentServiceItems { get; }
    DbSet<AppointmentMessage> AppointmentMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
