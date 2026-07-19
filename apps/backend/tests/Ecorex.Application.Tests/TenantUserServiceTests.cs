using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios de los metodos nuevos del modulo Administracion de usuarios (000073, ADR-0031):
/// ResetPasswordAsync (hashea, valida clave minima, reactiva Invited -> Active) y UpdateProfileAsync
/// (cambia el DisplayName). Se corren sobre un IApplicationDbContext en memoria (EF InMemory) con
/// solo las entidades que el servicio toca; el hasher es un doble simple y verificable. Estos tests
/// NO validan aislamiento multi-tenant (eso vive en la matriz dual de Integration.Tests).
/// </summary>
public class TenantUserServiceTests
{
    private sealed class FakeHasher : Ecorex.Application.Common.Auth.IPasswordHasher
    {
        public string Hash(string password) => "H:" + password;
        public bool Verify(string hash, string password) => hash == "H:" + password;
    }

    private sealed class NullTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
    }

    // DbContext EF InMemory con solo las 3 entidades que el servicio usa; ignora las navegaciones
    // de TenantUser para no arrastrar MenuView/PlatformUser al modelo del contexto de prueba.
    private sealed class InnerDb(DbContextOptions<InnerDb> options) : DbContext(options)
    {
        public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
        public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
        public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => Set<SuperAdminAuditLog>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<TenantUser>().Ignore(t => t.PlatformUser).Ignore(t => t.MenuView);
        }
    }

    // Adaptador minimo de IApplicationDbContext: expone solo los 3 conjuntos usados; el resto lanza.
    private sealed class FakeAppDb(InnerDb inner) : IApplicationDbContext
    {
        public DbSet<PlatformUser> PlatformUsers => inner.PlatformUsers;
        public DbSet<TenantUser> TenantUsers => inner.TenantUsers;
        public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => inner.SuperAdminAuditLogs;
        public DbSet<Rol> Roles => throw new NotSupportedException();
        public DbSet<RolPermiso> RolPermisos => throw new NotSupportedException();
        public DbSet<ScheduledJob> ScheduledJobs => throw new NotSupportedException();
        public DbSet<ScheduledJobRule> ScheduledJobRules => throw new NotSupportedException();
        public DbSet<ScheduledJobChannel> ScheduledJobChannels => throw new NotSupportedException();
        public DbSet<ScheduledJobRun> ScheduledJobRuns => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool HasActiveTransaction => false;

        public DbSet<Tenant> Tenants => throw new NotSupportedException();
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
        public DbSet<TaskWorkLog> TaskWorkLogs => throw new NotSupportedException();
        public DbSet<TaskItemActivity> TaskItemActivities => throw new NotSupportedException();
        public DbSet<Notification> Notifications => throw new NotSupportedException();
        public DbSet<TaskItemAttachment> TaskItemAttachments => throw new NotSupportedException();
        public DbSet<TaskItemChecklistItem> TaskItemChecklistItems => throw new NotSupportedException();
        public DbSet<TaskItemAssignment> TaskItemAssignments => throw new NotSupportedException();
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
        public DbSet<FormFlowLink> FormFlowLinks => throw new NotSupportedException();
        public DbSet<FormToken> FormTokens => throw new NotSupportedException();
        public DbSet<FormRecordLink> FormRecordLinks => throw new NotSupportedException();
        public DbSet<WorkflowNodeForm> WorkflowNodeForms => throw new NotSupportedException();
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
        public DbSet<DataModelRelation> DataModelRelations => throw new NotSupportedException();
        public DbSet<DataModelRelationLink> DataModelRelationLinks => throw new NotSupportedException();
        public DbSet<DataConnector> DataConnectors => throw new NotSupportedException();
        public DbSet<DataClient> DataClients => throw new NotSupportedException();
        public DbSet<ImportProcess> ImportProcesses => throw new NotSupportedException();
        public DbSet<ImportRun> ImportRuns => throw new NotSupportedException();
        public DbSet<WhatsAppTemplate> WhatsAppTemplates => throw new NotSupportedException();
        public DbSet<MenuView> MenuViews => throw new NotSupportedException();
        public DbSet<MenuNode> MenuNodes => throw new NotSupportedException();
        public DbSet<Tercero> Terceros => throw new NotSupportedException();
        public DbSet<TerceroContacto> TerceroContactos => throw new NotSupportedException();
        public DbSet<TerceroFieldDefinition> TerceroFieldDefinitions => throw new NotSupportedException();
        public DbSet<TerceroFormLink> TerceroFormLinks => throw new NotSupportedException();
        public DbSet<TerceroNota> TerceroNotas => throw new NotSupportedException();
        public DbSet<ActividadCategoria> ActividadCategorias => throw new NotSupportedException();
        public DbSet<ActividadSubcategoria> ActividadSubcategorias => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaCargo> ActividadSubcategoriaCargos => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaTercero> ActividadSubcategoriaTerceros => throw new NotSupportedException();
        public DbSet<ActividadSubcategoriaNotificacion> ActividadSubcategoriaNotificaciones => throw new NotSupportedException();
        public DbSet<BolsaColumna> BolsaColumnas => throw new NotSupportedException();
        public DbSet<Oportunidad> Oportunidades => throw new NotSupportedException();
        public DbSet<OportunidadEstado> OportunidadEstados => throw new NotSupportedException();
        public DbSet<Cita> Citas => throw new NotSupportedException();
        public DbSet<TerceroFiltro> TerceroFiltros => throw new NotSupportedException();
        public DbSet<ProspectoScrapeado> ProspectosScrapeados => throw new NotSupportedException();
    }

    private static (TenantUserService svc, InnerDb inner, TenantUser tu, PlatformUser pu) Build(
        PlatformUserStatus status, string? displayName = null)
    {
        var tenantId = Guid.CreateVersion7();
        var options = new DbContextOptionsBuilder<InnerDb>()
            .UseInMemoryDatabase("tuser-" + Guid.NewGuid().ToString("N"))
            .Options;
        var inner = new InnerDb(options);

        var pu = new PlatformUser
        {
            Email = "u@empresa.local",
            DisplayName = displayName,
            Status = status,
            PasswordHash = status == PlatformUserStatus.Invited ? null : "H:vieja1"
        };
        var tu = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = pu.Id,
            Email = pu.Email,
            TenantRole = TenantRole.Advisor,
            Status = status
        };
        inner.PlatformUsers.Add(pu);
        inner.TenantUsers.Add(tu);
        inner.SaveChanges();

        var svc = new TenantUserService(new FakeAppDb(inner), new NullTenantContext(tenantId), new FakeHasher(), new AuditWriter(new FakeAppDb(inner)));
        return (svc, inner, tu, pu);
    }

    [Fact]
    public async Task ResetPassword_HashesNewPassword_AndActivatesInvited()
    {
        var (svc, inner, tu, _) = Build(PlatformUserStatus.Invited);

        var result = await svc.ResetPasswordAsync(tu.Id, "SecretoNuevo", Guid.CreateVersion7());

        Assert.NotNull(result);
        Assert.Equal(PlatformUserStatus.Active, result!.Status);
        var pu = await inner.PlatformUsers.FirstAsync();
        Assert.Equal("H:SecretoNuevo", pu.PasswordHash);
        Assert.Equal(PlatformUserStatus.Active, pu.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    public async Task ResetPassword_RejectsShortOrEmptyPassword(string weak)
    {
        var (svc, _, tu, _) = Build(PlatformUserStatus.Active);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ResetPasswordAsync(tu.Id, weak, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task ResetPassword_ReturnsNull_WhenUserNotFound()
    {
        var (svc, _, _, _) = Build(PlatformUserStatus.Active);
        Assert.Null(await svc.ResetPasswordAsync(Guid.CreateVersion7(), "Valida1", Guid.CreateVersion7()));
    }

    [Fact]
    public async Task UpdateProfile_ChangesDisplayName()
    {
        var (svc, inner, tu, _) = Build(PlatformUserStatus.Active, displayName: "Nombre Viejo");

        var result = await svc.UpdateProfileAsync(tu.Id, "Nombre Nuevo", Guid.CreateVersion7());

        Assert.NotNull(result);
        Assert.Equal("Nombre Nuevo", result!.DisplayName);
        var pu = await inner.PlatformUsers.FirstAsync();
        Assert.Equal("Nombre Nuevo", pu.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_BlankName_ClearsDisplayName()
    {
        var (svc, inner, tu, _) = Build(PlatformUserStatus.Active, displayName: "Con Nombre");

        var result = await svc.UpdateProfileAsync(tu.Id, "   ", Guid.CreateVersion7());

        Assert.NotNull(result);
        Assert.Null(result!.DisplayName);
        var pu = await inner.PlatformUsers.FirstAsync();
        Assert.Null(pu.DisplayName);
    }
}
