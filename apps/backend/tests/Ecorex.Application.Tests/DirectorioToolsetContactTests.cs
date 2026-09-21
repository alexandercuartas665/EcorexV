using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Directorio;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests de crear_contacto (DirectorioToolset): usa el TELEFONO REAL de la conversacion (no el que
/// invente el modelo) y DEDUPLICA por telefono (ultimos 10 digitos) cuando no hay identificacion, ademas
/// de la dedup por identificacion ya existente. Corre con fakes (EF InMemory), sin BD real.
/// </summary>
public class DirectorioToolsetContactTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // ---- Fakes ----

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<Tercero> Terceros => Set<Tercero>();
        public DbSet<Conversation> Conversations => Set<Conversation>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            // Solo escalares del Tercero (la dedup consulta Id/Nombre/Telefono/IdValor/Estado).
            b.Entity<Tercero>(e =>
            {
                e.Ignore(x => x.Empresa); e.Ignore(x => x.Contactos); e.Ignore(x => x.Categorias);
            });
        }
    }

    // ITerceroService de mentira: CreateAsync PERSISTE el tercero en el InnerDb (para que la dedup lo
    // encuentre en llamadas siguientes) y devuelve su Id/Nombre. El resto no se usa en estos tests.
    private sealed class FakeTerceros(InnerDb inner) : ITerceroService
    {
        public SaveTerceroRequest? LastRequest { get; private set; }
        public int CreateCalls { get; private set; }

        public async Task<TerceroResult<TerceroDetailDto>> CreateAsync(SaveTerceroRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request; CreateCalls++;
            var t = new Tercero
            {
                TenantId = Tenant, Nombre = request.Nombre, Tipo = request.Tipo, Estado = request.Estado,
                Ciudad = request.Ciudad, IdTipo = request.IdTipo, IdValor = request.IdValor,
                Email = request.Email, Telefono = request.Telefono
            };
            inner.Terceros.Add(t);
            await inner.SaveChangesAsync(cancellationToken);
            var dto = new TerceroDetailDto(
                t.Id, t.Nombre, t.Tipo, TerceroPerfil.Cliente, t.Estado, null, t.Ciudad, t.IdTipo, t.IdValor,
                t.IdValor ?? "", null, null, t.Email, t.Telefono, null, null,
                t.Tipo == TerceroTipo.Empresa, t.Tipo == TerceroTipo.Persona, null,
                new Dictionary<string, IReadOnlyDictionary<string, string?>>(),
                System.Array.Empty<TerceroContactoDto>());
            return TerceroResult<TerceroDetailDto>.Ok(dto);
        }

        public Task<IReadOnlyList<TerceroListItemDto>> ListAsync(TerceroListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroDedupKeys> GetDedupKeysAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<TerceroDetailDto>> UpdateAsync(Guid id, SaveTerceroRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<bool>> AssignToEmpresaAsync(Guid personaId, Guid empresaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<bool>> UnassignFromEmpresaAsync(Guid personaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TerceroContactoDto>> ListContactosAsync(Guid terceroId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<TerceroContactoDto>> AddContactoAsync(Guid terceroId, SaveContactoRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<TerceroContactoDto>> UpdateContactoAsync(Guid contactoId, SaveContactoRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<bool>> DeleteContactoAsync(Guid contactoId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<Guid>> PromoteContactoToTerceroAsync(Guid contactoId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TerceroNotaDto>> ListNotasAsync(Guid terceroId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<TerceroNotaDto>> AddNotaAsync(Guid terceroId, SaveNotaRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroResult<bool>> DeleteNotaAsync(Guid notaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TerceroKpisDto> GetKpisAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // IApplicationDbContext de mentira: respalda Terceros y Conversations (InMemory); el resto lanza.
    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<Tercero> Terceros => inner.Terceros;
        public DbSet<Conversation> Conversations => inner.Conversations;

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

    // ---- Helpers ----

    private static (DirectorioToolset Ts, FakeTerceros Terceros, InnerDb Inner) NewToolset()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var terceros = new FakeTerceros(inner);
        return (new DirectorioToolset(terceros, new FakeAppDb(inner)), terceros, inner);
    }

    private static Guid SeedConversation(InnerDb inner, string contactPhone)
    {
        var conv = new Conversation { Id = Guid.NewGuid(), TenantId = Tenant, ContactPhone = contactPhone };
        inner.Conversations.Add(conv);
        inner.SaveChanges();
        return conv.Id;
    }

    private static async Task<JsonElement> RunAsync(DirectorioToolset ts, object args)
    {
        var res = await ts.ExecuteAsync("crear_contacto", JsonSerializer.Serialize(args), Guid.NewGuid(), true);
        using var doc = JsonDocument.Parse(res.Json);
        return doc.RootElement.Clone();
    }

    // ---- Tests ----

    [Fact]
    public async Task CrearContacto_usa_telefono_real_de_la_conversacion()
    {
        var (ts, terceros, inner) = NewToolset();
        var conv = SeedConversation(inner, "573001234567");
        using (AiToolRunContext.Begin(conv, null, null, null, null))
        {
            var r = await RunAsync(ts, new { nombre = "Juan Perez", tipo = "persona", telefono = "999-INVENTADO" });
            Assert.True(r.GetProperty("ok").GetBoolean());
        }
        Assert.Equal(1, terceros.CreateCalls);
        Assert.Equal("573001234567", terceros.LastRequest!.Telefono);   // el real de la conversacion, no el del modelo
        Assert.Null(terceros.LastRequest!.IdValor);
    }

    [Fact]
    public async Task CrearContacto_segunda_vez_misma_conversacion_no_duplica()
    {
        var (ts, terceros, inner) = NewToolset();
        var conv = SeedConversation(inner, "573001234567");
        JsonElement r1, r2;
        using (AiToolRunContext.Begin(conv, null, null, null, null))
        {
            r1 = await RunAsync(ts, new { nombre = "Juan Perez", tipo = "persona" });
            r2 = await RunAsync(ts, new { nombre = "Juan P.", tipo = "persona", telefono = "3009998877" });
        }
        Assert.Equal(1, terceros.CreateCalls);   // solo se creo una vez
        Assert.True(r2.GetProperty("ya_existia").GetBoolean());
        Assert.Equal(r1.GetProperty("contacto_id").GetString(), r2.GetProperty("contacto_id").GetString());
    }

    [Fact]
    public async Task CrearContacto_dedup_por_ultimos_10_digitos_absorbe_prefijo()
    {
        var (ts, terceros, inner) = NewToolset();
        inner.Terceros.Add(new Tercero
        {
            TenantId = Tenant, Nombre = "Cliente", Tipo = TerceroTipo.Persona,
            Estado = TerceroEstado.Activo, Telefono = "573001234567"
        });
        inner.SaveChanges();
        var conv = SeedConversation(inner, "3001234567");   // mismo numero sin prefijo de pais
        using (AiToolRunContext.Begin(conv, null, null, null, null))
        {
            var r = await RunAsync(ts, new { nombre = "Cliente otra vez", tipo = "persona" });
            Assert.True(r.GetProperty("ya_existia").GetBoolean());
        }
        Assert.Equal(0, terceros.CreateCalls);
    }

    [Fact]
    public async Task CrearContacto_inactivo_no_bloquea_dedup_por_telefono()
    {
        var (ts, terceros, inner) = NewToolset();
        inner.Terceros.Add(new Tercero
        {
            TenantId = Tenant, Nombre = "Dado de baja", Tipo = TerceroTipo.Persona,
            Estado = TerceroEstado.Inactivo, Telefono = "573001234567"
        });
        inner.SaveChanges();
        var conv = SeedConversation(inner, "573001234567");
        using (AiToolRunContext.Begin(conv, null, null, null, null))
        {
            var r = await RunAsync(ts, new { nombre = "Cliente nuevo", tipo = "persona" });
            Assert.True(r.GetProperty("ok").GetBoolean());
        }
        Assert.Equal(1, terceros.CreateCalls);   // el inactivo se ignora: se crea uno nuevo
    }

    [Fact]
    public async Task CrearContacto_con_identificacion_sigue_deduplicando_por_identificacion()
    {
        var (ts, terceros, inner) = NewToolset();
        inner.Terceros.Add(new Tercero
        {
            TenantId = Tenant, Nombre = "ACME", Tipo = TerceroTipo.Empresa,
            Estado = TerceroEstado.Activo, IdValor = "900123456"
        });
        inner.SaveChanges();
        var r = await RunAsync(ts, new { nombre = "ACME SA", identificacion = "900123456", tipo_identificacion = "nit" });
        Assert.True(r.GetProperty("ya_existia").GetBoolean());
        Assert.Equal(0, terceros.CreateCalls);
    }
}
