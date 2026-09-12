using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del toolset NUEVO ActividadesToolset (ADR-0098, Opcion A): ver_formulario_concepto lista los
/// campos+opciones del formulario del concepto, y crear_actividad crea la actividad tipada por concepto
/// (SubcategoriaId correcto) + crea y ENVIA la respuesta del formulario con los valores por field_code.
/// Ante un valor de seleccion invalido, PRE-VALIDA y devuelve ok:false SIN crear nada (no deja actividad
/// huerfana). No toca TasksToolset/crear_tarea. Corre con fakes (sin BD real).
/// </summary>
public class ActividadesToolsetTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid DefId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    // ---- Fakes ----

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<ActividadSubcategoria> ActividadSubcategorias => Set<ActividadSubcategoria>();
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;
        public Guid? UserId => null;
    }

    private sealed class FakeTasks : ITaskItemService
    {
        public CreateTaskItemRequest? LastRequest { get; private set; }
        public int CreateCalls { get; private set; }
        public int ArchiveCalls { get; private set; }

        public Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
        {
            LastRequest = request; CreateCalls++;
            var item = new TaskItemSummaryDto(
                Guid.NewGuid(), "T-100", request.Title, request.ActivityTypeId, null,
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

        public Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
        {
            ArchiveCalls++;
            var item = new TaskItemSummaryDto(taskId, "T-100", "x", null, null, TaskPriority.Medium,
                TaskItemStatus.Pending, null, null, null, null, true, null, 1, DateTimeOffset.UtcNow,
                Array.Empty<TaskItemTagDto>(), BoardId: null);
            return Task.FromResult(TaskCoreResult<TaskItemSummaryDto>.Ok(item));
        }

        public Task<TaskCoreResult<TaskItemDetailDto>> CreateSubtaskAsync(Guid parentId, string title, Guid? assigneeTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateCustomFieldsAsync(Guid taskId, string? customFieldsJson, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class FakeForms : IFormResponseService
    {
        public IReadOnlyDictionary<string, FormFieldValue>? LastData { get; private set; }
        public bool LastSubmit { get; private set; }
        public Guid? LastAgentId { get; private set; }
        public int CreateFormCalls { get; private set; }

        public Task<FormResult<TaskConceptFormItemDto>> CreateTaskConceptFormAsync(Guid taskItemId, CancellationToken cancellationToken = default)
        {
            CreateFormCalls++;
            var item = new TaskConceptFormItemDto(Guid.NewGuid(), "T-100", FormResponseStatus.Draft, null, null, DateTimeOffset.UtcNow);
            return Task.FromResult(FormResult<TaskConceptFormItemDto>.Ok(item));
        }

        public Task<FormResult<FormResponseDto>> SaveAsync(Guid responseId, IReadOnlyDictionary<string, FormFieldValue> data, bool submit,
            Guid? submittedByTenantUserId = null, string? approvalResult = null, IReadOnlyCollection<string>? hiddenFieldCodes = null,
            Guid? executedByAiAgentId = null, CancellationToken cancellationToken = default)
        {
            LastData = data; LastSubmit = submit; LastAgentId = executedByAiAgentId;
            var dto = new FormResponseDto(responseId, DefId, "T-100", FormResponseStatus.Submitted,
                data, DateTimeOffset.UtcNow, null, 1);
            return Task.FromResult(FormResult<FormResponseDto>.Ok(dto));
        }

        public Task<FormResult<FormResponseDto>> GetOrCreateDraftAsync(Guid definitionId, string? reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResponseDto?> GetAsync(Guid responseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormResponseDto>> SetReferenceAsync(Guid responseId, string reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskStepFormDto>> GetTaskStepFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskFlowStepDto?> GetTaskCurrentStepAsync(Guid taskItemId, Guid? actorTenantUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> CloseTaskStepAsync(Guid taskItemId, Guid stepId, Guid? actorTenantUserId, string? approvalResult, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskConceptFormsDto?> GetTaskConceptFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskConceptFormsDto>> GetTaskFormGenerosAsync(Guid taskItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CreationFlowFormDto>> GetSubcategoriaCreationFlowFormsAsync(Guid subcategoriaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<TaskConceptFormItemDto>> CreateTaskFormAsync(Guid taskItemId, Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<TaskConceptFormItemDto>> DuplicateResponseAsync(Guid responseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> SetActiveTaskFormAsync(Guid responseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<Guid>> CreateDerivedFormAsync(Guid sourceResponseId, Guid targetDefinitionId, IReadOnlyDictionary<string, string>? fieldMapping, IReadOnlyDictionary<string, string>? contextDefaults = null, Guid? actorTenantUserId = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? gridMapping = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskRelatedFormDto>> GetTaskRelatedFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DerivedFormRefDto?> GetDerivedRecordAsync(Guid sourceResponseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BoardFormDto>> GetBoardFormsAsync(Guid boardId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskFormDataDto>> GetBoardTaskFormValuesAsync(Guid boardId, IReadOnlyList<Guid> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BoardGridSourceDto>> GetBoardGridSourcesAsync(Guid boardId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskGridRowsDto>> GetBoardTaskGridRowsAsync(Guid boardId, Guid formDefId, string gridFieldCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormResponseDto>> ReopenResponseAsync(Guid responseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormResponseDto>> VoidAsync(Guid responseId, string reason, Guid? byTenantUserId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> DeleteRecordAsync(Guid responseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FormRecordListItemDto>> ListRecordsAsync(Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]?> ExportRecordsXlsxAsync(Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FormRecordListItemDto>> ListChildrenAsync(Guid parentResponseId, string parentFieldCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<Guid>> AddChildAsync(Guid parentResponseId, string parentFieldCode, Guid childDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> UnlinkChildAsync(Guid parentResponseId, string parentFieldCode, Guid childResponseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<Guid>> AddRowChildAsync(Guid parentResponseId, string parentFieldCode, string parentRowId, Guid childDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FormRecordListItemDto>> ListRowChildrenAsync(Guid parentResponseId, string parentFieldCode, string parentRowId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> FindRowChildAsync(Guid parentResponseId, string parentFieldCode, string parentRowId, Guid childDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, int>>> CountRowChildrenAsync(Guid parentResponseId, string parentFieldCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> ResolveDefinitionIdByCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeFormDefs : IFormDefinitionService
    {
        public FormDefinitionDetailDto? Def { get; set; }

        public Task<FormDefinitionDetailDto?> GetAsync(Guid definitionId, CancellationToken cancellationToken = default) => Task.FromResult(Def);

        public Task<IReadOnlyList<FormDefinitionListItemDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> CreateAsync(CreateFormDefinitionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<string>> ExportAsync(Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> ImportAsync(string json, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> UpdateHeaderAsync(Guid definitionId, UpdateFormDefinitionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> SetTransactionalAsync(Guid definitionId, SetFormTransactionalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<long>> SetSequenceNextAsync(Guid definitionId, long next, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> SetCustomCssAsync(Guid definitionId, SetFormCssRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> SetStatusLadderAsync(Guid definitionId, string? statusLadderJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> SetModuleAsync(Guid definitionId, SetFormModuleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> ActivateAsync(Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormDefinitionDetailDto>> DeactivateAsync(Guid definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> SetArchivedAsync(Guid definitionId, bool archived, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormContainerDto>> AddContainerAsync(Guid definitionId, SaveFormContainerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormContainerDto>> UpdateContainerAsync(Guid containerId, SaveFormContainerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> DeleteContainerAsync(Guid containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> MoveContainerAsync(Guid containerId, bool moveUp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> MoveContainerToAsync(Guid containerId, Guid? parentId, int index, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormQuestionDto>> AddQuestionAsync(Guid definitionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<FormQuestionDto>> UpdateQuestionAsync(Guid questionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> DeleteQuestionAsync(Guid questionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> MoveQuestionAsync(Guid questionId, bool moveUp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> MoveQuestionToAsync(Guid questionId, Guid? containerId, int index, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FormResult<bool>> AssignToWorkflowNodeAsync(Guid workflowNodeId, Guid? definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> GetWorkflowNodeFormAsync(Guid workflowNodeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // IApplicationDbContext de mentira: solo respalda ActividadSubcategorias (InMemory); el resto lanza.
    // El toolset, en estos tests (sin ConversationId y con TaskBoardId null), NO toca Messages/TaskBoards/
    // Conversations/TaskItemAttachments, asi que basta con lanzar en esos.
    private sealed class FakeActDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<ActividadSubcategoria> ActividadSubcategorias => inner.ActividadSubcategorias;

        public DbSet<PlatformUser> PlatformUsers => throw new NotSupportedException();
        public DbSet<TenantUser> TenantUsers => throw new NotSupportedException();
        public DbSet<Tenant> Tenants => throw new NotSupportedException();
        public DbSet<TenantEmailConfig> TenantEmailConfigs => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellVoiceLine> RetellVoiceLines => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.VoiceCall> VoiceCalls => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellAgentMap> RetellAgentMaps => throw new NotSupportedException();
        public DbSet<StorageConfig> StorageConfigs => throw new NotSupportedException();
        public DbSet<Asesor> Asesores => throw new NotSupportedException();
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
        public DbSet<Message> Messages => throw new NotSupportedException();
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
        public DbSet<TaskBoard> TaskBoards => throw new NotSupportedException();
        public DbSet<TaskBoardColumn> TaskBoardColumns => throw new NotSupportedException();
        public DbSet<TaskCard> TaskCards => throw new NotSupportedException();
        public DbSet<TaskCardAssignment> TaskCardAssignments => throw new NotSupportedException();
        public DbSet<TaskCardTag> TaskCardTags => throw new NotSupportedException();
        public DbSet<TaskCardTagAssignment> TaskCardTagAssignments => throw new NotSupportedException();
        public DbSet<TaskCardChecklistItem> TaskCardChecklistItems => throw new NotSupportedException();
        public DbSet<TaskCardActivity> TaskCardActivities => throw new NotSupportedException();
        public DbSet<TaskCardAttachment> TaskCardAttachments => throw new NotSupportedException();
        public DbSet<ActivityType> ActivityTypes => throw new NotSupportedException();
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
        public DbSet<TaskItemAttachment> TaskItemAttachments => throw new NotSupportedException();
        public DbSet<TaskItemChecklistItem> TaskItemChecklistItems => throw new NotSupportedException();
        public DbSet<TaskItemAssignment> TaskItemAssignments => throw new NotSupportedException();
        public DbSet<TaskFieldDefinition> TaskFieldDefinitions => throw new NotSupportedException();
        public DbSet<TenantSequence> TenantSequences => throw new NotSupportedException();
        public DbSet<WorkflowDefinition> WorkflowDefinitions => throw new NotSupportedException();
        public DbSet<CardTag> CardTags => throw new NotSupportedException();
        public DbSet<FlowTag> FlowTags => throw new NotSupportedException();
        public DbSet<FormTag> FormTags => throw new NotSupportedException();
        public DbSet<MarketplaceItem> MarketplaceItems => throw new NotSupportedException();
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

    // ---- Helpers ----

    private static FormQuestionDto Q(string code, string label, FormControlType type, bool required, string? optionsJson = null)
        => new(Guid.NewGuid(), null, code, label, null, null, type, optionsJson, required, 0, "col-12", null, null);

    private static FormDefinitionDetailDto Def(params FormQuestionDto[] qs)
        => new(DefId, "FRM-LEAD", "Registro Lead", null, FormStatus.Active, 1, false, 1,
            Array.Empty<FormContainerDto>(), qs);

    private static (ActividadesToolset Ts, FakeTasks Tasks, FakeForms Forms) NewToolset(FormDefinitionDetailDto? def, bool withConcept = true)
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        if (withConcept)
        {
            inner.ActividadSubcategorias.Add(new ActividadSubcategoria
            {
                Id = SubId, TenantId = Tenant, CategoriaId = Guid.NewGuid(),
                Codigo = "LEAD-01", Nombre = "f1.CAPTACION DE LEAD test",
                FormDefinitionId = def is null ? null : DefId
            });
            inner.SaveChanges();
        }
        var db = new FakeActDb(inner);
        var tasks = new FakeTasks();
        var forms = new FakeForms();
        var defs = new FakeFormDefs { Def = def };
        return (new ActividadesToolset(db, tasks, forms, defs, new FakeTenant()), tasks, forms);
    }

    private static async Task<JsonElement> RunAsync(ActividadesToolset ts, string tool, object args)
    {
        var res = await ts.ExecuteAsync(tool, JsonSerializer.Serialize(args), Guid.NewGuid(), autonomous: true);
        return JsonDocument.Parse(res.Json).RootElement;
    }

    // ---- Tests ----

    [Fact]
    public async Task VerFormularioConcepto_lista_campos_y_opciones()
    {
        var def = Def(
            Q("nombre", "Nombre Contacto", FormControlType.Text, required: true),
            Q("area", "Area interesada", FormControlType.Radio, required: true,
                optionsJson: """[{"id":"ventas","label":"Ventas"},{"id":"soporte","label":"Soporte"}]"""));
        var (ts, _, _) = NewToolset(def);

        var r = await RunAsync(ts, "ver_formulario_concepto", new { concepto = "f1.CAPTACION DE LEAD test" });

        Assert.True(r.GetProperty("ok").GetBoolean());
        var campos = r.GetProperty("campos").EnumerateArray().ToList();
        Assert.Equal(2, campos.Count);
        var area = campos.First(c => c.GetProperty("field_code").GetString() == "area");
        Assert.True(area.GetProperty("requerido").GetBoolean());
        var opciones = area.GetProperty("opciones").EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "ventas", "soporte" }, opciones);
    }

    [Fact]
    public async Task CrearActividad_exito_crea_con_subcategoria_y_envia_formulario()
    {
        var def = Def(
            Q("nombre", "Nombre Contacto", FormControlType.Text, required: true),
            Q("area", "Area interesada", FormControlType.Radio, required: true,
                optionsJson: """[{"id":"ventas","label":"Ventas"},{"id":"soporte","label":"Soporte"}]"""),
            Q("detalle", "Necesidad", FormControlType.TextArea, required: false));
        var (ts, tasks, forms) = NewToolset(def);

        JsonElement r;
        using (AiToolRunContext.Begin(null, null, null, null, null, agentId: AgentId))
        {
            r = await RunAsync(ts, "crear_actividad", new
            {
                concepto = "LEAD-01",
                datos = new { nombre = "Juan Perez", area = "ventas", detalle = "Quiero cotizar" }
            });
        }

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("T-100", r.GetProperty("ticket").GetString());
        // La actividad se creo tipada por el concepto (SubcategoriaId) y sin BoardId (hereda del concepto).
        Assert.Equal(1, tasks.CreateCalls);
        Assert.Equal(SubId, tasks.LastRequest!.SubcategoriaId);
        Assert.Null(tasks.LastRequest.BoardId);
        Assert.Equal("Juan Perez", tasks.LastRequest.RequesterName);
        // El formulario se lleno y ENVIO con los valores por field_code, con el agente como autor.
        Assert.True(forms.LastSubmit);
        Assert.Equal(AgentId, forms.LastAgentId);
        Assert.NotNull(forms.LastData);
        Assert.Equal("Juan Perez", forms.LastData!["nombre"].Value);
        Assert.Equal("ventas", forms.LastData["area"].Value);
        Assert.Equal("Quiero cotizar", forms.LastData["detalle"].Value);
    }

    [Fact]
    public async Task CrearActividad_opcion_invalida_no_crea_y_devuelve_campos()
    {
        var def = Def(
            Q("nombre", "Nombre Contacto", FormControlType.Text, required: true),
            Q("area", "Area interesada", FormControlType.Radio, required: true,
                optionsJson: """[{"id":"ventas","label":"Ventas"},{"id":"soporte","label":"Soporte"}]"""));
        var (ts, tasks, forms) = NewToolset(def);

        var r = await RunAsync(ts, "crear_actividad", new
        {
            concepto = "LEAD-01",
            datos = new { nombre = "Juan", area = "marketing" }   // opcion invalida
        });

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.True(r.GetProperty("campos").TryGetProperty("area", out _));
        // NO se creo nada (ni actividad ni formulario): no dejar actividad huerfana.
        Assert.Equal(0, tasks.CreateCalls);
        Assert.Equal(0, forms.CreateFormCalls);
    }

    [Fact]
    public async Task CrearActividad_requerido_faltante_no_crea()
    {
        var def = Def(
            Q("nombre", "Nombre Contacto", FormControlType.Text, required: true),
            Q("detalle", "Necesidad", FormControlType.TextArea, required: false));
        var (ts, tasks, _) = NewToolset(def);

        var r = await RunAsync(ts, "crear_actividad", new
        {
            concepto = "LEAD-01",
            datos = new { detalle = "algo" }   // falta 'nombre' (requerido)
        });

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.True(r.GetProperty("campos").TryGetProperty("nombre", out _));
        Assert.Equal(0, tasks.CreateCalls);
    }

    [Fact]
    public async Task Concepto_inexistente_devuelve_error()
    {
        var (ts, _, _) = NewToolset(def: null, withConcept: false);
        var r = await RunAsync(ts, "crear_actividad", new { concepto = "NO-EXISTE", datos = new { } });
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Contains("No existe un concepto", r.GetProperty("error").GetString());
    }
}
