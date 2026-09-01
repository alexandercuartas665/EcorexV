using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Gestor;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios del MOTOR de acciones por filtro de contactos (ADR-0056, Fase 2). Se corren sobre un
/// IApplicationDbContext en memoria (EF InMemory) con solo las entidades que el dispatcher toca; los
/// servicios de ejecucion (WhatsApp/Email/CRM) son dobles verificables. Comprueban:
///   1. un workflow con 2 contactos y 1 paso WhatsApp en ventana corre UNA vez (2 envios, 2 runs Sent);
///   2. re-correr NO reenvia (dedupe por ContactWorkflowRun);
///   3. si la ventana de horario NO esta vigente, no se envia nada.
/// El aislamiento cross-tenant vive en la matriz dual de Integration.Tests (no aqui).
/// </summary>
public class ContactWorkflowDispatcherTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // ---- EF InMemory con solo lo que toca el dispatcher ----

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<Tercero> Terceros => Set<Tercero>();
        public DbSet<TerceroFiltro> TerceroFiltros => Set<TerceroFiltro>();
        public DbSet<ContactWorkflow> ContactWorkflows => Set<ContactWorkflow>();
        public DbSet<ContactWorkflowStep> ContactWorkflowSteps => Set<ContactWorkflowStep>();
        public DbSet<ContactWorkflowSchedule> ContactWorkflowSchedules => Set<ContactWorkflowSchedule>();
        public DbSet<ContactWorkflowRun> ContactWorkflowRuns => Set<ContactWorkflowRun>();
        public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            // Ignora las navegaciones del Tercero para no arrastrar entidades ajenas al modelo de prueba.
            b.Entity<Tercero>().Ignore(t => t.Empresa).Ignore(t => t.Contactos).Ignore(t => t.BolsaColumna);
        }
    }

    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
    }

    // Doble de WhatsApp: cuenta los envios y devuelve Ok; el resto de la interfaz no se ejercita.
    private sealed class FakeWhatsApp : IWhatsAppConnectorService
    {
        public int Sends { get; private set; }
        public Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default)
        {
            Sends++;
            return Task.FromResult(new LineSendResult(true, null, $"wamid-{Sends}"));
        }
        public Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LineSendResult> DeleteMessageForEveryoneAsync(Guid lineId, string phone, string messageId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<EvolutionMediaResult> FetchInboundMediaAsync(Guid lineId, string messageKeyId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeEmail : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult(true, null));
    }

    // Doble de tareas: el paso Llamada no se ejercita en estos tests (solo WhatsApp), asi que todos los
    // metodos lanzan; basta para construir el dispatcher.
    private sealed class FakeTasks : ITaskItemService
    {
        public Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> CreateSubtaskAsync(Guid parentId, string title, Guid? assigneeTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateCustomFieldsAsync(Guid taskId, string? customFieldsJson, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> RestoreAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListTagsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemTagDto>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountTagUsageAsync(Guid tagId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> AttachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> DetachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListColumnAllowedTagsAsync(Guid columnId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> SetColumnAllowedTagsAsync(Guid columnId, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListAssignableTagsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> AddChecklistItemAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> ToggleChecklistItemAsync(Guid checklistItemId, bool isCompleted, Guid? completedByTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> RemoveChecklistItemAsync(Guid checklistItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> ReorderChecklistAsync(Guid taskId, IReadOnlyList<Guid> orderedItemIds, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> AddAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> RemoveAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemActivityDto>> AddCommentAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskItemAttachmentDto>> AddAttachmentAsync(AddTaskAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<bool>> DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskCoreResult<TaskWorkLogDto>> AddWorkLogAsync(AddTaskWorkLogRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskWorkLogDto>> ListWorkLogsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long> TotalSecondsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<TaskItemSummaryDto>> ListAsync(TaskItemListFilter filter, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TaskItemDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    // Adaptador IApplicationDbContext: expone los 9 conjuntos que usa el dispatcher sobre el InnerDb;
    // el resto de la interfaz no se toca (NotUsed) -mismo patron que ScheduledUpsertRunTests-.
    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<Tenant> Tenants => inner.Tenants;
        public DbSet<Tercero> Terceros => inner.Terceros;
        public DbSet<TerceroFiltro> TerceroFiltros => inner.TerceroFiltros;
        public DbSet<ContactWorkflow> ContactWorkflows => inner.ContactWorkflows;
        public DbSet<ContactWorkflowStep> ContactWorkflowSteps => inner.ContactWorkflowSteps;
        public DbSet<ContactWorkflowSchedule> ContactWorkflowSchedules => inner.ContactWorkflowSchedules;
        public DbSet<ContactWorkflowRun> ContactWorkflowRuns => inner.ContactWorkflowRuns;
        public DbSet<EmailTemplate> EmailTemplates => inner.EmailTemplates;
        public DbSet<WhatsAppLine> WhatsAppLines => inner.WhatsAppLines;
        public DbSet<Conversation> Conversations => inner.Conversations;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool HasActiveTransaction => false;

        public DbSet<T> NotUsed<T>() where T : class => throw new NotSupportedException();
        public DbSet<ContactSearchRun> ContactSearchRuns => NotUsed<ContactSearchRun>();
        public DbSet<PlatformUser> PlatformUsers => NotUsed<PlatformUser>();
        public DbSet<TenantUser> TenantUsers => NotUsed<TenantUser>();
        public DbSet<TenantEmailConfig> TenantEmailConfigs => NotUsed<TenantEmailConfig>();
        public DbSet<StorageConfig> StorageConfigs => NotUsed<StorageConfig>();
        public DbSet<Asesor> Asesores => NotUsed<Asesor>();
        public DbSet<DataModelRelation> DataModelRelations => NotUsed<DataModelRelation>();
        public DbSet<DataModelRelationLink> DataModelRelationLinks => NotUsed<DataModelRelationLink>();
        public DbSet<ReportDefinition> ReportDefinitions => NotUsed<ReportDefinition>();
        public DbSet<ReportDefinitionRole> ReportDefinitionRoles => NotUsed<ReportDefinitionRole>();
        public DbSet<ReportTemplate> ReportTemplates => NotUsed<ReportTemplate>();
        public DbSet<ExternalDataSource> ExternalDataSources => NotUsed<ExternalDataSource>();
        public DbSet<ExternalDataSet> ExternalDataSets => NotUsed<ExternalDataSet>();
        public DbSet<ExternalDataSourceGrant> ExternalDataSourceGrants => NotUsed<ExternalDataSourceGrant>();
        public DbSet<DocumentoCategoria> DocumentoCategorias => NotUsed<DocumentoCategoria>();
        public DbSet<DocumentoCarpeta> DocumentoCarpetas => NotUsed<DocumentoCarpeta>();
        public DbSet<Documento> Documentos => NotUsed<Documento>();
        public DbSet<DocumentoVersion> DocumentoVersiones => NotUsed<DocumentoVersion>();
        public DbSet<DocumentoEtiquetaCatalogo> DocumentoEtiquetaCatalogos => NotUsed<DocumentoEtiquetaCatalogo>();
        public DbSet<DocumentoEtiqueta> DocumentoEtiquetas => NotUsed<DocumentoEtiqueta>();
        public DbSet<DocumentoDestacadoPersonal> DocumentoDestacadosPersonales => NotUsed<DocumentoDestacadoPersonal>();
        public DbSet<DocumentoAuditoria> DocumentoAuditorias => NotUsed<DocumentoAuditoria>();
        public DbSet<DocumentoConsumo> DocumentoConsumos => NotUsed<DocumentoConsumo>();
        public DbSet<SerieDocumental> SeriesDocumentales => NotUsed<SerieDocumental>();
        public DbSet<SubserieDocumental> SubseriesDocumentales => NotUsed<SubserieDocumental>();
        public DbSet<SubserieTipologia> SubserieTipologias => NotUsed<SubserieTipologia>();
        public DbSet<SubserieCampo> SubserieCampos => NotUsed<SubserieCampo>();
        public DbSet<Expediente> Expedientes => NotUsed<Expediente>();
        public DbSet<ExpedienteTipologia> ExpedienteTipologias => NotUsed<ExpedienteTipologia>();
        public DbSet<ExpedienteCampo> ExpedienteCampos => NotUsed<ExpedienteCampo>();
        public DbSet<TerceroFormLink> TerceroFormLinks => NotUsed<TerceroFormLink>();
        public DbSet<TenantConfiguration> TenantConfigurations => NotUsed<TenantConfiguration>();
        public DbSet<ConceptoActividad> ConceptosActividad => NotUsed<ConceptoActividad>();
        public DbSet<TenantEvolutionConfig> TenantEvolutionConfigs => NotUsed<TenantEvolutionConfig>();
        public DbSet<PipelineStage> PipelineStages => NotUsed<PipelineStage>();
        public DbSet<PipelineFieldDefinition> PipelineFieldDefinitions => NotUsed<PipelineFieldDefinition>();
        public DbSet<BusinessUnit> BusinessUnits => NotUsed<BusinessUnit>();
        public DbSet<Lead> Leads => NotUsed<Lead>();
        public DbSet<LeadActivity> LeadActivities => NotUsed<LeadActivity>();
        public DbSet<LeadNote> LeadNotes => NotUsed<LeadNote>();
        public DbSet<LeadFile> LeadFiles => NotUsed<LeadFile>();
        public DbSet<ContactImportBatch> ContactImportBatches => NotUsed<ContactImportBatch>();
        public DbSet<FollowUpTask> FollowUpTasks => NotUsed<FollowUpTask>();
        public DbSet<Message> Messages => NotUsed<Message>();
        public DbSet<TenantBlockedNumber> TenantBlockedNumbers => NotUsed<TenantBlockedNumber>();
        public DbSet<MessageTemplate> MessageTemplates => NotUsed<MessageTemplate>();
        public DbSet<QuoteTemplate> QuoteTemplates => NotUsed<QuoteTemplate>();
        public DbSet<TemplateAsset> TemplateAssets => NotUsed<TemplateAsset>();
        public DbSet<AiAgent> AiAgents => NotUsed<AiAgent>();
        public DbSet<AiAgentResource> AiAgentResources => NotUsed<AiAgentResource>();
        public DbSet<AiAgentPrompt> AiAgentPrompts => NotUsed<AiAgentPrompt>();
        public DbSet<AiAgentCacheField> AiAgentCacheFields => NotUsed<AiAgentCacheField>();
        public DbSet<AiAgentCacheValue> AiAgentCacheValues => NotUsed<AiAgentCacheValue>();
        public DbSet<AiAgentLineBinding> AiAgentLineBindings => NotUsed<AiAgentLineBinding>();
        public DbSet<AiAgentRunLog> AiAgentRunLogs => NotUsed<AiAgentRunLog>();
        public DbSet<AiUsageLog> AiUsageLogs => NotUsed<AiUsageLog>();
        public DbSet<AutomationRule> AutomationRules => NotUsed<AutomationRule>();
        public DbSet<TaskBoard> TaskBoards => NotUsed<TaskBoard>();
        public DbSet<TaskBoardColumn> TaskBoardColumns => NotUsed<TaskBoardColumn>();
        public DbSet<TaskCard> TaskCards => NotUsed<TaskCard>();
        public DbSet<TaskCardAssignment> TaskCardAssignments => NotUsed<TaskCardAssignment>();
        public DbSet<TaskCardTag> TaskCardTags => NotUsed<TaskCardTag>();
        public DbSet<TaskCardTagAssignment> TaskCardTagAssignments => NotUsed<TaskCardTagAssignment>();
        public DbSet<TaskCardChecklistItem> TaskCardChecklistItems => NotUsed<TaskCardChecklistItem>();
        public DbSet<TaskCardActivity> TaskCardActivities => NotUsed<TaskCardActivity>();
        public DbSet<TaskCardAttachment> TaskCardAttachments => NotUsed<TaskCardAttachment>();
        public DbSet<ActivityType> ActivityTypes => NotUsed<ActivityType>();
        public DbSet<Project> Projects => NotUsed<Project>();
        public DbSet<ProjectMember> ProjectMembers => NotUsed<ProjectMember>();
        public DbSet<ProjectMilestone> ProjectMilestones => NotUsed<ProjectMilestone>();
        public DbSet<ProjectBudgetItem> ProjectBudgetItems => NotUsed<ProjectBudgetItem>();
        public DbSet<ProjectDofa> ProjectDofas => NotUsed<ProjectDofa>();
        public DbSet<TaskItem> TaskItems => NotUsed<TaskItem>();
        public DbSet<TaskItemTag> TaskItemTags => NotUsed<TaskItemTag>();
        public DbSet<TaskItemTagAssignment> TaskItemTagAssignments => NotUsed<TaskItemTagAssignment>();
        public DbSet<TaskBoardColumnTag> TaskBoardColumnTags => NotUsed<TaskBoardColumnTag>();
        public DbSet<TaskWorkLog> TaskWorkLogs => NotUsed<TaskWorkLog>();
        public DbSet<TaskItemActivity> TaskItemActivities => NotUsed<TaskItemActivity>();
        public DbSet<Notification> Notifications => NotUsed<Notification>();
        public DbSet<TaskItemAttachment> TaskItemAttachments => NotUsed<TaskItemAttachment>();
        public DbSet<TaskItemChecklistItem> TaskItemChecklistItems => NotUsed<TaskItemChecklistItem>();
        public DbSet<TaskItemAssignment> TaskItemAssignments => NotUsed<TaskItemAssignment>();
        public DbSet<TaskFieldDefinition> TaskFieldDefinitions => NotUsed<TaskFieldDefinition>();
        public DbSet<TenantSequence> TenantSequences => NotUsed<TenantSequence>();
        public DbSet<WorkflowDefinition> WorkflowDefinitions => NotUsed<WorkflowDefinition>();
        public DbSet<WorkflowNode> WorkflowNodes => NotUsed<WorkflowNode>();
        public DbSet<WorkflowEdge> WorkflowEdges => NotUsed<WorkflowEdge>();
        public DbSet<WorkflowInstance> WorkflowInstances => NotUsed<WorkflowInstance>();
        public DbSet<WorkflowStepHistory> WorkflowStepHistories => NotUsed<WorkflowStepHistory>();
        public DbSet<FormDefinition> FormDefinitions => NotUsed<FormDefinition>();
        public DbSet<FormContainer> FormContainers => NotUsed<FormContainer>();
        public DbSet<FormQuestion> FormQuestions => NotUsed<FormQuestion>();
        public DbSet<FormResponse> FormResponses => NotUsed<FormResponse>();
        public DbSet<WorkflowNodeNote> WorkflowNodeNotes => NotUsed<WorkflowNodeNote>();
        public DbSet<FormSubmitRule> FormSubmitRules => NotUsed<FormSubmitRule>();
        public DbSet<FormFlowLink> FormFlowLinks => NotUsed<FormFlowLink>();
        public DbSet<FormToken> FormTokens => NotUsed<FormToken>();
        public DbSet<FormRecordLink> FormRecordLinks => NotUsed<FormRecordLink>();
        public DbSet<WorkflowNodeForm> WorkflowNodeForms => NotUsed<WorkflowNodeForm>();
        public DbSet<WorkflowNodeAgent> WorkflowNodeAgents => NotUsed<WorkflowNodeAgent>();
        public DbSet<ScheduledJob> ScheduledJobs => NotUsed<ScheduledJob>();
        public DbSet<ScheduledJobRule> ScheduledJobRules => NotUsed<ScheduledJobRule>();
        public DbSet<ScheduledJobChannel> ScheduledJobChannels => NotUsed<ScheduledJobChannel>();
        public DbSet<ScheduledJobRun> ScheduledJobRuns => NotUsed<ScheduledJobRun>();
        public DbSet<RuleDocument> RuleDocuments => NotUsed<RuleDocument>();
        public DbSet<Rule> Rules => NotUsed<Rule>();
        public DbSet<RuleExecutionLog> RuleExecutionLogs => NotUsed<RuleExecutionLog>();
        public DbSet<FormFieldRule> FormFieldRules => NotUsed<FormFieldRule>();
        public DbSet<WorkflowNodeRule> WorkflowNodeRules => NotUsed<WorkflowNodeRule>();
        public DbSet<OrgUnit> OrgUnits => NotUsed<OrgUnit>();
        public DbSet<OrgUnitMember> OrgUnitMembers => NotUsed<OrgUnitMember>();
        public DbSet<WorkflowNodePolicy> WorkflowNodePolicies => NotUsed<WorkflowNodePolicy>();
        public DbSet<ModuleDefinition> ModuleDefinitions => NotUsed<ModuleDefinition>();
        public DbSet<TenantModule> TenantModules => NotUsed<TenantModule>();
        public DbSet<SaasPlan> SaasPlans => NotUsed<SaasPlan>();
        public DbSet<SaasPlanLimit> SaasPlanLimits => NotUsed<SaasPlanLimit>();
        public DbSet<TenantSubscription> TenantSubscriptions => NotUsed<TenantSubscription>();
        public DbSet<TenantPayment> TenantPayments => NotUsed<TenantPayment>();
        public DbSet<WompiMasterConfig> WompiMasterConfigs => NotUsed<WompiMasterConfig>();
        public DbSet<WompiWebhookEvent> WompiWebhookEvents => NotUsed<WompiWebhookEvent>();
        public DbSet<EvolutionMasterConfig> EvolutionMasterConfigs => NotUsed<EvolutionMasterConfig>();
        public DbSet<AiProviderConfig> AiProviderConfigs => NotUsed<AiProviderConfig>();
        public DbSet<PlatformBranding> PlatformBrandings => NotUsed<PlatformBranding>();
        public DbSet<EmailConfig> EmailConfigs => NotUsed<EmailConfig>();
        public DbSet<GoogleAuthConfig> GoogleAuthConfigs => NotUsed<GoogleAuthConfig>();
        public DbSet<TenantApiConfig> TenantApiConfigs => NotUsed<TenantApiConfig>();
        public DbSet<PasswordResetToken> PasswordResetTokens => NotUsed<PasswordResetToken>();
        public DbSet<AccountActivationCode> AccountActivationCodes => NotUsed<AccountActivationCode>();
        public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => NotUsed<SuperAdminAuditLog>();
        public DbSet<ScrapeSource> ScrapeSources => NotUsed<ScrapeSource>();
        public DbSet<ScrapeRun> ScrapeRuns => NotUsed<ScrapeRun>();
        public DbSet<ScrapeFlow> ScrapeFlows => NotUsed<ScrapeFlow>();
        public DbSet<ScrapeStep> ScrapeSteps => NotUsed<ScrapeStep>();
        public DbSet<ScrapeVariable> ScrapeVariables => NotUsed<ScrapeVariable>();
        public DbSet<ScrapeFlowRun> ScrapeFlowRuns => NotUsed<ScrapeFlowRun>();
        public DbSet<AgentActivityLog> AgentActivityLogs => NotUsed<AgentActivityLog>();
        public DbSet<Warehouse> Warehouses => NotUsed<Warehouse>();
        public DbSet<Brand> Brands => NotUsed<Brand>();
        public DbSet<ItemGroup> ItemGroups => NotUsed<ItemGroup>();
        public DbSet<ItemSubgroup> ItemSubgroups => NotUsed<ItemSubgroup>();
        public DbSet<ItemType> ItemTypes => NotUsed<ItemType>();
        public DbSet<Item> Items => NotUsed<Item>();
        public DbSet<ItemImage> ItemImages => NotUsed<ItemImage>();
        public DbSet<ItemStock> ItemStocks => NotUsed<ItemStock>();
        public DbSet<ItemFieldDefinition> ItemFieldDefinitions => NotUsed<ItemFieldDefinition>();
        public DbSet<Entidad> Entidades => NotUsed<Entidad>();
        public DbSet<EntidadFieldDefinition> EntidadFieldDefinitions => NotUsed<EntidadFieldDefinition>();
        public DbSet<DataModel> DataModels => NotUsed<DataModel>();
        public DbSet<DataDestination> DataDestinations => NotUsed<DataDestination>();
        public DbSet<DataContainer> DataContainers => NotUsed<DataContainer>();
        public DbSet<DataContainerColumn> DataContainerColumns => NotUsed<DataContainerColumn>();
        public DbSet<DataContainerRow> DataContainerRows => NotUsed<DataContainerRow>();
        public DbSet<DataContainerCell> DataContainerCells => NotUsed<DataContainerCell>();
        public DbSet<DataContainerLink> DataContainerLinks => NotUsed<DataContainerLink>();
        public DbSet<DataConnector> DataConnectors => NotUsed<DataConnector>();
        public DbSet<DataClient> DataClients => NotUsed<DataClient>();
        public DbSet<ImportProcess> ImportProcesses => NotUsed<ImportProcess>();
        public DbSet<ImportRun> ImportRuns => NotUsed<ImportRun>();
        public DbSet<WhatsAppTemplate> WhatsAppTemplates => NotUsed<WhatsAppTemplate>();
        public DbSet<MenuView> MenuViews => NotUsed<MenuView>();
        public DbSet<MenuNode> MenuNodes => NotUsed<MenuNode>();
        public DbSet<Rol> Roles => NotUsed<Rol>();
        public DbSet<Ciudad> Ciudades => NotUsed<Ciudad>();
        public DbSet<TenantApiToken> TenantApiTokens => NotUsed<TenantApiToken>();
        public DbSet<RolPermiso> RolPermisos => NotUsed<RolPermiso>();
        public DbSet<TerceroContacto> TerceroContactos => NotUsed<TerceroContacto>();
        public DbSet<TerceroFieldDefinition> TerceroFieldDefinitions => NotUsed<TerceroFieldDefinition>();
        public DbSet<ContactSearchDefinition> ContactSearchDefinitions => NotUsed<ContactSearchDefinition>();
        public DbSet<TerceroFichaDefinition> TerceroFichaDefinitions => NotUsed<TerceroFichaDefinition>();
        public DbSet<TerceroNota> TerceroNotas => NotUsed<TerceroNota>();
        public DbSet<BolsaColumna> BolsaColumnas => NotUsed<BolsaColumna>();
        public DbSet<Oportunidad> Oportunidades => NotUsed<Oportunidad>();
        public DbSet<OportunidadEstado> OportunidadEstados => NotUsed<OportunidadEstado>();
        public DbSet<Cita> Citas => NotUsed<Cita>();
        public DbSet<ProspectoScrapeado> ProspectosScrapeados => NotUsed<ProspectoScrapeado>();
        public DbSet<ActividadCategoria> ActividadCategorias => NotUsed<ActividadCategoria>();
        public DbSet<ActividadSubcategoria> ActividadSubcategorias => NotUsed<ActividadSubcategoria>();
        public DbSet<ActividadSubcategoriaCargo> ActividadSubcategoriaCargos => NotUsed<ActividadSubcategoriaCargo>();
        public DbSet<ActividadSubcategoriaTercero> ActividadSubcategoriaTerceros => NotUsed<ActividadSubcategoriaTercero>();
        public DbSet<ActividadSubcategoriaNotificacion> ActividadSubcategoriaNotificaciones => NotUsed<ActividadSubcategoriaNotificacion>();
        public DbSet<ActividadSubcategoriaSede> ActividadSubcategoriaSedes => NotUsed<ActividadSubcategoriaSede>();
        public DbSet<ActivityPriority> ActivityPriorities => NotUsed<ActivityPriority>();
        public DbSet<ActivityState> ActivityStates => NotUsed<ActivityState>();
        public DbSet<ProjectType> ProjectTypes => NotUsed<ProjectType>();
    }

    private static (ContactWorkflowDispatcher Dispatcher, FakeWhatsApp Whatsapp, FakeAppDb Db, InnerDb Inner) Build(
        DateTimeOffset nowUtc, string activeDays, TimeOnly start, TimeOnly end)
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        // Tenant en UTC para que la ventana local == UTC (test determinista).
        inner.Tenants.Add(new Tenant { Id = TenantId, Name = "Demo", TimeZoneId = "UTC" });

        // Linea WhatsApp conectada (la ventana la resuelve por fallback: AccountId nulo).
        inner.WhatsAppLines.Add(new WhatsAppLine
        {
            TenantId = TenantId, InstanceName = "Linea 1", Status = WhatsAppLineStatus.Connected, PhoneNumber = "570000"
        });

        // Filtro sin criterios: su segmento es TODOS los terceros no inactivos.
        var filtro = new TerceroFiltro { TenantId = TenantId, Nombre = "Todos", CriteriosJson = "[]" };
        inner.TerceroFiltros.Add(filtro);

        // 2 contactos con telefono.
        inner.Terceros.Add(new Tercero { TenantId = TenantId, Nombre = "Ana", Tipo = TerceroTipo.Persona, Estado = TerceroEstado.Activo, Telefono = "573001112233" });
        inner.Terceros.Add(new Tercero { TenantId = TenantId, Nombre = "Beto", Tipo = TerceroTipo.Persona, Estado = TerceroEstado.Activo, Telefono = "573004445566" });

        var wf = new ContactWorkflow { TenantId = TenantId, TerceroFiltroId = filtro.Id, Nombre = "Acciones", Activo = true };
        inner.ContactWorkflows.Add(wf);
        var step = new ContactWorkflowStep
        {
            TenantId = TenantId, ContactWorkflowId = wf.Id, StepType = ContactWorkflowStepType.WhatsApp,
            Label = "WhatsApp", Orden = 0
        };
        inner.ContactWorkflowSteps.Add(step);
        inner.ContactWorkflowSchedules.Add(new ContactWorkflowSchedule
        {
            TenantId = TenantId, ContactWorkflowStepId = step.Id,
            StartTime = start, EndTime = end, ActiveDays = activeDays,
            TemplateId = "Hola, te contactamos.", AccountId = null, PackageSize = null, RepeatEvery = null
        });
        inner.SaveChanges();

        var db = new FakeAppDb(inner);
        var whatsapp = new FakeWhatsApp();
        var dispatcher = new ContactWorkflowDispatcher(
            db, new FakeTenantContext(TenantId), whatsapp, new FakeEmail(), new FakeTasks(),
            TimeProvider.System, NullLogger<ContactWorkflowDispatcher>.Instance);
        return (dispatcher, whatsapp, db, inner);
    }

    /// <summary>Todos los dias ISO como CSV (para que la ventana no dependa del dia en que corre el test).</summary>
    private const string AllDays = "1,2,3,4,5,6,7";

    [Fact]
    public async Task Corre_una_vez_sobre_dos_contactos_y_luego_dedupe_no_reenvia()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero); // dentro de una ventana amplia
        var (dispatcher, whatsapp, _, inner) = Build(now, AllDays, new TimeOnly(0, 0), new TimeOnly(23, 59));

        // Primera corrida: 2 envios, 2 runs Sent.
        var written1 = await dispatcher.RunDueForTenantAsync(now);
        Assert.Equal(2, written1);
        Assert.Equal(2, whatsapp.Sends);
        var runs = await inner.ContactWorkflowRuns.ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal(ContactWorkflowRunStatus.Sent, r.Status));
        Assert.All(runs, r => Assert.Equal("whatsapp", r.Channel));

        // Segunda corrida el MISMO dia: dedupe, no reenvia.
        var written2 = await dispatcher.RunDueForTenantAsync(now.AddMinutes(1));
        Assert.Equal(0, written2);
        Assert.Equal(2, whatsapp.Sends);
        Assert.Equal(2, await inner.ContactWorkflowRuns.CountAsync());
    }

    [Fact]
    public async Task Fuera_de_la_ventana_de_horario_no_envia_nada()
    {
        // Ventana 09:00-10:00 pero "ahora" son las 12:00: fuera de franja.
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var (dispatcher, whatsapp, _, inner) = Build(now, AllDays, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var written = await dispatcher.RunDueForTenantAsync(now);
        Assert.Equal(0, written);
        Assert.Equal(0, whatsapp.Sends);
        Assert.Equal(0, await inner.ContactWorkflowRuns.CountAsync());
    }

    [Fact]
    public async Task Dia_inactivo_no_envia_nada()
    {
        // 2026-08-03 es lunes (ISO 1). ActiveDays solo domingo (7): la ventana no aplica hoy.
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var (dispatcher, whatsapp, _, inner) = Build(now, "7", new TimeOnly(0, 0), new TimeOnly(23, 59));

        var written = await dispatcher.RunDueForTenantAsync(now);
        Assert.Equal(0, written);
        Assert.Equal(0, whatsapp.Sends);
        Assert.Equal(0, await inner.ContactWorkflowRuns.CountAsync());
    }
}
