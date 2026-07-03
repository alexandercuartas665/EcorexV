using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Common;

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
    DbSet<ActivityType> ActivityTypes { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<TaskItem> TaskItems { get; }
    DbSet<TaskItemTag> TaskItemTags { get; }
    DbSet<TaskItemTagAssignment> TaskItemTagAssignments { get; }
    DbSet<TaskWorkLog> TaskWorkLogs { get; }
    DbSet<TaskItemActivity> TaskItemActivities { get; }
    DbSet<TaskItemAttachment> TaskItemAttachments { get; }
    DbSet<TenantSequence> TenantSequences { get; }
    DbSet<WorkflowDefinition> WorkflowDefinitions { get; }
    DbSet<WorkflowNode> WorkflowNodes { get; }
    DbSet<WorkflowEdge> WorkflowEdges { get; }
    DbSet<WorkflowInstance> WorkflowInstances { get; }
    DbSet<WorkflowStepHistory> WorkflowStepHistories { get; }
    DbSet<FormDefinition> FormDefinitions { get; }
    DbSet<FormContainer> FormContainers { get; }
    DbSet<FormQuestion> FormQuestions { get; }
    DbSet<FormResponse> FormResponses { get; }
    DbSet<FormFlowLink> FormFlowLinks { get; }
    DbSet<FormToken> FormTokens { get; }
    DbSet<WorkflowNodeForm> WorkflowNodeForms { get; }
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre una transaccion explicita para casos de uso multi-paso (ej. emitir consecutivo +
    /// insertar TaskItem de forma atomica). Los casos simples siguen usando SaveChangesAsync solo.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si ya hay una transaccion abierta sobre la conexion. Permite que un caso de
    /// uso anidado (ej. WorkflowEngine.StartInstanceAsync dentro de TaskItemService.CreateAsync)
    /// se una a la transaccion del llamador en vez de intentar abrir otra.
    /// </summary>
    bool HasActiveTransaction { get; }
}
