using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del ContenedorDatosToolset (ADR-0103): cargar_productos inserta N filas + celdas en el contenedor
/// "Productos" mapeando cada campo a la columna con ese nombre (case-insensitive); los campos que no son
/// columnas se ignoran; un contenedor inexistente devuelve error. Corre sobre EF InMemory.
/// </summary>
public class ContenedorDatosToolsetTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<DataContainer> DataContainers => Set<DataContainer>();
        public DbSet<DataContainerColumn> DataContainerColumns => Set<DataContainerColumn>();
        public DbSet<DataContainerRow> DataContainerRows => Set<DataContainerRow>();
        public DbSet<DataContainerCell> DataContainerCells => Set<DataContainerCell>();
        public DbSet<DataContainerLink> DataContainerLinks => Set<DataContainerLink>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<DataContainer>(e => { e.Ignore(x => x.Model); e.Ignore(x => x.ParentContainer); e.Ignore(x => x.Columns); e.Ignore(x => x.Rows); });
            b.Entity<DataContainerColumn>(e => { e.Ignore(x => x.Container); e.Ignore(x => x.ChildContainer); });
            b.Entity<DataContainerRow>(e => { e.Ignore(x => x.Container); e.Ignore(x => x.ParentRow); e.Ignore(x => x.Cells); });
            b.Entity<DataContainerCell>(e => { e.Ignore(x => x.Row); e.Ignore(x => x.Column); });
            b.Entity<DataContainerLink>(e => { e.Ignore(x => x.Column); e.Ignore(x => x.Row); e.Ignore(x => x.TargetRow); });
        }
    }

    private sealed class FakeTenant : ITenantContext { public Guid? TenantId => Tenant; public Guid? UserId => null; }

    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<PlatformUser> PlatformUsers => throw new NotSupportedException();
        public DbSet<TenantUser> TenantUsers => throw new NotSupportedException();
        public DbSet<Tenant> Tenants => throw new NotSupportedException();
        public DbSet<TenantEmailConfig> TenantEmailConfigs => throw new NotSupportedException();
        public DbSet<TenantTelegramConfig> TenantTelegramConfigs => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellVoiceLine> RetellVoiceLines => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.VoiceCall> VoiceCalls => throw new NotSupportedException();
        public DbSet<Ecorex.Domain.Entities.RetellAgentMap> RetellAgentMaps => throw new NotSupportedException();
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
        public DbSet<CardTag> CardTags => throw new NotSupportedException();
        public DbSet<FlowTag> FlowTags => throw new NotSupportedException();
        public DbSet<FormTag> FormTags => throw new NotSupportedException();
        public DbSet<MarketplaceItem> MarketplaceItems => throw new NotSupportedException();
        public DbSet<WorkflowNode> WorkflowNodes => throw new NotSupportedException();
        public DbSet<WorkflowEdge> WorkflowEdges => throw new NotSupportedException();
        public DbSet<WorkflowDecisionToken> WorkflowDecisionTokens => throw new NotSupportedException();
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
        public DbSet<TenantOperatingDay> TenantOperatingDays => throw new NotSupportedException();
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
        public DbSet<DataContainer> DataContainers => inner.DataContainers;
        public DbSet<DataContainerColumn> DataContainerColumns => inner.DataContainerColumns;
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

    private static (ContenedorDatosToolset Ts, InnerDb Inner) NewToolset()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new ContenedorDatosToolset(new FakeAppDb(inner), new FakeTenant()), inner);
    }

    private static Guid SeedProductos(InnerDb inner, params string[] columnNames)
    {
        var containerId = Guid.NewGuid();
        inner.DataContainers.Add(new DataContainer { Id = containerId, TenantId = Tenant, Name = "Productos" });
        var sort = 0;
        foreach (var col in columnNames)
        {
            inner.DataContainerColumns.Add(new DataContainerColumn
            {
                TenantId = Tenant, ContainerId = containerId, Name = col,
                Type = DataContainerColumnType.Text, SortOrder = sort++
            });
        }
        inner.SaveChanges();
        return containerId;
    }

    private static async Task<JsonElement> RunAsync(ContenedorDatosToolset ts, string tool, object args)
    {
        var res = await ts.ExecuteAsync(tool, JsonSerializer.Serialize(args), Guid.NewGuid(), true);
        using var doc = JsonDocument.Parse(res.Json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CargarProductos_inserta_filas_y_celdas_mapeando_por_columna()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "categoria", "marca", "nombre", "precio", "archivo");

        var r = await RunAsync(ts, "cargar_productos", new
        {
            productos = new object[]
            {
                new { categoria = "PC", marca = "Acme", nombre = "Equipo A", precio = 1000000, archivo = "prueba.txt", campo_inexistente = "x" },
                new { categoria = "PC", marca = "Acme", nombre = "Equipo B", precio = 2000000, archivo = "prueba.txt" }
            }
        });

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(2, r.GetProperty("cargados").GetInt32());
        Assert.Equal("Productos", r.GetProperty("contenedor").GetString());

        var rowIds = inner.DataContainerRows.Where(x => x.ContainerId == cid).Select(x => x.Id).ToList();
        Assert.Equal(2, rowIds.Count);
        // 2 filas x 5 columnas = 10 celdas; el campo 'campo_inexistente' se ignora (no es columna).
        Assert.Equal(10, inner.DataContainerCells.Count(c => rowIds.Contains(c.RowId)));
        // Precio numerico guardado como texto.
        var precioCol = inner.DataContainerColumns.Single(c => c.ContainerId == cid && c.Name == "precio").Id;
        Assert.Contains(inner.DataContainerCells, c => c.ColumnId == precioCol && c.Value == "1000000");
        // 'archivo' poblado.
        var archivoCol = inner.DataContainerColumns.Single(c => c.ContainerId == cid && c.Name == "archivo").Id;
        Assert.All(inner.DataContainerCells.Where(c => c.ColumnId == archivoCol), c => Assert.Equal("prueba.txt", c.Value));
    }

    [Fact]
    public async Task CargarProductos_campos_que_no_son_columnas_se_ignoran()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "nombre");
        var r = await RunAsync(ts, "cargar_productos", new
        {
            productos = new object[] { new { nombre = "X", marca = "no-es-columna", precio = 9 } }
        });
        Assert.True(r.GetProperty("ok").GetBoolean());
        var rowId = inner.DataContainerRows.Single(x => x.ContainerId == cid).Id;
        Assert.Equal(1, inner.DataContainerCells.Count(c => c.RowId == rowId));
        Assert.Equal("X", inner.DataContainerCells.Single(c => c.RowId == rowId).Value);
    }

    [Fact]
    public async Task CargarProductos_contenedor_inexistente_devuelve_error()
    {
        var (ts, _) = NewToolset();
        var r = await RunAsync(ts, "cargar_productos", new { productos = new object[] { new { nombre = "X" } } });
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Contains("Productos", r.GetProperty("error").GetString());
    }

    // Siembra una fila con celdas (el InnerDb de test no tiene interceptor: CreatedAt/UpdatedAt se ponen a mano).
    private static void SeedRow(InnerDb inner, Guid containerId, DateTimeOffset? updatedAt, params (string Col, string Val)[] cells)
    {
        var row = new DataContainerRow { TenantId = Tenant, ContainerId = containerId, CreatedAt = DateTimeOffset.UtcNow.AddDays(-30) };
        if (updatedAt is DateTimeOffset u) { row.UpdatedAt = u; }
        inner.DataContainerRows.Add(row);
        var colByName = inner.DataContainerColumns.Where(c => c.ContainerId == containerId)
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var (col, val) in cells)
        {
            if (colByName.TryGetValue(col, out var colId))
            {
                inner.DataContainerCells.Add(new DataContainerCell { TenantId = Tenant, RowId = row.Id, ColumnId = colId, Value = val });
            }
        }
        inner.SaveChanges();
    }

    [Fact]
    public async Task ConsultarProductos_encuentra_por_texto_y_trae_campos_y_fecha()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "nombre", "marca", "precio", "proveedor");
        SeedRow(inner, cid, new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero),
            ("nombre", "Tornillo 1/4"), ("marca", "Acme"), ("precio", "1500"), ("proveedor", "Ferreteria X"));
        SeedRow(inner, cid, null, ("nombre", "Tuerca 1/4"), ("marca", "Acme"), ("precio", "900"), ("proveedor", "Ferreteria Y"));

        var r = await RunAsync(ts, "consultar_productos", new { texto = "tornillo" });   // case-insensitive

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var res = r.GetProperty("resultados").EnumerateArray().Single();
        Assert.Equal("Tornillo 1/4", res.GetProperty("nombre").GetString());
        Assert.Equal("1500", res.GetProperty("precio").GetString());
        Assert.Equal("Ferreteria X", res.GetProperty("proveedor").GetString());
        Assert.Equal("2026-09-18", res.GetProperty("fecha_actualizacion").GetString());   // UpdatedAt, zona tenant
    }

    [Fact]
    public async Task ConsultarProductos_articulo_inexistente_total_cero()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "nombre", "precio");
        SeedRow(inner, cid, null, ("nombre", "Tornillo"), ("precio", "1"));
        var r = await RunAsync(ts, "consultar_productos", new { texto = "zzz-inexistente" });
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(0, r.GetProperty("total").GetInt32());
        Assert.Empty(r.GetProperty("resultados").EnumerateArray());
    }

    [Fact]
    public async Task ConsultarProductos_sin_texto_lista_todas_y_respeta_limite()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "nombre", "precio");
        for (var i = 0; i < 5; i++) { SeedRow(inner, cid, null, ("nombre", $"Item {i}"), ("precio", i.ToString())); }
        var r = await RunAsync(ts, "consultar_productos", new { limite = 3 });
        Assert.Equal(5, r.GetProperty("total").GetInt32());                  // total = coincidencias
        Assert.Equal(3, r.GetProperty("resultados").GetArrayLength());       // aplica el limite
    }

    [Fact]
    public async Task ConsultarContenedor_generico_por_nombre_y_inexistente_error()
    {
        var (ts, inner) = NewToolset();
        var cid = SeedProductos(inner, "nombre");
        SeedRow(inner, cid, null, ("nombre", "Cosa"));
        var ok = await RunAsync(ts, "consultar_contenedor", new { contenedor = "Productos", texto = "cosa" });
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal(1, ok.GetProperty("total").GetInt32());

        var bad = await RunAsync(ts, "consultar_contenedor", new { contenedor = "NoExiste" });
        Assert.False(bad.GetProperty("ok").GetBoolean());
        Assert.Contains("Productos", bad.GetProperty("error").GetString());   // lista los disponibles
    }

    [Fact]
    public async Task ListarContenedores_devuelve_contenedor_y_columnas()
    {
        var (ts, inner) = NewToolset();
        SeedProductos(inner, "categoria", "nombre");
        var r = await RunAsync(ts, "listar_contenedores", new { });
        Assert.True(r.GetProperty("ok").GetBoolean());
        var cont = r.GetProperty("contenedores").EnumerateArray().Single();
        Assert.Equal("Productos", cont.GetProperty("nombre").GetString());
        var cols = cont.GetProperty("columnas").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("categoria", cols);
        Assert.Contains("nombre", cols);
    }
}
