using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Reinicio NO DESTRUCTIVO de la memoria del agente por contacto (Opcion B): marca AgentContextResetAt en la
/// conversacion y NO borra los mensajes del chat (el runtime solo considera mensajes con SentAt > la marca).
/// </summary>
public class AiAgentLineServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<Message> Messages => Set<Message>();
        protected override void OnModelCreating(ModelBuilder b)
            => b.Entity<Message>(e => e.Ignore(x => x.Conversation));
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;
        public Guid? UserId => User;
    }

    private sealed class FakeAudit : IAuditWriter
    {
        public int Writes { get; private set; }
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId, object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null, AuditActorType actorType = AuditActorType.Human) => Writes++;
    }

    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<Tercero> Terceros => throw new NotSupportedException();
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

    private static (AiAgentLineService Svc, InnerDb Inner, FakeAudit Audit) NewService()
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var audit = new FakeAudit();
        return (new AiAgentLineService(new FakeAppDb(inner), new FakeTenant(), audit), inner, audit);
    }

    [Fact]
    public async Task ResetConversationAgentContext_marca_reset_y_conserva_mensajes()
    {
        var (svc, inner, audit) = NewService();
        var conv = new Conversation { Id = Guid.NewGuid(), TenantId = Tenant, ContactPhone = "573001112233" };
        inner.Conversations.Add(conv);
        inner.Messages.Add(new Message { TenantId = Tenant, ConversationId = conv.Id, Direction = MessageDirection.Inbound, Body = "hola", MessageType = "text", SentAt = DateTimeOffset.UtcNow });
        inner.SaveChanges();

        var before = DateTimeOffset.UtcNow;
        var ok = await svc.ResetConversationAgentContextAsync(conv.Id);

        Assert.True(ok);
        var reloaded = inner.Conversations.Single();
        Assert.NotNull(reloaded.AgentContextResetAt);                 // se marco el reinicio
        Assert.True(reloaded.AgentContextResetAt >= before.AddSeconds(-5));
        Assert.Equal(1, inner.Messages.Count());                      // los mensajes NO se borraron
        Assert.Equal(1, audit.Writes);                                // se auditó
    }

    [Fact]
    public async Task ResetConversationAgentContext_inexistente_devuelve_false()
    {
        var (svc, inner, _) = NewService();
        Assert.False(await svc.ResetConversationAgentContextAsync(Guid.NewGuid()));
    }
}
