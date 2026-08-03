using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Fase 2 del scheduling (ADR-0060): una corrida PROGRAMADA de un conector RestApi debe reconciliar
/// (Upsert por la columna clave) en vez de duplicar. Se prueba la cadena EXACTA que arma el disparo del
/// scheduler (ProcessRunner.RunRestServerDirectAsync) a nivel Application, sin levantar el worker:
///   1. el proceso persiste Mode=Upsert + KeyColumn;
///   2. ese Mode/KeyColumn se castea y se pasa a <see cref="ConnectorRunPlanner"/> (el MISMO planner
///      que usa el /run manual), produciendo un ApiImportRequest en modo Upsert con la clave correcta;
///   3. el nucleo de ingesta compartido aplica ese modo sobre data existente sin duplicar.
/// </summary>
public class ScheduledUpsertRunTests
{
    // ---- EF InMemory con solo lo que toca el nucleo de ingesta (mismo patron que RowIngestServiceTests) ----

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<DataContainerRow> DataContainerRows => Set<DataContainerRow>();
        public DbSet<DataContainerCell> DataContainerCells => Set<DataContainerCell>();
        public DbSet<DataContainerLink> DataContainerLinks => Set<DataContainerLink>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<DataContainerRow>().Ignore(r => r.Container).Ignore(r => r.Cells);
            b.Entity<DataContainerCell>().Ignore(c => c.Row).Ignore(c => c.Column);
            b.Entity<DataContainerLink>().Ignore(l => l.Column).Ignore(l => l.Row).Ignore(l => l.TargetRow);
        }
    }

    private sealed class FakeIngestDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<DataContainerRow> DataContainerRows => inner.DataContainerRows;
        public DbSet<DataContainerCell> DataContainerCells => inner.DataContainerCells;
        public DbSet<DataContainerLink> DataContainerLinks => inner.DataContainerLinks;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool HasActiveTransaction => false;

        // El nucleo de ingesta solo necesita las tres tablas EAV de arriba; el resto de la interfaz no
        // se toca en esta prueba (igual criterio que FakeAppDb de RowIngestServiceTests).
        public DbSet<T> NotUsed<T>() where T : class => throw new NotSupportedException();
        public DbSet<PlatformUser> PlatformUsers => NotUsed<PlatformUser>();
        public DbSet<TenantUser> TenantUsers => NotUsed<TenantUser>();
        public DbSet<Tenant> Tenants => NotUsed<Tenant>();
        public DbSet<DataModelRelation> DataModelRelations => NotUsed<DataModelRelation>();
        public DbSet<DataModelRelationLink> DataModelRelationLinks => NotUsed<DataModelRelationLink>();
        public DbSet<ReportDefinition> ReportDefinitions => NotUsed<ReportDefinition>();
        public DbSet<ReportDefinitionRole> ReportDefinitionRoles => NotUsed<ReportDefinitionRole>();
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
        public DbSet<WhatsAppLine> WhatsAppLines => NotUsed<WhatsAppLine>();
        public DbSet<PipelineStage> PipelineStages => NotUsed<PipelineStage>();
        public DbSet<PipelineFieldDefinition> PipelineFieldDefinitions => NotUsed<PipelineFieldDefinition>();
        public DbSet<BusinessUnit> BusinessUnits => NotUsed<BusinessUnit>();
        public DbSet<Lead> Leads => NotUsed<Lead>();
        public DbSet<LeadActivity> LeadActivities => NotUsed<LeadActivity>();
        public DbSet<LeadNote> LeadNotes => NotUsed<LeadNote>();
        public DbSet<LeadFile> LeadFiles => NotUsed<LeadFile>();
        public DbSet<ContactImportBatch> ContactImportBatches => NotUsed<ContactImportBatch>();
        public DbSet<FollowUpTask> FollowUpTasks => NotUsed<FollowUpTask>();
        public DbSet<Conversation> Conversations => NotUsed<Conversation>();
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
        public DbSet<Tercero> Terceros => NotUsed<Tercero>();
        public DbSet<TerceroContacto> TerceroContactos => NotUsed<TerceroContacto>();
        public DbSet<TerceroFieldDefinition> TerceroFieldDefinitions => NotUsed<TerceroFieldDefinition>();
        public DbSet<TerceroNota> TerceroNotas => NotUsed<TerceroNota>();
        public DbSet<BolsaColumna> BolsaColumnas => NotUsed<BolsaColumna>();
        public DbSet<Oportunidad> Oportunidades => NotUsed<Oportunidad>();
        public DbSet<OportunidadEstado> OportunidadEstados => NotUsed<OportunidadEstado>();
        public DbSet<Cita> Citas => NotUsed<Cita>();
        public DbSet<TerceroFiltro> TerceroFiltros => NotUsed<TerceroFiltro>();
        public DbSet<ProspectoScrapeado> ProspectosScrapeados => NotUsed<ProspectoScrapeado>();
        public DbSet<ContactWorkflow> ContactWorkflows => NotUsed<ContactWorkflow>();
        public DbSet<ContactWorkflowStep> ContactWorkflowSteps => NotUsed<ContactWorkflowStep>();
        public DbSet<ContactWorkflowSchedule> ContactWorkflowSchedules => NotUsed<ContactWorkflowSchedule>();
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

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Container = Guid.NewGuid();
    private static readonly Guid ConnectorId = Guid.NewGuid();
    private static readonly Guid ColSiigoId = Guid.NewGuid();
    private static readonly Guid ColName = Guid.NewGuid();

    // Columnas de la tabla destino: nombre -> id (lo mismo que arma el disparo desde DataContainerColumns).
    private static Dictionary<string, Guid> ColumnsByName() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Siigo Id"] = ColSiigoId,
        ["nombre"] = ColName,
    };

    // MappingJson del conector Siigo (RestFetchSpec camelCase): incluye ruta ANIDADA para el nombre.
    private static string SiigoMappingJson() => JsonSerializer.Serialize(new
    {
        baseUrl = "https://api.siigo.com/v1/customers",
        arrayPath = "results",
        paging = new { mode = "Page", pageParam = "page", limitParam = "page_size", startValue = 1, pageSize = 100, maxPages = 50 },
        fields = new[]
        {
            new { column = "Siigo Id", path = "id" },
            new { column = "nombre", path = "name.0" },
        }
    });

    [Fact]
    public void Disparo_programado_castea_el_Mode_persistido_a_ApiImportMode()
    {
        // El disparo server-direct hace exactamente este cast; es load-bearing (ambos enums deben alinear).
        Assert.Equal((int)ApiImportMode.Append, (int)ImportRunMode.Append);
        Assert.Equal((int)ApiImportMode.Replace, (int)ImportRunMode.Replace);
        Assert.Equal((int)ApiImportMode.Upsert, (int)ImportRunMode.Upsert);
        Assert.Equal(ApiImportMode.Upsert, (ApiImportMode)ImportRunMode.Upsert);
    }

    [Fact]
    public void El_planner_del_disparo_usa_el_Mode_y_KeyColumn_persistidos_del_proceso()
    {
        // Un proceso CRON que persiste Upsert por "Siigo Id" (lo que crea PUT /schedule).
        var process = new ImportProcess
        {
            TenantId = Tenant,
            ModelId = Guid.NewGuid(),
            ConnectorId = ConnectorId,
            Name = "Programacion Siigo",
            ScheduleKind = ImportScheduleKind.Cron,
            CronExpression = "0 3 * * *",
            Mode = ImportRunMode.Upsert,
            KeyColumn = "Siigo Id",
            IsActive = true
        };

        // La MISMA llamada que hace ProcessRunner.RunRestServerDirectAsync: castea el Mode persistido y
        // pasa el KeyColumn persistido al planner del /run.
        var plan = ConnectorRunPlanner.Build(
            process.ConnectorId!.Value, Container, SiigoMappingJson(), ColumnsByName(),
            (ApiImportMode)process.Mode, process.KeyColumn);

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal(ApiImportMode.Upsert, plan.Request!.Mode);
        Assert.Equal(ColSiigoId, plan.Request.KeyColumnId);        // reconcilia por "Siigo Id"
        Assert.Equal("id", plan.Request.ColumnToField[ColSiigoId]);
        Assert.Equal("name.0", plan.Request.ColumnToField[ColName]); // ruta anidada preservada
    }

    [Fact]
    public async Task Corrida_programada_en_Upsert_reconcilia_sin_duplicar_sobre_data_existente()
    {
        // Data que ya existe (de una corrida previa): un cliente Siigo con id "SIIGO-1".
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var existente = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(existente);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColSiigoId, Value = "SIIGO-1" });
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColName, Value = "viejo" });
        await inner.SaveChangesAsync();

        var db = new FakeIngestDb(inner);
        var ingest = new RowIngestService(db);

        // El proceso persiste Upsert por "Siigo Id"; el disparo arma el plan igual que el /run.
        var process = new ImportProcess
        {
            TenantId = Tenant, ModelId = Guid.NewGuid(), ConnectorId = ConnectorId,
            ScheduleKind = ImportScheduleKind.Cron, CronExpression = "0 3 * * *",
            Mode = ImportRunMode.Upsert, KeyColumn = "Siigo Id", Name = "P"
        };
        var plan = ConnectorRunPlanner.Build(
            ConnectorId, Container, SiigoMappingJson(), ColumnsByName(),
            (ApiImportMode)process.Mode, process.KeyColumn);
        Assert.True(plan.Ok, plan.Error);

        // El nucleo de ingesta (mismo que usa ApiImportService.ImportAsync) aplica el modo del plan.
        // Se mapea columnaId -> nombre de campo del JSON; la clave la marca el plan.
        var mapping = plan.Request!.ColumnToField.ToDictionary(kv => kv.Key, kv => kv.Value);
        var session = ingest.CreateSession(Container, Tenant, mapping, plan.Request.Mode, plan.Request.KeyColumnId);
        await session.PrepareAsync(default);

        // La corrida trae "SIIGO-1" de nuevo (actualizado) y un "SIIGO-2" nuevo. Las claves usan el
        // nombre de campo del JSON (id / name.0), igual que produce ApiImportService al proyectar filas.
        var filas = new[]
        {
            new Dictionary<string, string?> { ["id"] = "SIIGO-1", ["name.0"] = "Cliente Uno" },
            new Dictionary<string, string?> { ["id"] = "SIIGO-2", ["name.0"] = "Cliente Dos" },
        };
        await session.IngestChunkAsync(filas.Cast<IReadOnlyDictionary<string, string?>>().ToList(), default);

        // Reconcilia: SIIGO-1 se ACTUALIZA (no se duplica), SIIGO-2 se inserta -> 2 filas, no 3.
        Assert.Equal(1, session.Updated);
        Assert.Equal(1, session.Inserted);
        Assert.Equal(2, inner.DataContainerRows.Count());
        var actualizado = inner.DataContainerCells.Single(c => c.RowId == existente.Id && c.ColumnId == ColName).Value;
        Assert.Equal("Cliente Uno", actualizado);
    }

    [Fact]
    public async Task Corrida_via_agente_en_Upsert_reconcilia_por_columna_sin_duplicar()
    {
        // El camino VIA AGENTE (ADR-0061) usa la MISMA IRowIngestService que el server-direct, pero con
        // la convencion del agente (la de DispatchFetchAsync): mapping columnaId -> NOMBRE de columna
        // (el agente ya aplico el mapeo campo->columna del RestFetchSpec, asi que sus filas vienen
        // indexadas por NOMBRE de columna, no por ruta JSON), y el keyColumnId es el Guid de la columna
        // clave. Se prueba que ese ingest reconcilia por "Siigo Id" sin duplicar, igual que el server.
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var existente = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(existente);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColSiigoId, Value = "SIIGO-1" });
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColName, Value = "viejo" });
        await inner.SaveChangesAsync();

        var db = new FakeIngestDb(inner);
        var ingest = new RowIngestService(db);

        // Convencion del agente: columnaId -> NOMBRE de columna (NO ruta JSON). keyColumnId = Guid de la clave.
        var mapping = new Dictionary<Guid, string> { [ColSiigoId] = "Siigo Id", [ColName] = "nombre" };
        var session = ingest.CreateSession(Container, Tenant, mapping, ApiImportMode.Upsert, ColSiigoId);
        await session.PrepareAsync(default);

        // Filas tal como las devuelve el agente (FetchResult): indexadas por NOMBRE de columna.
        var filas = new[]
        {
            new Dictionary<string, string?> { ["Siigo Id"] = "SIIGO-1", ["nombre"] = "Cliente Uno" },
            new Dictionary<string, string?> { ["Siigo Id"] = "SIIGO-2", ["nombre"] = "Cliente Dos" },
        };
        await session.IngestChunkAsync(filas.Cast<IReadOnlyDictionary<string, string?>>().ToList(), default);

        // SIIGO-1 se ACTUALIZA (no se duplica), SIIGO-2 se inserta -> 2 filas, no 3.
        Assert.Equal(1, session.Updated);
        Assert.Equal(1, session.Inserted);
        Assert.Equal(2, inner.DataContainerRows.Count());
        var actualizado = inner.DataContainerCells.Single(c => c.RowId == existente.Id && c.ColumnId == ColName).Value;
        Assert.Equal("Cliente Uno", actualizado);
    }
}
