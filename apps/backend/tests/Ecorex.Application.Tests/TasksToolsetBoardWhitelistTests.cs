using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests de la WHITELIST DURA de tableros por agente en <see cref="TasksToolset"/> (crear_tarea /
/// listar_tableros). La whitelist llega por <see cref="AiToolRunContext"/> (la inyecta el motor de
/// inferencia desde AiAgent.AllowedBoardIdsJson). Semantica: null/vacio = SIN restriccion (todos los
/// tableros del tenant), 1+ ids = SOLO esos. Con exactamente 1 permitido, un nombre ausente o que no
/// calza cae al unico permitido. Corre sobre EF InMemory con solo las entidades que el toolset toca.
/// </summary>
public class TasksToolsetBoardWhitelistTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BoardA = Guid.NewGuid();
    private static readonly Guid BoardB = Guid.NewGuid();
    private static readonly Guid BoardC = Guid.NewGuid();

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<TaskBoard> TaskBoards => Set<TaskBoard>();
        public DbSet<ActivityType> ActivityTypes => Set<ActivityType>();
        public DbSet<Asesor> Asesores => Set<Asesor>();
        public DbSet<TaskItemAttachment> TaskItemAttachments => Set<TaskItemAttachment>();
        public DbSet<Message> Messages => Set<Message>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<ActivityType>().Ignore(x => x.WorkflowDefinition);
            b.Entity<Asesor>().Ignore(x => x.TenantUser);
            b.Entity<TaskItemAttachment>().Ignore(x => x.TaskItem);
            b.Entity<Message>().Ignore(x => x.Conversation);
        }
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;
        public Guid? UserId => null;
    }

    // ITaskItemService de mentira: captura el BoardId con el que se pidio crear y devuelve Ok.
    private sealed class FakeTasks : ITaskItemService
    {
        public Guid? LastBoardId { get; private set; }
        public CreateTaskItemRequest? LastRequest { get; private set; }
        public int CreateCalls { get; private set; }

        public Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
        {
            LastBoardId = request.BoardId;
            LastRequest = request;
            CreateCalls++;
            var item = new TaskItemSummaryDto(
                Guid.NewGuid(), "T-1", request.Title, request.ActivityTypeId, null,
                request.Priority, TaskItemStatus.Pending, request.AssigneeTenantUserId,
                request.DueDate, null, null, false, null, 1, DateTimeOffset.UtcNow,
                Array.Empty<TaskItemTagDto>(), BoardId: request.BoardId);
            var detail = new TaskItemDetailDto(
                item, request.Description, null, null, null, null,
                Array.Empty<string>(), 0,
                Array.Empty<TaskItemActivityDto>(), Array.Empty<TaskItemAttachmentDto>(),
                Array.Empty<TaskItemChecklistItemDto>(), Array.Empty<TaskItemAssigneeDto>());
            return Task.FromResult(TaskCoreResult<TaskItemDetailDto>.Ok(detail));
        }

        // El toolset solo llama CreateAsync; el resto de la interfaz no se usa en estas pruebas.
        public Task<TaskCoreResult<TaskItemDetailDto>> CreateSubtaskAsync(Guid parentId, string title, Guid? assigneeTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateCustomFieldsAsync(Guid taskId, string? customFieldsJson, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> RestoreAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListTagsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemTagDto>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountTagUsageAsync(Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> AttachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> DetachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListColumnAllowedTagsAsync(Guid columnId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> SetColumnAllowedTagsAsync(Guid columnId, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListAssignableTagsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> AddChecklistItemAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> ToggleChecklistItemAsync(Guid checklistItemId, bool isCompleted, Guid? completedByTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> RemoveChecklistItemAsync(Guid checklistItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> ReorderChecklistAsync(Guid taskId, IReadOnlyList<Guid> orderedItemIds, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> AddAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> RemoveAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemActivityDto>> AddCommentAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemAttachmentDto>> AddAttachmentAsync(AddTaskAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskWorkLogDto>> AddWorkLogAsync(AddTaskWorkLogRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskWorkLogDto>> ListWorkLogsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> TotalSecondsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<TaskItemSummaryDto>> ListAsync(TaskItemListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskItemDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // IApplicationDbContext de mentira: solo respalda con InMemory los DbSet que TasksToolset toca
    // (TaskBoards, ActivityTypes, Asesores, TaskItemAttachments, Messages); el resto lanza.
    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<PlatformUser> PlatformUsers => throw new NotSupportedException();
        public DbSet<TenantUser> TenantUsers => throw new NotSupportedException();
        public DbSet<Tenant> Tenants => throw new NotSupportedException();
        public DbSet<TenantEmailConfig> TenantEmailConfigs => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellVoiceLine> RetellVoiceLines => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.VoiceCall> VoiceCalls => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellAgentMap> RetellAgentMaps => throw new NotSupportedException();
        public DbSet<StorageConfig> StorageConfigs => throw new NotSupportedException();
        public DbSet<Asesor> Asesores => inner.Asesores;
        public DbSet<DataModelRelation> DataModelRelations => throw new NotSupportedException();
        public DbSet<DataModelRelationLink> DataModelRelationLinks => throw new NotSupportedException();
        public DbSet<ReportDefinition> ReportDefinitions => throw new NotSupportedException();
        public DbSet<ReportDefinitionRole> ReportDefinitionRoles => throw new NotSupportedException();
        public DbSet<ReportTemplate> ReportTemplates => throw new NotSupportedException();
        public DbSet<ExternalDataSource> ExternalDataSources => throw new NotSupportedException();
        public DbSet<ExternalDataSet> ExternalDataSets => throw new NotSupportedException();
        public DbSet<ExternalDataSourceGrant> ExternalDataSourceGrants => throw new NotSupportedException();
        public DbSet<DocumentoCategoria> DocumentoCategorias => throw new NotSupportedException();
        public DbSet<DocumentoCarpeta> DocumentoCarpetas => throw new NotSupportedException();
        public DbSet<Documento> Documentos => throw new NotSupportedException();
        public DbSet<DocumentoVersion> DocumentoVersiones => throw new NotSupportedException();
        public DbSet<DocumentoEtiquetaCatalogo> DocumentoEtiquetaCatalogos => throw new NotSupportedException();
        public DbSet<DocumentoEtiqueta> DocumentoEtiquetas => throw new NotSupportedException();
        public DbSet<DocumentoDestacadoPersonal> DocumentoDestacadosPersonales => throw new NotSupportedException();
        public DbSet<DocumentoAuditoria> DocumentoAuditorias => throw new NotSupportedException();
        public DbSet<DocumentoConsumo> DocumentoConsumos => throw new NotSupportedException();
        public DbSet<SerieDocumental> SeriesDocumentales => throw new NotSupportedException();
        public DbSet<SubserieDocumental> SubseriesDocumentales => throw new NotSupportedException();
        public DbSet<SubserieTipologia> SubserieTipologias => throw new NotSupportedException();
        public DbSet<SubserieCampo> SubserieCampos => throw new NotSupportedException();
        public DbSet<Expediente> Expedientes => throw new NotSupportedException();
        public DbSet<ExpedienteTipologia> ExpedienteTipologias => throw new NotSupportedException();
        public DbSet<ExpedienteCampo> ExpedienteCampos => throw new NotSupportedException();
        public DbSet<TerceroFormLink> TerceroFormLinks => throw new NotSupportedException();
        public DbSet<TenantConfiguration> TenantConfigurations => throw new NotSupportedException();
        public DbSet<ConceptoActividad> ConceptosActividad => throw new NotSupportedException();
        public DbSet<TenantEvolutionConfig> TenantEvolutionConfigs => throw new NotSupportedException();
        public DbSet<WhatsAppLine> WhatsAppLines => throw new NotSupportedException();
        public DbSet<PipelineStage> PipelineStages => throw new NotSupportedException();
        public DbSet<PipelineFieldDefinition> PipelineFieldDefinitions => throw new NotSupportedException();
        public DbSet<BusinessUnit> BusinessUnits => throw new NotSupportedException();
        public DbSet<Lead> Leads => throw new NotSupportedException();
        public DbSet<LeadActivity> LeadActivities => throw new NotSupportedException();
        public DbSet<LeadNote> LeadNotes => throw new NotSupportedException();
        public DbSet<LeadFile> LeadFiles => throw new NotSupportedException();
        public DbSet<ContactImportBatch> ContactImportBatches => throw new NotSupportedException();
        public DbSet<ContactSearchRun> ContactSearchRuns => throw new NotSupportedException();
        public DbSet<EmailTemplate> EmailTemplates => throw new NotSupportedException();
        public DbSet<FollowUpTask> FollowUpTasks => throw new NotSupportedException();
        public DbSet<Conversation> Conversations => throw new NotSupportedException();
        public DbSet<Message> Messages => inner.Messages;
        public DbSet<TenantBlockedNumber> TenantBlockedNumbers => throw new NotSupportedException();
        public DbSet<MessageTemplate> MessageTemplates => throw new NotSupportedException();
        public DbSet<QuoteTemplate> QuoteTemplates => throw new NotSupportedException();
        public DbSet<TemplateAsset> TemplateAssets => throw new NotSupportedException();
        public DbSet<AiAgent> AiAgents => throw new NotSupportedException();
        public DbSet<AiAgentResource> AiAgentResources => throw new NotSupportedException();
        public DbSet<AiAgentPrompt> AiAgentPrompts => throw new NotSupportedException();
        public DbSet<AiAgentCacheField> AiAgentCacheFields => throw new NotSupportedException();
        public DbSet<AiAgentCacheValue> AiAgentCacheValues => throw new NotSupportedException();
        public DbSet<AiAgentLineBinding> AiAgentLineBindings => throw new NotSupportedException();
        public DbSet<AiAgentRunLog> AiAgentRunLogs => throw new NotSupportedException();
        public DbSet<AiUsageLog> AiUsageLogs => throw new NotSupportedException();
        public DbSet<AutomationRule> AutomationRules => throw new NotSupportedException();
        public DbSet<TaskBoard> TaskBoards => inner.TaskBoards;
        public DbSet<TaskBoardColumn> TaskBoardColumns => throw new NotSupportedException();
        public DbSet<TaskCard> TaskCards => throw new NotSupportedException();
        public DbSet<TaskCardAssignment> TaskCardAssignments => throw new NotSupportedException();
        public DbSet<TaskCardTag> TaskCardTags => throw new NotSupportedException();
        public DbSet<TaskCardTagAssignment> TaskCardTagAssignments => throw new NotSupportedException();
        public DbSet<TaskCardChecklistItem> TaskCardChecklistItems => throw new NotSupportedException();
        public DbSet<TaskCardActivity> TaskCardActivities => throw new NotSupportedException();
        public DbSet<TaskCardAttachment> TaskCardAttachments => throw new NotSupportedException();
        public DbSet<ActivityType> ActivityTypes => inner.ActivityTypes;
        public DbSet<Project> Projects => throw new NotSupportedException();
        public DbSet<ProjectMember> ProjectMembers => throw new NotSupportedException();
        public DbSet<ProjectMilestone> ProjectMilestones => throw new NotSupportedException();
        public DbSet<ProjectBudgetItem> ProjectBudgetItems => throw new NotSupportedException();
        public DbSet<ProjectDofa> ProjectDofas => throw new NotSupportedException();
        public DbSet<TaskItem> TaskItems => throw new NotSupportedException();
        public DbSet<TaskItemTag> TaskItemTags => throw new NotSupportedException();
        public DbSet<TaskItemTagAssignment> TaskItemTagAssignments => throw new NotSupportedException();
        public DbSet<TaskBoardColumnTag> TaskBoardColumnTags => throw new NotSupportedException();
        public DbSet<TaskWorkLog> TaskWorkLogs => throw new NotSupportedException();
        public DbSet<TaskItemActivity> TaskItemActivities => throw new NotSupportedException();
        public DbSet<Notification> Notifications => throw new NotSupportedException();
        public DbSet<TaskItemAttachment> TaskItemAttachments => inner.TaskItemAttachments;
        public DbSet<TaskItemChecklistItem> TaskItemChecklistItems => throw new NotSupportedException();
        public DbSet<TaskItemAssignment> TaskItemAssignments => throw new NotSupportedException();
        public DbSet<TaskFieldDefinition> TaskFieldDefinitions => throw new NotSupportedException();
        public DbSet<TenantSequence> TenantSequences => throw new NotSupportedException();
        public DbSet<WorkflowDefinition> WorkflowDefinitions => throw new NotSupportedException();
        public DbSet<CardTag> CardTags => throw new NotSupportedException();
        public DbSet<FlowTag> FlowTags => throw new NotSupportedException();
        public DbSet<FormTag> FormTags => throw new NotSupportedException();
        public DbSet<WorkflowNode> WorkflowNodes => throw new NotSupportedException();
        public DbSet<WorkflowEdge> WorkflowEdges => throw new NotSupportedException();
        public DbSet<WorkflowInstance> WorkflowInstances => throw new NotSupportedException();
        public DbSet<WorkflowStepHistory> WorkflowStepHistories => throw new NotSupportedException();
        public DbSet<FormDefinition> FormDefinitions => throw new NotSupportedException();
        public DbSet<FormContainer> FormContainers => throw new NotSupportedException();
        public DbSet<FormQuestion> FormQuestions => throw new NotSupportedException();
        public DbSet<FormResponse> FormResponses => throw new NotSupportedException();
        public DbSet<WorkflowNodeNote> WorkflowNodeNotes => throw new NotSupportedException();
        public DbSet<FormSubmitRule> FormSubmitRules => throw new NotSupportedException();
        public DbSet<FormFlowLink> FormFlowLinks => throw new NotSupportedException();
        public DbSet<FormToken> FormTokens => throw new NotSupportedException();
        public DbSet<FormRecordLink> FormRecordLinks => throw new NotSupportedException();
        public DbSet<WorkflowNodeForm> WorkflowNodeForms => throw new NotSupportedException();
        public DbSet<WorkflowNodeAgent> WorkflowNodeAgents => throw new NotSupportedException();
        public DbSet<ScheduledJob> ScheduledJobs => throw new NotSupportedException();
        public DbSet<ScheduledJobRule> ScheduledJobRules => throw new NotSupportedException();
        public DbSet<ScheduledJobChannel> ScheduledJobChannels => throw new NotSupportedException();
        public DbSet<ScheduledJobRun> ScheduledJobRuns => throw new NotSupportedException();
        public DbSet<RuleDocument> RuleDocuments => throw new NotSupportedException();
        public DbSet<Rule> Rules => throw new NotSupportedException();
        public DbSet<RuleExecutionLog> RuleExecutionLogs => throw new NotSupportedException();
        public DbSet<FormFieldRule> FormFieldRules => throw new NotSupportedException();
        public DbSet<WorkflowNodeRule> WorkflowNodeRules => throw new NotSupportedException();
        public DbSet<OrgUnit> OrgUnits => throw new NotSupportedException();
        public DbSet<OrgUnitMember> OrgUnitMembers => throw new NotSupportedException();
        public DbSet<WorkflowNodePolicy> WorkflowNodePolicies => throw new NotSupportedException();
        public DbSet<ModuleDefinition> ModuleDefinitions => throw new NotSupportedException();
        public DbSet<TenantModule> TenantModules => throw new NotSupportedException();
        public DbSet<SaasPlan> SaasPlans => throw new NotSupportedException();
        public DbSet<SaasPlanLimit> SaasPlanLimits => throw new NotSupportedException();
        public DbSet<TenantSubscription> TenantSubscriptions => throw new NotSupportedException();
        public DbSet<TenantPayment> TenantPayments => throw new NotSupportedException();
        public DbSet<WompiMasterConfig> WompiMasterConfigs => throw new NotSupportedException();
        public DbSet<WompiWebhookEvent> WompiWebhookEvents => throw new NotSupportedException();
        public DbSet<EvolutionMasterConfig> EvolutionMasterConfigs => throw new NotSupportedException();
        public DbSet<AiProviderConfig> AiProviderConfigs => throw new NotSupportedException();
        public DbSet<PlatformBranding> PlatformBrandings => throw new NotSupportedException();
        public DbSet<EmailConfig> EmailConfigs => throw new NotSupportedException();
        public DbSet<GoogleAuthConfig> GoogleAuthConfigs => throw new NotSupportedException();
        public DbSet<TenantApiConfig> TenantApiConfigs => throw new NotSupportedException();
        public DbSet<PasswordResetToken> PasswordResetTokens => throw new NotSupportedException();
        public DbSet<AccountActivationCode> AccountActivationCodes => throw new NotSupportedException();
        public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => throw new NotSupportedException();
        public DbSet<ScrapeSource> ScrapeSources => throw new NotSupportedException();
        public DbSet<ScrapeRun> ScrapeRuns => throw new NotSupportedException();
        public DbSet<ScrapeFlow> ScrapeFlows => throw new NotSupportedException();
        public DbSet<ScrapeStep> ScrapeSteps => throw new NotSupportedException();
        public DbSet<ScrapeVariable> ScrapeVariables => throw new NotSupportedException();
        public DbSet<ScrapeFlowRun> ScrapeFlowRuns => throw new NotSupportedException();
        public DbSet<AgentActivityLog> AgentActivityLogs => throw new NotSupportedException();
        public DbSet<Warehouse> Warehouses => throw new NotSupportedException();
        public DbSet<Brand> Brands => throw new NotSupportedException();
        public DbSet<ItemGroup> ItemGroups => throw new NotSupportedException();
        public DbSet<ItemSubgroup> ItemSubgroups => throw new NotSupportedException();
        public DbSet<ItemType> ItemTypes => throw new NotSupportedException();
        public DbSet<Item> Items => throw new NotSupportedException();
        public DbSet<ItemImage> ItemImages => throw new NotSupportedException();
        public DbSet<ItemStock> ItemStocks => throw new NotSupportedException();
        public DbSet<ItemFieldDefinition> ItemFieldDefinitions => throw new NotSupportedException();
        public DbSet<Entidad> Entidades => throw new NotSupportedException();
        public DbSet<EntidadFieldDefinition> EntidadFieldDefinitions => throw new NotSupportedException();
        public DbSet<DataModel> DataModels => throw new NotSupportedException();
        public DbSet<DataDestination> DataDestinations => throw new NotSupportedException();
        public DbSet<DataContainer> DataContainers => throw new NotSupportedException();
        public DbSet<DataContainerColumn> DataContainerColumns => throw new NotSupportedException();
        public DbSet<DataContainerRow> DataContainerRows => throw new NotSupportedException();
        public DbSet<DataContainerCell> DataContainerCells => throw new NotSupportedException();
        public DbSet<DataContainerLink> DataContainerLinks => throw new NotSupportedException();
        public DbSet<DataConnector> DataConnectors => throw new NotSupportedException();
        public DbSet<DataClient> DataClients => throw new NotSupportedException();
        public DbSet<ImportProcess> ImportProcesses => throw new NotSupportedException();
        public DbSet<ImportRun> ImportRuns => throw new NotSupportedException();
        public DbSet<WhatsAppTemplate> WhatsAppTemplates => throw new NotSupportedException();
        public DbSet<MenuView> MenuViews => throw new NotSupportedException();
        public DbSet<MenuNode> MenuNodes => throw new NotSupportedException();
        public DbSet<Rol> Roles => throw new NotSupportedException();
        public DbSet<Ciudad> Ciudades => throw new NotSupportedException();
        public DbSet<TenantApiToken> TenantApiTokens => throw new NotSupportedException();
        public DbSet<RolPermiso> RolPermisos => throw new NotSupportedException();
        public DbSet<Tercero> Terceros => throw new NotSupportedException();
        public DbSet<TerceroContacto> TerceroContactos => throw new NotSupportedException();
        public DbSet<TerceroFieldDefinition> TerceroFieldDefinitions => throw new NotSupportedException();
        public DbSet<ContactSearchDefinition> ContactSearchDefinitions => throw new NotSupportedException();
        public DbSet<TerceroFichaDefinition> TerceroFichaDefinitions => throw new NotSupportedException();
        public DbSet<TerceroNota> TerceroNotas => throw new NotSupportedException();
        public DbSet<BolsaColumna> BolsaColumnas => throw new NotSupportedException();
        public DbSet<Oportunidad> Oportunidades => throw new NotSupportedException();
        public DbSet<OportunidadEstado> OportunidadEstados => throw new NotSupportedException();
        public DbSet<Cita> Citas => throw new NotSupportedException();
        public DbSet<TerceroFiltro> TerceroFiltros => throw new NotSupportedException();
        public DbSet<ProspectoScrapeado> ProspectosScrapeados => throw new NotSupportedException();
        public DbSet<ContactWorkflow> ContactWorkflows => throw new NotSupportedException();
        public DbSet<ContactWorkflowStep> ContactWorkflowSteps => throw new NotSupportedException();
        public DbSet<ContactWorkflowSchedule> ContactWorkflowSchedules => throw new NotSupportedException();
        public DbSet<ContactWorkflowRun> ContactWorkflowRuns => throw new NotSupportedException();
        public DbSet<ActividadCategoria> ActividadCategorias => throw new NotSupportedException();
        public DbSet<ActividadSubcategoria> ActividadSubcategorias => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaCargo> ActividadSubcategoriaCargos => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaTercero> ActividadSubcategoriaTerceros => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaNotificacion> ActividadSubcategoriaNotificaciones => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaSede> ActividadSubcategoriaSedes => throw new NotSupportedException();
        public DbSet<ActivityPriority> ActivityPriorities => throw new NotSupportedException();
        public DbSet<ActivityState> ActivityStates => throw new NotSupportedException();
        public DbSet<ProjectType> ProjectTypes => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool HasActiveTransaction => false;
    }

    private static (TasksToolset Toolset, FakeTasks Tasks, InnerDb Inner) NewToolset()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // Tres tableros no archivados + un tipo de actividad (para no auto-provisionar).
        inner.TaskBoards.AddRange(
            new TaskBoard { Id = BoardA, TenantId = Tenant, Name = "Tablero A" },
            new TaskBoard { Id = BoardB, TenantId = Tenant, Name = "Tablero B" },
            new TaskBoard { Id = BoardC, TenantId = Tenant, Name = "Tablero C" });
        inner.ActivityTypes.Add(new ActivityType { TenantId = Tenant, Category = "General", Name = "Solicitud" });
        inner.SaveChanges();

        var db = new FakeAppDb(inner);
        var tasks = new FakeTasks();
        return (new TasksToolset(db, tasks, new FakeTenant()), tasks, inner);
    }

    private static async Task<JsonElement> CreateAsync(TasksToolset ts, string? tablero, string titulo = "Necesito ayuda")
    {
        var args = tablero is null
            ? JsonSerializer.Serialize(new { titulo })
            : JsonSerializer.Serialize(new { tablero, titulo });
        var res = await ts.ExecuteAsync("crear_tarea", args, Guid.NewGuid(), autonomous: true);
        return JsonDocument.Parse(res.Json).RootElement;
    }

    private static async Task<JsonElement> ListAsync(TasksToolset ts)
    {
        var res = await ts.ExecuteAsync("listar_tableros", "{}", Guid.NewGuid(), autonomous: true);
        return JsonDocument.Parse(res.Json).RootElement;
    }

    // (a) whitelist vacia (null) => permite cualquier tablero (comportamiento historico).
    [Fact]
    public async Task Whitelist_vacia_permite_cualquier_tablero()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, allowedBoardIds: null))
        {
            var r = await CreateAsync(ts, "Tablero B");
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("Tablero B", r.GetProperty("tablero").GetString());
        }
        Assert.Equal(BoardB, tasks.LastBoardId);
    }

    // (b) whitelist [A] y pide A => crea en A.
    [Fact]
    public async Task Whitelist_unico_permite_ese_tablero()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, new[] { BoardA }))
        {
            var r = await CreateAsync(ts, "Tablero A");
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("Tablero A", r.GetProperty("tablero").GetString());
        }
        Assert.Equal(BoardA, tasks.LastBoardId);
    }

    // (c1) whitelist [A] y pide B (un tablero que existe pero NO esta permitido) => la whitelist NUNCA se
    // escapa: con 1 solo permitido, cae al unico permitido (A). Jamas crea en B. (SEMANTICA + CAMBIOS s6.)
    [Fact]
    public async Task Whitelist_unico_no_escapa_a_un_tablero_fuera_de_lista()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, new[] { BoardA }))
        {
            var r = await CreateAsync(ts, "Tablero B");
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("Tablero A", r.GetProperty("tablero").GetString());
        }
        Assert.Equal(BoardA, tasks.LastBoardId);   // creo en A, NUNCA en B
        Assert.NotEqual(BoardB, tasks.LastBoardId);
    }

    // (c2) whitelist [A] y NO pasa tablero => usa A por defecto (comodidad con 1 permitido).
    [Fact]
    public async Task Whitelist_unico_sin_tablero_usa_el_unico_permitido()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, new[] { BoardA }))
        {
            var r = await CreateAsync(ts, tablero: null);
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("Tablero A", r.GetProperty("tablero").GetString());
        }
        Assert.Equal(BoardA, tasks.LastBoardId);
    }

    // (d) whitelist [A,B] y pide C => Err listando SOLO A,B; no crea. Con 2+, no hay default.
    [Fact]
    public async Task Whitelist_multiple_rechaza_y_lista_solo_permitidos()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, new[] { BoardA, BoardB }))
        {
            var r = await CreateAsync(ts, "Tablero C");
            Assert.False(r.GetProperty("ok").GetBoolean());
            var error = r.GetProperty("error").GetString()!;
            // La lista de tableros DISPONIBLES (tras "disponibles:") debe traer solo los permitidos A y B,
            // nunca C (el nombre pedido se puede citar en el mensaje, pero no en la lista de opciones).
            var disponibles = error[(error.IndexOf("disponibles:", StringComparison.Ordinal) + "disponibles:".Length)..];
            Assert.Contains("Tablero A", disponibles);
            Assert.Contains("Tablero B", disponibles);
            Assert.DoesNotContain("Tablero C", disponibles);
        }
        Assert.Equal(0, tasks.CreateCalls);
    }

    // (e) listar_tableros con whitelist [A] => solo A.
    [Fact]
    public async Task ListarTableros_con_whitelist_muestra_solo_permitidos()
    {
        var (ts, _, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, new[] { BoardA }))
        {
            var r = await ListAsync(ts);
            var nombres = r.GetProperty("tableros").EnumerateArray()
                .Select(t => t.GetProperty("nombre").GetString()).ToList();
            Assert.Equal(new[] { "Tablero A" }, nombres);
        }
    }

    // (e-bis) listar_tableros sin whitelist => todos.
    [Fact]
    public async Task ListarTableros_sin_whitelist_muestra_todos()
    {
        var (ts, _, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, allowedBoardIds: null))
        {
            var r = await ListAsync(ts);
            var nombres = r.GetProperty("tableros").EnumerateArray()
                .Select(t => t.GetProperty("nombre").GetString()).ToList();
            Assert.Equal(3, nombres.Count);
        }
    }

    // (f) crear_tarea LIGA el contacto: cliente_nombre/telefono/email/identificacion -> Requester* del request
    // (para que el RESUMEN del detalle muestre Contacto/Telefono/Email/Identificacion).
    [Fact]
    public async Task CrearTarea_liga_datos_del_contacto()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, allowedBoardIds: null))
        {
            var args = JsonSerializer.Serialize(new
            {
                tablero = "Tablero A",
                titulo = "Necesito cotizacion",
                cliente_nombre = "Juan Perez",
                cliente_telefono = "573001112233",
                cliente_email = "juan@correo.com",
                cliente_identificacion = "CC 79.123.456"
            });
            var res = await ts.ExecuteAsync("crear_tarea", args, Guid.NewGuid(), autonomous: true);
            Assert.True(JsonDocument.Parse(res.Json).RootElement.GetProperty("ok").GetBoolean());
        }
        Assert.NotNull(tasks.LastRequest);
        Assert.Equal("Juan Perez", tasks.LastRequest!.RequesterName);
        Assert.Equal("573001112233", tasks.LastRequest.RequesterPhone);
        Assert.Equal("juan@correo.com", tasks.LastRequest.RequesterEmail);
        Assert.Equal("CC 79.123.456", tasks.LastRequest.RequesterDocument);
    }

    // (f-bis) sin datos de contacto, los Requester* quedan null (compat con el comportamiento previo).
    [Fact]
    public async Task CrearTarea_sin_contacto_deja_requester_null()
    {
        var (ts, tasks, _) = NewToolset();
        using (AiToolRunContext.Begin(null, null, null, null, allowedBoardIds: null))
        {
            await CreateAsync(ts, "Tablero A");
        }
        Assert.NotNull(tasks.LastRequest);
        Assert.Null(tasks.LastRequest!.RequesterName);
        Assert.Null(tasks.LastRequest.RequesterPhone);
        Assert.Null(tasks.LastRequest.RequesterEmail);
        Assert.Null(tasks.LastRequest.RequesterDocument);
    }
}
