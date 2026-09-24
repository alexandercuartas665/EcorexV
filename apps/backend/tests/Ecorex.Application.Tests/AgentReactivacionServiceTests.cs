using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Motor de la SECUENCIA DE REACTIVACION (AgentReactivacionService.RunTenantAsync): elige el siguiente paso
/// pendiente segun las horas de inactividad desde el ultimo entrante y respeta la ventana de 24h de Meta
/// (texto libre &lt;=24h, plantilla &gt;24h, omitir si no hay plantilla). Reinicia al responder el cliente y
/// no toca conversaciones cerradas ni con opt-out. Corre sobre EF InMemory + fakes de envio.
/// </summary>
public class AgentReactivacionServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<AiAgent> AiAgents => Set<AiAgent>();
        public DbSet<AiAgentLineBinding> AiAgentLineBindings => Set<AiAgentLineBinding>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<TenantBlockedNumber> TenantBlockedNumbers => Set<TenantBlockedNumber>();
        public DbSet<Lead> Leads => Set<Lead>();
        public DbSet<WorkflowStepHistory> WorkflowStepHistories => Set<WorkflowStepHistory>();
        public DbSet<AiAgentRunLog> AiAgentRunLogs => Set<AiAgentRunLog>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Message>(e => e.Ignore(x => x.Conversation));
            b.Entity<AiAgentLineBinding>(e => { e.Ignore(x => x.Agent); e.Ignore(x => x.WhatsAppLine); });
            b.Entity<Lead>(e => e.Ignore(x => x.Stage));
            b.Entity<WorkflowStepHistory>(e => { e.Ignore(x => x.Instance); e.Ignore(x => x.Node); });
        }
    }

    // Reloj fijo para controlar "ahora".
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Conector: registra los envios de texto libre; el resto no se usa en estos tests.
    private sealed class FakeConnector : IWhatsAppConnectorService
    {
        public List<(Guid LineId, string Phone, string Text)> TextSends { get; } = new();
        public bool NextOk = true;

        public Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default)
        {
            TextSends.Add((lineId, phone, text));
            return Task.FromResult(new LineSendResult(NextOk, NextOk ? null : "fallo", NextOk ? "ext-" + TextSends.Count : null));
        }

        public Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendTemplateAsync(Guid lineId, string phone, string templateName, string language, IReadOnlyList<string> bodyParams, Guid actorUserId, string? headerMediaType = null, string? headerMediaUrl = null, string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> DeleteMessageForEveryoneAsync(Guid lineId, string phone, string messageId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvolutionMediaResult> FetchInboundMediaAsync(Guid lineId, string messageKeyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Sender de notificaciones: registra los envios de plantilla; el resto no se usa.
    private sealed class FakeSender : INotificationChannelSender
    {
        public List<(Guid LineId, string Phone, string Template)> TemplateSends { get; } = new();
        public bool NextOk = true;

        public Task<WhatsAppSendOutcome> SendWhatsAppTemplateAsync(Guid lineId, string phone, string templateName, string? language, IReadOnlyDictionary<string, string> tokens, Guid actorUserId, string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null, CancellationToken cancellationToken = default)
        {
            TemplateSends.Add((lineId, phone, templateName));
            return Task.FromResult(new WhatsAppSendOutcome(NextOk, NextOk ? null : "test-fail"));
        }

        public Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SendWhatsAppGroupAsync(Guid lineId, string groupJid, string text, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SendWhatsAppDocumentAsync(Guid lineId, string phone, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId, object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null, AuditActorType actorType = AuditActorType.Human) { }
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
        public DbSet<Lead> Leads => inner.Leads;
        public DbSet<LeadActivity> LeadActivities => throw new NotSupportedException();
        public DbSet<LeadNote> LeadNotes => throw new NotSupportedException();
        public DbSet<LeadFile> LeadFiles => throw new NotSupportedException();
        public DbSet<ContactImportBatch> ContactImportBatches => throw new NotSupportedException();
        public DbSet<ContactSearchRun> ContactSearchRuns => throw new NotSupportedException();
        public DbSet<EmailTemplate> EmailTemplates => throw new NotSupportedException();
        public DbSet<FollowUpTask> FollowUpTasks => throw new NotSupportedException();
        public DbSet<Message> Messages => inner.Messages;
        public DbSet<TenantBlockedNumber> TenantBlockedNumbers => inner.TenantBlockedNumbers;
        public DbSet<MessageTemplate> MessageTemplates => throw new NotSupportedException();
        public DbSet<QuoteTemplate> QuoteTemplates => throw new NotSupportedException();
        public DbSet<TemplateAsset> TemplateAssets => throw new NotSupportedException();
        public DbSet<AiAgent> AiAgents => inner.AiAgents;
        public DbSet<AiAgentResource> AiAgentResources => throw new NotSupportedException();
        public DbSet<AiAgentPrompt> AiAgentPrompts => throw new NotSupportedException();
        public DbSet<AiAgentCacheField> AiAgentCacheFields => throw new NotSupportedException();
        public DbSet<AiAgentCacheValue> AiAgentCacheValues => throw new NotSupportedException();
        public DbSet<AiAgentLineBinding> AiAgentLineBindings => inner.AiAgentLineBindings;
        public DbSet<AiAgentRunLog> AiAgentRunLogs => inner.AiAgentRunLogs;
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
        public DbSet<WorkflowStepHistory> WorkflowStepHistories => inner.WorkflowStepHistories;
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

    private static (AgentReactivacionService Svc, InnerDb Inner, FakeConnector Conn, FakeSender Sender) NewService(DateTimeOffset now)
    {
        var inner = new InnerDb(new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var conn = new FakeConnector();
        var sender = new FakeSender();
        var svc = new AgentReactivacionService(new FakeAppDb(inner), conn, sender, new FakeAudit(), new FixedClock(now));
        return (svc, inner, conn, sender);
    }

    private static Guid SeedAgent(InnerDb inner, AgentReactivacionConfig cfg)
    {
        var agent = new AiAgent
        {
            TenantId = Tenant, Name = "Agente", SystemPrompt = "", IsActive = true,
            Provider = AiProvider.Gemini, ReactivacionJson = cfg.Serialize()
        };
        inner.AiAgents.Add(agent);
        return agent.Id;
    }

    private static Guid SeedLine(InnerDb inner, Guid agentId)
    {
        var lineId = Guid.NewGuid();
        inner.AiAgentLineBindings.Add(new AiAgentLineBinding
        {
            TenantId = Tenant, AgentId = agentId, WhatsAppLineId = lineId, IsConnected = true
        });
        return lineId;
    }

    private static Conversation SeedConversation(InnerDb inner, Guid lineId, string phone = "573001112233", Guid? leadId = null)
    {
        var conv = new Conversation
        {
            TenantId = Tenant, ContactPhone = phone, WhatsAppLineId = lineId, LeadId = leadId,
            LastMessageAt = DateTimeOffset.UtcNow
        };
        inner.Conversations.Add(conv);
        return conv;
    }

    private static void SeedInbound(InnerDb inner, Guid convId, DateTimeOffset at)
    {
        inner.Messages.Add(new Message
        {
            TenantId = Tenant, ConversationId = convId, Direction = MessageDirection.Inbound,
            Body = "hola", MessageType = "text", SentAt = at
        });
    }

    private static AgentReactivacionConfig Cfg(params AgentReactivacionPaso[] pasos) => new(true, pasos);

    // ---- Tests ----

    [Fact]
    public async Task Paso_dentro_de_24h_usa_texto_libre()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, sender) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(
            new AgentReactivacionPaso(4, "Sigues ahi?"),
            new AgentReactivacionPaso(24, null, "plantilla24", "es"),
            new AgentReactivacionPaso(72, null, "plantilla72", "es")));
        var lineId = SeedLine(inner, agentId);
        var conv = SeedConversation(inner, lineId);
        SeedInbound(inner, conv.Id, now.AddHours(-5));   // ultimo entrante hace 5h
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(1, sent);
        Assert.Single(conn.TextSends);                       // texto libre (<=24h)
        Assert.Equal("Sigues ahi?", conn.TextSends[0].Text);
        Assert.Empty(sender.TemplateSends);                  // NO plantilla
        Assert.Equal(1, inner.Conversations.Single().ReactivacionUltimoPaso);
    }

    [Fact]
    public async Task Paso_fuera_de_24h_usa_plantilla()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, sender) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(24, "texto", "plantilla24", "es")));
        var lineId = SeedLine(inner, agentId);
        var conv = SeedConversation(inner, lineId);
        SeedInbound(inner, conv.Id, now.AddHours(-30));   // hace 30h -> ventana cerrada
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(1, sent);
        Assert.Single(sender.TemplateSends);                 // PLANTILLA (>24h)
        Assert.Equal("plantilla24", sender.TemplateSends[0].Template);
        Assert.Empty(conn.TextSends);                        // NUNCA texto libre >24h
        Assert.Equal(1, inner.Conversations.Single().ReactivacionUltimoPaso);
    }

    [Fact]
    public async Task Fuera_de_24h_sin_plantilla_se_omite_y_loguea()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, sender) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(24, "solo texto", null, null)));
        var lineId = SeedLine(inner, agentId);
        var conv = SeedConversation(inner, lineId);
        SeedInbound(inner, conv.Id, now.AddHours(-30));   // >24h y sin plantilla
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(0, sent);
        Assert.Empty(conn.TextSends);
        Assert.Empty(sender.TemplateSends);
        Assert.Equal(1, inner.Conversations.Single().ReactivacionUltimoPaso);   // paso omitido = avanzado
        Assert.Contains(inner.AiAgentRunLogs, l => l.Title!.Contains("omitido"));
    }

    [Fact]
    public async Task Cliente_responde_reinicia_la_secuencia()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, _) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(4, "Sigues ahi?")));
        var lineId = SeedLine(inner, agentId);
        var conv = SeedConversation(inner, lineId);
        // Ya se envio el paso 1 hace 2h; luego el cliente respondio hace 1h (DESPUES del seguimiento).
        conv.ReactivacionUltimoPaso = 1;
        conv.ReactivacionUltimoEnvioAt = now.AddHours(-2);
        SeedInbound(inner, conv.Id, now.AddHours(-1));
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(0, sent);                                   // el cliente revivio: no reenvia
        Assert.Empty(conn.TextSends);
        Assert.Equal(0, inner.Conversations.Single().ReactivacionUltimoPaso);   // secuencia reiniciada
    }

    [Fact]
    public async Task Lead_cerrado_no_envia()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, sender) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(4, "Sigues ahi?")));
        var lineId = SeedLine(inner, agentId);
        var lead = new Lead { TenantId = Tenant, ContactName = "Cliente", Status = LeadStatus.Lost };
        inner.Leads.Add(lead);
        var conv = SeedConversation(inner, lineId, leadId: lead.Id);
        SeedInbound(inner, conv.Id, now.AddHours(-5));
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(0, sent);
        Assert.Empty(conn.TextSends);
        Assert.Empty(sender.TemplateSends);
    }

    [Fact]
    public async Task Numero_en_lista_negra_no_recibe_reactivacion()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, _) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(4, "Sigues ahi?")));
        var lineId = SeedLine(inner, agentId);
        var conv = SeedConversation(inner, lineId, phone: "573001112233");
        inner.TenantBlockedNumbers.Add(new TenantBlockedNumber { TenantId = Tenant, Phone = "573001112233" });
        SeedInbound(inner, conv.Id, now.AddHours(-5));
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(0, sent);
        Assert.Empty(conn.TextSends);
    }

    [Fact]
    public async Task Solo_conversaciones_de_las_lineas_del_agente()
    {
        var now = DateTimeOffset.UtcNow;
        var (svc, inner, conn, _) = NewService(now);
        var agentId = SeedAgent(inner, Cfg(new AgentReactivacionPaso(4, "Sigues ahi?")));
        SeedLine(inner, agentId);                            // el agente esta en su propia linea
        var otraLinea = Guid.NewGuid();                      // linea SIN binding a este agente
        var conv = SeedConversation(inner, otraLinea);
        SeedInbound(inner, conv.Id, now.AddHours(-5));
        inner.SaveChanges();

        var sent = await svc.RunTenantAsync();

        Assert.Equal(0, sent);                               // no toca conversaciones de otras lineas
        Assert.Empty(conn.TextSends);
    }
}
