using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del NUCLEO de ingesta compartido (doc 03 s6): el mismo que usan el import REST
/// (ApiImportService) y el importador via agente. Cubre los tres modos sobre el modelo EAV
/// (fila + celdas por columna): Append inserta siempre, Replace vacia antes, y Upsert actualiza
/// por columna clave o inserta (deduplicando tambien dentro de la misma corrida).
/// Corre sobre EF InMemory con solo las entidades que el nucleo toca.
/// </summary>
public class RowIngestServiceTests
{
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

    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<PlatformUser> PlatformUsers => throw new NotSupportedException();
        public DbSet<TenantUser> TenantUsers => throw new NotSupportedException();
        public DbSet<Tenant> Tenants => throw new NotSupportedException();
        public DbSet<TenantEmailConfig> TenantEmailConfigs => throw new NotSupportedException();
        public DbSet<StorageConfig> StorageConfigs => throw new NotSupportedException();
        public DbSet<Asesor> Asesores => throw new NotSupportedException();
        // Llegaron con el merge de fase-0/clon-backbone (modelo ER de contenedores): el nucleo de
        // ingesta no los toca, pero la interfaz los exige.
        public DbSet<DataModelRelation> DataModelRelations => throw new NotSupportedException();
        public DbSet<DataModelRelationLink> DataModelRelationLinks => throw new NotSupportedException();
        // Motor de Reportes: la ingesta EAV no lo toca, pero la interfaz lo exige.
        public DbSet<ReportDefinition> ReportDefinitions => throw new NotSupportedException();
        public DbSet<ReportDefinitionRole> ReportDefinitionRoles => throw new NotSupportedException();
        public DbSet<ReportTemplate> ReportTemplates => throw new NotSupportedException();
        public DbSet<ExternalDataSource> ExternalDataSources => throw new NotSupportedException();
        public DbSet<ExternalDataSet> ExternalDataSets => throw new NotSupportedException();
        public DbSet<ExternalDataSourceGrant> ExternalDataSourceGrants => throw new NotSupportedException();
        // Gestor Documental: la ingesta EAV no lo toca, pero la interfaz lo exige.
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
        public DbSet<DataContainerRow> DataContainerRows => inner.DataContainerRows;
        public DbSet<DataContainerCell> DataContainerCells => inner.DataContainerCells;
        public DbSet<DataContainerLink> DataContainerLinks => inner.DataContainerLinks;
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

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Container = Guid.NewGuid();
    private static readonly Guid ColCode = Guid.NewGuid();
    private static readonly Guid ColName = Guid.NewGuid();

    // Mapea columna -> campo del origen (igual que ApiImportRequest.ColumnToField).
    private static readonly Dictionary<Guid, string> Mapping = new() { [ColCode] = "code", [ColName] = "name" };

    private static (FakeAppDb Db, InnerDb Inner, RowIngestService Svc) NewDb()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var db = new FakeAppDb(inner);
        return (db, inner, new RowIngestService(db));
    }

    private static IReadOnlyDictionary<string, string?> Row(string code, string name)
        => new Dictionary<string, string?> { ["code"] = code, ["name"] = name };

    private static string? CellOf(InnerDb inner, Guid rowId, Guid colId)
        => inner.DataContainerCells.Single(c => c.RowId == rowId && c.ColumnId == colId).Value;

    [Fact]
    public async Task Append_inserta_una_fila_por_elemento_con_sus_celdas()
    {
        var (_, inner, svc) = NewDb();
        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Append, null);
        await s.PrepareAsync(default);
        await s.IngestChunkAsync(new[] { Row("A", "Ana"), Row("B", "Beto") }, default);

        Assert.Equal(2, s.Inserted);
        Assert.Equal(0, s.Updated);
        Assert.Equal(2, inner.DataContainerRows.Count());
        Assert.Equal(4, inner.DataContainerCells.Count()); // 2 filas x 2 columnas
        var rowA = inner.DataContainerRows.ToList()[0];
        Assert.Equal("A", CellOf(inner, rowA.Id, ColCode));
        Assert.Equal("Ana", CellOf(inner, rowA.Id, ColName));
        Assert.All(inner.DataContainerRows, r => Assert.Equal(Tenant, r.TenantId));
    }

    [Fact]
    public async Task Append_en_dos_chunks_acumula_sin_borrar()
    {
        var (_, inner, svc) = NewDb();
        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Append, null);
        await s.PrepareAsync(default);
        await s.IngestChunkAsync(new[] { Row("A", "Ana") }, default);
        await s.IngestChunkAsync(new[] { Row("B", "Beto") }, default);

        Assert.Equal(2, s.Inserted);
        Assert.Equal(2, inner.DataContainerRows.Count());
    }

    [Fact]
    public async Task Replace_vacia_las_filas_previas_antes_de_insertar()
    {
        var (_, inner, svc) = NewDb();
        var vieja = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(vieja);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = vieja.Id, ColumnId = ColCode, Value = "VIEJA" });
        await inner.SaveChangesAsync();

        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Replace, null);
        await s.PrepareAsync(default);
        await s.IngestChunkAsync(new[] { Row("A", "Ana") }, default);

        Assert.Equal(1, s.Deleted);
        Assert.Equal(1, s.Inserted);
        Assert.Single(inner.DataContainerRows);
        Assert.DoesNotContain(inner.DataContainerCells, c => c.Value == "VIEJA");
    }

    [Fact]
    public async Task Upsert_actualiza_la_fila_existente_por_clave_y_no_duplica()
    {
        var (_, inner, svc) = NewDb();
        var existente = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(existente);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColCode, Value = "A" });
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColName, Value = "viejo" });
        await inner.SaveChangesAsync();

        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Upsert, ColCode);
        await s.PrepareAsync(default);
        await s.IngestChunkAsync(new[] { Row("A", "Ana"), Row("B", "Beto") }, default);

        Assert.Equal(1, s.Updated);            // A existia -> se actualiza
        Assert.Equal(1, s.Inserted);           // B es nueva
        Assert.Equal(2, inner.DataContainerRows.Count());
        Assert.Equal("Ana", CellOf(inner, existente.Id, ColName)); // se actualizo, no se duplico
    }

    [Fact]
    public async Task Upsert_no_sobrescribe_con_vacio_cuando_la_ruta_no_resuelve()
    {
        // Simula el bug del /run: en Upsert, si una ruta anidada NO resuelve, el campo NO viene en la
        // fila (NestedJsonResolver.ProjectRow lo omite). El nucleo debe CONSERVAR el valor existente,
        // no borrarlo con vacio/null.
        var (_, inner, svc) = NewDb();
        var existente = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(existente);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColCode, Value = "A" });
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColName, Value = "valor previo" });
        await inner.SaveChangesAsync();

        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Upsert, ColCode);
        await s.PrepareAsync(default);
        // La fila trae la clave ("code") pero NO "name" (ruta que no resolvio -> se omitio).
        var soloClave = new Dictionary<string, string?> { ["code"] = "A" };
        await s.IngestChunkAsync(new[] { (IReadOnlyDictionary<string, string?>)soloClave }, default);

        Assert.Equal(1, s.Updated);
        Assert.Equal(0, s.Inserted);
        Assert.Equal("valor previo", CellOf(inner, existente.Id, ColName)); // se CONSERVA, no se borro
    }

    [Fact]
    public async Task Upsert_campo_presente_con_null_si_limpia_la_celda()
    {
        // Distincion clave: una ruta que resuelve a JSON null SI viene en la fila (con valor null) y
        // debe limpiar la celda (borrado explicito), a diferencia de la ruta ausente.
        var (_, inner, svc) = NewDb();
        var existente = new DataContainerRow { TenantId = Tenant, ContainerId = Container };
        inner.DataContainerRows.Add(existente);
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColCode, Value = "A" });
        inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = existente.Id, ColumnId = ColName, Value = "valor previo" });
        await inner.SaveChangesAsync();

        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Upsert, ColCode);
        await s.PrepareAsync(default);
        var conNull = new Dictionary<string, string?> { ["code"] = "A", ["name"] = null };
        await s.IngestChunkAsync(new[] { (IReadOnlyDictionary<string, string?>)conNull }, default);

        Assert.Equal(1, s.Updated);
        Assert.Null(CellOf(inner, existente.Id, ColName)); // presente con null -> se limpia
    }

    [Fact]
    public async Task Upsert_deduplica_claves_repetidas_dentro_de_la_misma_corrida()
    {
        var (_, inner, svc) = NewDb();
        var s = svc.CreateSession(Container, Tenant, Mapping, ApiImportMode.Upsert, ColCode);
        await s.PrepareAsync(default);
        await s.IngestChunkAsync(new[] { Row("A", "Ana"), Row("A", "Ana 2") }, default);

        Assert.Equal(1, s.Inserted);
        Assert.Equal(1, s.Updated);
        Assert.Single(inner.DataContainerRows);
        var row = inner.DataContainerRows.Single();
        Assert.Equal("Ana 2", CellOf(inner, row.Id, ColName)); // gana el ultimo
    }
}
