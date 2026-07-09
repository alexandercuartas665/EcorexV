using System.Reflection;
using Ecorex.Application.Common;
using Ecorex.Domain.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Persistence;

public class EcorexDbContext : DbContext, IApplicationDbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;

    public EcorexDbContext(DbContextOptions<EcorexDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Constructor para contextos derivados por proveedor (p.ej. SqlServerEcorexDbContext),
    /// que existen unicamente para separar las migraciones por motor (ADR-001).
    /// </summary>
    protected EcorexDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Globales (administradas por Super Admin / plataforma)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SaasPlan> SaasPlans => Set<SaasPlan>();
    public DbSet<SaasPlanLimit> SaasPlanLimits => Set<SaasPlanLimit>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantPayment> TenantPayments => Set<TenantPayment>();
    public DbSet<WompiMasterConfig> WompiMasterConfigs => Set<WompiMasterConfig>();
    public DbSet<WompiWebhookEvent> WompiWebhookEvents => Set<WompiWebhookEvent>();
    public DbSet<EvolutionMasterConfig> EvolutionMasterConfigs => Set<EvolutionMasterConfig>();
    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<PlatformBranding> PlatformBrandings => Set<PlatformBranding>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<GoogleAuthConfig> GoogleAuthConfigs => Set<GoogleAuthConfig>();
    public DbSet<TenantApiConfig> TenantApiConfigs => Set<TenantApiConfig>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AccountActivationCode> AccountActivationCodes => Set<AccountActivationCode>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => Set<SuperAdminAuditLog>();

    // Llaves de Data Protection compartidas entre apps (Api, SuperAdmin, Workers) para
    // que los secretos cifrados (Wompi, Evolution) se descifren en cualquiera de ellas.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Tenant-scoped (con filtro global de consulta)
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<TenantConfiguration> TenantConfigurations => Set<TenantConfiguration>();
    public DbSet<TenantEvolutionConfig> TenantEvolutionConfigs => Set<TenantEvolutionConfig>();
    public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<PipelineFieldDefinition> PipelineFieldDefinitions => Set<PipelineFieldDefinition>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<LeadNote> LeadNotes => Set<LeadNote>();
    public DbSet<LeadFile> LeadFiles => Set<LeadFile>();
    public DbSet<ContactImportBatch> ContactImportBatches => Set<ContactImportBatch>();
    // Extraccion de datos / web scraping acotado (modulo 000730, ADR-0025).
    public DbSet<ScrapeSource> ScrapeSources => Set<ScrapeSource>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<TenantBlockedNumber> TenantBlockedNumbers => Set<TenantBlockedNumber>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<QuoteTemplate> QuoteTemplates => Set<QuoteTemplate>();
    public DbSet<TemplateAsset> TemplateAssets => Set<TemplateAsset>();
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<AiAgentResource> AiAgentResources => Set<AiAgentResource>();
    public DbSet<AiAgentPrompt> AiAgentPrompts => Set<AiAgentPrompt>();
    public DbSet<AiAgentCacheField> AiAgentCacheFields => Set<AiAgentCacheField>();
    public DbSet<AiAgentCacheValue> AiAgentCacheValues => Set<AiAgentCacheValue>();
    public DbSet<AiAgentLineBinding> AiAgentLineBindings => Set<AiAgentLineBinding>();
    public DbSet<AiAgentRunLog> AiAgentRunLogs => Set<AiAgentRunLog>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();

    // Modulo Tableros (Kanban de tareas/proyectos por agencia).
    public DbSet<TaskBoard> TaskBoards => Set<TaskBoard>();
    public DbSet<TaskBoardColumn> TaskBoardColumns => Set<TaskBoardColumn>();
    public DbSet<TaskCard> TaskCards => Set<TaskCard>();
    public DbSet<TaskCardAssignment> TaskCardAssignments => Set<TaskCardAssignment>();
    public DbSet<TaskCardTag> TaskCardTags => Set<TaskCardTag>();
    public DbSet<TaskCardTagAssignment> TaskCardTagAssignments => Set<TaskCardTagAssignment>();
    public DbSet<TaskCardChecklistItem> TaskCardChecklistItems => Set<TaskCardChecklistItem>();
    public DbSet<TaskCardActivity> TaskCardActivities => Set<TaskCardActivity>();
    public DbSet<TaskCardAttachment> TaskCardAttachments => Set<TaskCardAttachment>();

    // Nucleo de tareas/proyectos (FASE 3, ADR-0013): TaskItem de primera clase con
    // consecutivo por tenant, estados propios, proyectos con ACL y worklogs.
    public DbSet<ActivityType> ActivityTypes => Set<ActivityType>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<TaskItemTag> TaskItemTags => Set<TaskItemTag>();
    public DbSet<TaskItemTagAssignment> TaskItemTagAssignments => Set<TaskItemTagAssignment>();
    public DbSet<TaskWorkLog> TaskWorkLogs => Set<TaskWorkLog>();
    public DbSet<TaskItemActivity> TaskItemActivities => Set<TaskItemActivity>();
    public DbSet<TaskItemAttachment> TaskItemAttachments => Set<TaskItemAttachment>();

    // Tableros de actividades unificados (ADR-0020): checklist y asignados M:N del TaskItem.
    public DbSet<TaskItemChecklistItem> TaskItemChecklistItems => Set<TaskItemChecklistItem>();
    public DbSet<TaskItemAssignment> TaskItemAssignments => Set<TaskItemAssignment>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();

    // Motor de flujos BPMN (FASE 4, ADR-0014): definiciones versionadas, grafo materializado
    // (nodos + aristas), instancias por caso y historial de pasos append-only.
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowStepHistory> WorkflowStepHistories => Set<WorkflowStepHistory>();

    // Formularios dinamicos (FASE 4, ADR-0015): definiciones con arbol contenedores ->
    // preguntas, respuestas como documento JSON, tokens de publicacion por URL y vinculos
    // formulario <-> paso de flujo.
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormContainer> FormContainers => Set<FormContainer>();
    public DbSet<FormQuestion> FormQuestions => Set<FormQuestion>();
    public DbSet<FormResponse> FormResponses => Set<FormResponse>();
    public DbSet<FormFlowLink> FormFlowLinks => Set<FormFlowLink>();
    public DbSet<FormToken> FormTokens => Set<FormToken>();
    public DbSet<WorkflowNodeForm> WorkflowNodeForms => Set<WorkflowNodeForm>();

    // Motor de reglas (FASE 4 ola 3, ADR-0016): documentos de reglas, reglas con verbo
    // tipado, historial append-only con TTL y vinculos a preguntas de formulario y nodos
    // de flujo.
    public DbSet<RuleDocument> RuleDocuments => Set<RuleDocument>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<RuleExecutionLog> RuleExecutionLogs => Set<RuleExecutionLog>();
    public DbSet<FormFieldRule> FormFieldRules => Set<FormFieldRule>();
    public DbSet<WorkflowNodeRule> WorkflowNodeRules => Set<WorkflowNodeRule>();

    // Modulos de sistema (FASE 5, ADR-0017): organigrama del tenant (Dependencias, legacy
    // 000850) y registro de modulos (Modulos web, legacy 000109). ModuleDefinition es el
    // catalogo GLOBAL de plataforma (sin TenantId ni query filter); TenantModule es el
    // estado por tenant (scoped).
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<OrgUnitMember> OrgUnitMembers => Set<OrgUnitMember>();
    public DbSet<WorkflowNodePolicy> WorkflowNodePolicies => Set<WorkflowNodePolicy>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();

    // Inventarios (grupo Sistema - Inventarios): catalogos normalizados (bodegas, marcas,
    // grupos, subgrupos, tipos) + items con imagenes por URL y existencias por bodega.
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();
    public DbSet<ItemSubgroup> ItemSubgroups => Set<ItemSubgroup>();
    public DbSet<ItemType> ItemTypes => Set<ItemType>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<ItemStock> ItemStocks => Set<ItemStock>();

    // Plantillas HSM de WhatsApp (ADR-0029): mensajes plantilla con ciclo de aprobacion.
    public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();

    // Menu configurable por perfil (Ola 1): vistas del menu por tenant y sus nodos (arbol).
    public DbSet<MenuView> MenuViews => Set<MenuView>();
    public DbSet<MenuNode> MenuNodes => Set<MenuNode>();

    // Roles de permisos dinamicos (Ola B1, ADR-0032): matriz Modulo x Accion por tenant.
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<RolPermiso> RolPermisos => Set<RolPermiso>();

    // Directorio General (modulo 000232): terceros (empresas / personas) con perfiles de
    // negocio, contactos embebidos y fichas dinamicas (jsonb). Multi-tenant (filtro global).
    public DbSet<Tercero> Terceros => Set<Tercero>();
    public DbSet<TerceroContacto> TerceroContactos => Set<TerceroContacto>();
    public DbSet<TerceroFieldDefinition> TerceroFieldDefinitions => Set<TerceroFieldDefinition>();
    public DbSet<TerceroNota> TerceroNotas => Set<TerceroNota>();

    // Conceptos de actividades (modulo 000270): catalogo de dos niveles Categoria ->
    // Subcategoria (concepto). Multi-tenant (filtro global por reflexion).
    public DbSet<ActividadCategoria> ActividadCategorias => Set<ActividadCategoria>();
    public DbSet<ActividadSubcategoria> ActividadSubcategorias => Set<ActividadSubcategoria>();
    public DbSet<ActividadSubcategoriaCargo> ActividadSubcategoriaCargos => Set<ActividadSubcategoriaCargo>();
    public DbSet<ActividadSubcategoriaTercero> ActividadSubcategoriaTerceros => Set<ActividadSubcategoriaTercero>();

    /// <summary>
    /// Transaccion explicita para casos de uso multi-paso (IApplicationDbContext).
    /// </summary>
    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    /// <summary>Hay una transaccion abierta (los casos de uso anidados se unen a ella).</summary>
    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Todos los enums se persisten como texto (legibles y estables ante reordenamientos).
        configurationBuilder.Properties<TenantStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TenantKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<SubscriptionStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<BillingFrequency>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PlatformRole>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LimitEnforcementMode>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AuditActorType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TenantRole>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PlatformUserStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LeadVisibility>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppLineStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LeadStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FollowUpTaskStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MessageDirection>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MessageMediaType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AiProvider>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AgentResourceType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AutomationTrigger>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AutomationAction>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WompiEnvironment>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WompiIntegrationStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<EvolutionIntegrationStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WebhookProcessingStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PipelineFieldType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<BusinessUnitModalKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TaskActivityType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AiAgentRunLogKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppProvider>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ProjectStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TaskPriority>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TaskItemStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WorkLogKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WorkflowNodeType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WorkflowInstanceStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WorkflowStepStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FormStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FormContainerType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FormControlType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FormResponseStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FormFlowLinkStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<RuleStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<RuleTriggerKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<RuleExecutionStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<OrgUnitKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<OrgUnitClassifier>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ModuleArea>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TaskBoardStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TaskBoardKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ScrapeSourceKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ScrapeSourceStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ScrapeRunStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppTemplateCategory>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppTemplateHeaderType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppTemplateStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MenuNodeKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MenuNodeState>().HaveConversion<string>().HaveMaxLength(40);
        // Directorio General (000232): Tipo/Estado/IdTipo como texto legible. TerceroPerfil NO
        // se convierte a texto: es [Flags] y se guarda como int para filtrar por bits en SQL.
        configurationBuilder.Properties<TerceroTipo>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TerceroEstado>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TerceroIdTipo>().HaveConversion<string>().HaveMaxLength(40);
        // Campos configurables por ficha (000232): el tipo del campo se guarda como texto legible.
        configurationBuilder.Properties<TerceroFieldType>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // DAL dual (ADR-001): el modelo es neutro salvo los puntos marcados con este flag,
        // donde cada proveedor (PostgreSQL / SQL Server) recibe su tipo o sintaxis equivalente.
        var isNpgsql = Database.IsNpgsql();

        ConfigureEntities(modelBuilder, isNpgsql);
        ApplyTenantQueryFilters(modelBuilder);
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder, bool isNpgsql)
    {
        // jsonb existe solo en PostgreSQL; en SQL Server el equivalente practico es nvarchar(max).
        var jsonColumnType = isNpgsql ? "jsonb" : "nvarchar(max)";
        // "text" en SQL Server esta deprecado y no soporta operadores de comparacion (=);
        // el equivalente correcto es nvarchar(max).
        var longTextColumnType = isNpgsql ? "text" : "nvarchar(max)";
        modelBuilder.Entity<Tenant>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(250);
            b.Property(x => x.TaxId).HasMaxLength(80);
            b.Property(x => x.Country).HasMaxLength(80);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.City).HasMaxLength(120);
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.Phone).HasMaxLength(80);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.LogoUrl).HasMaxLength(500);
            b.Property(x => x.PublicBookingToken).HasMaxLength(64);
            b.Property(x => x.PublicBookingBaseUrl).HasMaxLength(300);
            b.HasIndex(x => x.PublicBookingToken).IsUnique()
                .HasFilter(isNpgsql ? "public_booking_token IS NOT NULL" : "[public_booking_token] IS NOT NULL");
        });

        modelBuilder.Entity<SaasPlan>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
            b.Property(x => x.YearlyPrice).HasPrecision(12, 2);
            b.HasMany(x => x.Limits)
                .WithOne(x => x.Plan!)
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SaasPlanLimit>(b =>
        {
            b.Property(x => x.LimitKey).HasMaxLength(150).IsRequired();
            b.Property(x => x.LimitUnit).HasMaxLength(50);
            b.HasIndex(x => new { x.PlanId, x.LimitKey }).IsUnique();
        });

        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.PaymentMethodLabel).HasMaxLength(80);
            b.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<TenantPayment>(b =>
        {
            b.Property(x => x.Amount).HasPrecision(12, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            b.Property(x => x.ProviderReference).HasMaxLength(200);
            b.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<PlatformUser>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.GoogleSubject).HasMaxLength(255);
            b.Property(x => x.AuthProvider).HasMaxLength(50).IsRequired();
            b.HasIndex(x => x.Email).IsUnique();
            b.HasIndex(x => x.GoogleSubject).IsUnique()
                .HasFilter(isNpgsql ? "google_subject IS NOT NULL" : "[google_subject] IS NOT NULL");
        });

        modelBuilder.Entity<SuperAdminAuditLog>(b =>
        {
            b.Property(x => x.ActionName).HasMaxLength(200).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            b.Property(x => x.IpAddress).HasMaxLength(80);
            b.Property(x => x.PreviousValue).HasColumnType(jsonColumnType);
            b.Property(x => x.NewValue).HasColumnType(jsonColumnType);
            b.HasIndex(x => x.TenantId);
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<PlatformBranding>(b =>
        {
            b.Property(x => x.PlatformName).HasMaxLength(120).IsRequired();
            b.Property(x => x.Tagline).HasMaxLength(160);
            b.Property(x => x.LoginLogoUrl).HasMaxLength(500);
            b.Property(x => x.LoginHeadline).HasMaxLength(160);
            b.Property(x => x.LoginSubtext).HasMaxLength(600);
        });

        modelBuilder.Entity<EmailConfig>(b =>
        {
            b.Property(x => x.SmtpHost).HasMaxLength(200);
            b.Property(x => x.SmtpUser).HasMaxLength(200);
            b.Property(x => x.FromEmail).HasMaxLength(200);
            b.Property(x => x.FromName).HasMaxLength(160);
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(80).IsRequired();
            b.HasIndex(x => x.TokenHash);
            b.HasIndex(x => x.PlatformUserId);
        });

        modelBuilder.Entity<AccountActivationCode>(b =>
        {
            b.Property(x => x.CodeHash).HasMaxLength(80).IsRequired();
            b.HasIndex(x => x.PlatformUserId);
        });

        modelBuilder.Entity<GoogleAuthConfig>(b =>
        {
            b.Property(x => x.ClientId).HasMaxLength(300);
        });

        modelBuilder.Entity<TenantApiConfig>(b =>
        {
            b.Property(x => x.ApiKeyHash).HasMaxLength(80);
            b.HasIndex(x => x.ApiKeyHash);
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<WompiMasterConfig>(b =>
        {
            b.Property(x => x.PublicKey).HasMaxLength(200);
            b.Property(x => x.WebhookEndpoint).HasMaxLength(500);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<EvolutionMasterConfig>(b =>
        {
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.Property(x => x.WebhookMode).HasMaxLength(20).HasDefaultValue("Development");
            b.Property(x => x.WebhookPublicUrl).HasMaxLength(500);
            b.Property(x => x.WebhookActiveUrl).HasMaxLength(500);
            b.Property(x => x.WebhookToken).HasMaxLength(200);
        });

        modelBuilder.Entity<AiProviderConfig>(b =>
        {
            b.Property(x => x.Model).HasMaxLength(120);
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.HasIndex(x => x.Provider).IsUnique();
        });

        modelBuilder.Entity<WompiWebhookEvent>(b =>
        {
            b.Property(x => x.ProviderEventId).HasMaxLength(250).IsRequired();
            b.Property(x => x.TransactionId).HasMaxLength(200);
            b.Property(x => x.Reference).HasMaxLength(200);
            b.Property(x => x.Note).HasMaxLength(500);
            b.Property(x => x.RawPayload).HasColumnType(jsonColumnType);
            // Idempotencia: un evento (transaction + timestamp) no se procesa dos veces.
            b.HasIndex(x => x.ProviderEventId).IsUnique();
        });

        modelBuilder.Entity<TenantUser>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.InvitationToken).HasMaxLength(128);
            b.HasOne(x => x.PlatformUser).WithMany().HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
            // Asignacion usuario->vista del menu (Ola 1). NO ACTION: borrar una vista no arrastra
            // al usuario por cascada (la app deja MenuViewId en null antes de borrar la vista).
            b.HasOne(x => x.MenuView).WithMany().HasForeignKey(x => x.MenuViewId).OnDelete(DeleteBehavior.Restrict);
            // Rol de permisos (Ola B1). NO ACTION: borrar un rol no arrastra al usuario por cascada
            // (la app bloquea el borrado de un rol con usuarios asignados). Ver ADR-0032.
            b.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.PlatformUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.HasIndex(x => x.InvitationToken);
            b.HasIndex(x => x.MenuViewId);
            b.HasIndex(x => x.RolId);
        });

        modelBuilder.Entity<TenantConfiguration>(b =>
        {
            b.Property(x => x.ConfigKey).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.ConfigKey }).IsUnique();
        });

        modelBuilder.Entity<TenantEvolutionConfig>(b =>
        {
            // Campos del servidor propio: opcionales (cuando la agencia usa el servidor maestro quedan nulos).
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.Property(x => x.InstanceName).HasMaxLength(200);
            b.Property(x => x.WebhookUrl).HasMaxLength(500);
            // Una configuracion Evolution por tenant.
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<WhatsAppLine>(b =>
        {
            b.Property(x => x.InstanceName).HasMaxLength(200).IsRequired();
            b.Property(x => x.PhoneNumber).HasMaxLength(40);
            b.Property(x => x.CloudPhoneNumberId).HasMaxLength(60);
            b.Property(x => x.CloudBusinessAccountId).HasMaxLength(60);
            b.Property(x => x.CloudAccessTokenEncrypted).HasColumnType(longTextColumnType);
            b.HasIndex(x => new { x.TenantId, x.InstanceName }).IsUnique();
            b.HasIndex(x => x.AssignedToTenantUserId);
            // El webhook entrante de Meta resuelve la linea por phone_number_id.
            b.HasIndex(x => x.CloudPhoneNumberId);
        });

        modelBuilder.Entity<PipelineStage>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<BusinessUnit>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<PipelineFieldDefinition>(b =>
        {
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Label).HasMaxLength(150).IsRequired();
            b.Property(x => x.Options).HasMaxLength(2000);
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.RepeatWithFieldKey).HasMaxLength(80);
            b.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.StageId, x.SortOrder });
            b.HasIndex(x => new { x.StageId, x.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<Lead>(b =>
        {
            b.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
            b.Property(x => x.ContactPhone).HasMaxLength(40);
            b.Property(x => x.Destination).HasMaxLength(200);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.LossReason).HasMaxLength(500);
            b.Property(x => x.EstimatedValue).HasPrecision(14, 2);
            b.Property(x => x.FieldValuesJson).HasColumnType(jsonColumnType);
            b.Property(x => x.ArchiveReason).HasMaxLength(80);
            b.Property(x => x.ArchiveNote).HasMaxLength(1000);
            b.Property(x => x.ArchivedByName).HasMaxLength(200);
            b.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.StageId });
            b.HasIndex(x => x.AssignedToTenantUserId);
            b.HasIndex(x => new { x.TenantId, x.ArchivedAt });
        });

        modelBuilder.Entity<LeadActivity>(b =>
        {
            b.Property(x => x.ActivityType).HasMaxLength(80).IsRequired();
            b.Property(x => x.Description).HasMaxLength(1000);
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<LeadNote>(b =>
        {
            b.Property(x => x.Content).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20).IsRequired();
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<LeadFile>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<ContactImportBatch>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            // El historial de cargas se lista de la mas reciente a la mas vieja, por tenant.
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        // Extraccion de datos / web scraping acotado (modulo 000730, ADR-0025).
        modelBuilder.Entity<ScrapeSource>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            b.Property(x => x.Selector).HasMaxLength(300);
            b.Property(x => x.LastResultSummary).HasMaxLength(400);
            // El nombre identifica la fuente dentro del tenant (el servicio valida el duplicado
            // con mensaje claro; el indice unico es la defensa en profundidad).
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<ScrapeRun>(b =>
        {
            b.Property(x => x.ErrorMessage).HasMaxLength(1000);
            // Resultado estructurado de la corrida: jsonb en PG, nvarchar(max) en SQL Server.
            // Siempre JSON valido y recortado a 64 KB (ScrapeContentParser.BuildResultJson).
            b.Property(x => x.ResultJson).HasColumnType(jsonColumnType);
            b.HasOne(x => x.Source).WithMany(x => x.Runs)
                .HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
            // El historial se lista por fuente, de la corrida mas reciente a la mas vieja.
            b.HasIndex(x => new { x.TenantId, x.SourceId, x.CreatedAt });
        });

        modelBuilder.Entity<FollowUpTask>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.DueAt });
        });

        modelBuilder.Entity<Conversation>(b =>
        {
            b.Property(x => x.ContactPhone).HasMaxLength(40).IsRequired();
            b.Property(x => x.ContactName).HasMaxLength(200);
            // Una conversacion por (tenant, linea, contacto): permite que el mismo numero escriba a
            // dos lineas distintas del tenant como hilos separados (clave de sesion del agente de IA).
            b.HasIndex(x => new { x.TenantId, x.WhatsAppLineId, x.ContactPhone }).IsUnique();
        });

        modelBuilder.Entity<Message>(b =>
        {
            b.Property(x => x.Body).HasMaxLength(4000);
            b.Property(x => x.MessageType).HasMaxLength(40).IsRequired();
            b.Property(x => x.ExternalId).HasMaxLength(200);
            b.Property(x => x.MediaUrl).HasMaxLength(500);
            b.Property(x => x.MediaMimeType).HasMaxLength(120);
            b.Property(x => x.SentByName).HasMaxLength(200);
            b.Property(x => x.Reaction).HasMaxLength(40);
            b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ConversationId });
            // Idempotencia de ingesta: un mensaje externo no se inserta dos veces.
            b.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique()
                .HasFilter(isNpgsql ? "external_id IS NOT NULL" : "[external_id] IS NOT NULL");
        });

        modelBuilder.Entity<TenantBlockedNumber>(b =>
        {
            b.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            b.Property(x => x.Note).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
        });

        modelBuilder.Entity<MessageTemplate>(b =>
        {
            b.Property(x => x.Category).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(120);
            b.Property(x => x.Body).HasMaxLength(4000);
            b.Property(x => x.MediaUrl).HasMaxLength(500);
            b.Property(x => x.MediaMimeType).HasMaxLength(120);
            b.HasIndex(x => new { x.TenantId, x.Category, x.SortOrder });
        });

        modelBuilder.Entity<QuoteTemplate>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.HtmlContent).HasColumnType(longTextColumnType);
            b.HasIndex(x => new { x.TenantId, x.IsDefault });
        });

        modelBuilder.Entity<TemplateAsset>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(120);
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<AiAgent>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Role).HasMaxLength(100);
            b.Property(x => x.Model).HasMaxLength(100);
            b.Property(x => x.SystemPrompt).HasColumnType(longTextColumnType);
            b.Property(x => x.DisabledToolsJson).HasColumnType(jsonColumnType);
            b.Property(x => x.PromptHistoryJson).HasColumnType(longTextColumnType);
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentResource>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Detail).HasColumnType(longTextColumnType);
            b.Property(x => x.FileUrl).HasMaxLength(500);
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentPrompt>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Rule).HasMaxLength(500);
            b.Property(x => x.Body).HasColumnType(longTextColumnType);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentCacheField>(b =>
        {
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Label).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Default true: por defecto el motor puede actualizar el dato si lo necesita.
            b.Property(x => x.IsUpdatable).HasDefaultValue(true);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
            // FieldKey unica por agente: el motor identifica el dato por esta clave.
            b.HasIndex(x => new { x.AgentId, x.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<AiAgentCacheValue>(b =>
        {
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Value).HasMaxLength(2000);
            b.Property(x => x.Source).HasMaxLength(40);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SessionId });
            // Un valor por (sesion, campo): si llega otro dato, se actualiza el registro.
            b.HasIndex(x => new { x.AgentId, x.SessionId, x.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<AiAgentLineBinding>(b =>
        {
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.WhatsAppLine).WithMany().HasForeignKey(x => x.WhatsAppLineId).OnDelete(DeleteBehavior.Cascade);
            // Una linea es atendida por a lo sumo un agente.
            b.HasIndex(x => new { x.TenantId, x.WhatsAppLineId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.AgentId });
        });

        modelBuilder.Entity<AiAgentRunLog>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(300).IsRequired();
            b.Property(x => x.Content).HasColumnType(longTextColumnType);
            b.Property(x => x.Response).HasColumnType(longTextColumnType);
            b.HasIndex(x => new { x.TenantId, x.ConversationId, x.OccurredAt });
        });

        modelBuilder.Entity<AiUsageLog>(b =>
        {
            b.Property(x => x.Model).HasMaxLength(120);
            b.Property(x => x.Source).HasMaxLength(40);
            b.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
            b.HasIndex(x => new { x.TenantId, x.AgentId });
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<AutomationRule>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.FollowUpTitle).HasMaxLength(200);
            b.Property(x => x.TimeWindowStart).HasMaxLength(5);
            b.Property(x => x.TimeWindowEnd).HasMaxLength(5);
            b.Property(x => x.TemplateCategory).HasMaxLength(40);
            b.Property(x => x.ShiftName).HasMaxLength(60);
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        // ---- Modulo Tableros (Kanban) ----

        modelBuilder.Entity<TaskBoard>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.Color).HasMaxLength(20);
            // ADR-0020: codigo legible de los tableros de actividades (PRY-0042); los CRM
            // heredados no tienen (null). Unico por tenant cuando existe (indice filtrado).
            b.Property(x => x.Code).HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique()
                .HasFilter(isNpgsql ? "code IS NOT NULL" : "[code] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
            b.HasIndex(x => new { x.TenantId, x.Kind, x.IsArchived });
        });

        modelBuilder.Entity<TaskBoardColumn>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.BoardId, x.SortOrder });
        });

        modelBuilder.Entity<TaskCard>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnType(longTextColumnType);
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Column).WithMany().HasForeignKey(x => x.ColumnId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.BoardId, x.ColumnId, x.SortOrder });
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
        });

        modelBuilder.Entity<TaskCardAssignment>(b =>
        {
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.TenantUser).WithMany().HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
            // Un usuario no se asigna dos veces a la misma tarjeta.
            b.HasIndex(x => new { x.TaskCardId, x.TenantUserId }).IsUnique();
        });

        modelBuilder.Entity<TaskCardTag>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(80).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            // El nombre de etiqueta es unico por tablero.
            b.HasIndex(x => new { x.BoardId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<TaskCardTagAssignment>(b =>
        {
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            // SQL Server no admite dos rutas de cascada hacia esta tabla (error 1785:
            // board->cards->tag_assignments y board->tags->tag_assignments). En ese motor la FK
            // hacia la etiqueta queda NO ACTION en BD (ClientCascade) y la limpieza explicita la
            // hacen TaskBoardService.DeleteBoardTagAsync/DeleteBoardAsync (neutra entre motores).
            b.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId)
                .OnDelete(isNpgsql ? DeleteBehavior.Cascade : DeleteBehavior.ClientCascade);
            b.HasIndex(x => new { x.TaskCardId, x.TagId }).IsUnique();
        });

        modelBuilder.Entity<TaskCardChecklistItem>(b =>
        {
            b.Property(x => x.Text).HasMaxLength(500).IsRequired();
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.SortOrder });
        });

        modelBuilder.Entity<TaskCardActivity>(b =>
        {
            b.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Text).HasColumnType(longTextColumnType).IsRequired();
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.CreatedAt });
        });

        modelBuilder.Entity<TaskCardAttachment>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(120);
            b.Property(x => x.UploadedByName).HasMaxLength(200);
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.CreatedAt });
        });

        // ---- Nucleo de tareas/proyectos (FASE 3, ADR-0013) ----

        modelBuilder.Entity<ActivityType>(b =>
        {
            b.Property(x => x.Category).HasMaxLength(100).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.Category, x.Name }).IsUnique();
            // FASE 4 (ADR-0014): FK real hacia la definicion de flujo, NO ACTION (borrar o
            // archivar definiciones nunca toca el catalogo de tipos).
            b.HasOne(x => x.WorkflowDefinition).WithMany()
                .HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            // Concurrencia optimista portable (ADR-0013): Version es ConcurrencyToken en
            // ambos motores; la incrementa el AuditableTenantInterceptor en cada UPDATE.
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne(x => x.OwnerTenantUser).WithMany().HasForeignKey(x => x.OwnerTenantUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
        });

        modelBuilder.Entity<ProjectMember>(b =>
        {
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            // La FK del owner del proyecto es Restrict, asi que esta cascada no crea doble
            // ruta tenant_users->project_members en SQL Server.
            b.HasOne(x => x.TenantUser).WithMany().HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.ProjectId, x.TenantUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TenantUserId });
        });

        modelBuilder.Entity<TaskItem>(b =>
        {
            b.Property(x => x.Number).HasMaxLength(20).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnType(longTextColumnType);
            b.Property(x => x.RequesterName).HasMaxLength(200);
            b.Property(x => x.RequesterEmail).HasMaxLength(256);
            b.Property(x => x.RequesterPhone).HasMaxLength(40);
            b.Property(x => x.CcEmails).HasColumnType(jsonColumnType);
            b.Property(x => x.Color).HasMaxLength(20);
            // Concurrencia optimista portable (ADR-0013), igual que Project.
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne(x => x.ActivityType).WithMany().HasForeignKey(x => x.ActivityTypeId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AssigneeTenantUser).WithMany().HasForeignKey(x => x.AssigneeTenantUserId).OnDelete(DeleteBehavior.Restrict);
            // FASE 4 (ADR-0014): la instancia de flujo que gobierna la tarea; sin cascada
            // (referencia circular controlada con workflow_instances.task_item_id).
            b.HasOne(x => x.WorkflowInstance).WithMany()
                .HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Restrict);
            // ADR-0020: la tarea como tarjeta de un tablero de actividades. FKs SIN cascada
            // (Restrict = NO ACTION en ambos motores): borrar tablero/columna exige desacoplar
            // o mover las tarjetas antes (ActivityBoardService lo hace explicito).
            b.HasOne(x => x.Board).WithMany()
                .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Column).WithMany()
                .HasForeignKey(x => x.ColumnId).OnDelete(DeleteBehavior.Restrict);
            // Consecutivo legible unico por tenant (emitido por TenantSequence).
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            b.HasIndex(x => new { x.TenantId, x.ProjectId });
            b.HasIndex(x => new { x.TenantId, x.AssigneeTenantUserId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.BoardId, x.ColumnId, x.BoardSortOrder });
        });

        modelBuilder.Entity<TaskItemChecklistItem>(b =>
        {
            b.Property(x => x.Text).HasMaxLength(500).IsRequired();
            b.HasOne(x => x.TaskItem).WithMany()
                .HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            // CompletedByTenantUserId es informativo (sin FK dura): la traza de quien completo
            // no debe bloquear el borrado del usuario del tenant.
            b.HasIndex(x => new { x.TaskItemId, x.SortOrder });
        });

        modelBuilder.Entity<TaskItemAssignment>(b =>
        {
            b.HasOne(x => x.TaskItem).WithMany()
                .HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            // Ambas FKs pueden ser Cascade en los dos motores: tenant_users NO cascadea hacia
            // task_items (la FK del encargado es Restrict), asi que no hay doble ruta en
            // SQL Server (mismo razonamiento que TaskItemTagAssignment).
            b.HasOne(x => x.TenantUser).WithMany()
                .HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
            // Un usuario no se asigna dos veces a la misma tarea.
            b.HasIndex(x => new { x.TaskItemId, x.TenantUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TenantUserId });
        });

        modelBuilder.Entity<TaskItemTag>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(80).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            // Catalogo por TENANT (no por tablero): nombre unico por tenant.
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<TaskItemTagAssignment>(b =>
        {
            b.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            // A diferencia de TaskCardTagAssignment, aqui no hay doble ruta de cascada en
            // SQL Server (tags y task_items no comparten un ancestro con cascade), por lo
            // que ambas FKs pueden ser Cascade en los dos motores.
            b.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskItemId, x.TagId }).IsUnique();
        });

        modelBuilder.Entity<TaskWorkLog>(b =>
        {
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.TenantUser).WithMany().HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TaskItemId, x.LoggedAt });
        });

        modelBuilder.Entity<TaskItemActivity>(b =>
        {
            b.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Text).HasColumnType(longTextColumnType).IsRequired();
            b.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskItemId, x.CreatedAt });
        });

        modelBuilder.Entity<TaskItemAttachment>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(120);
            b.Property(x => x.UploadedByName).HasMaxLength(200);
            b.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskItemId, x.CreatedAt });
        });

        modelBuilder.Entity<TenantSequence>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(10).IsRequired();
            // Un consecutivo por (tenant, codigo). SequenceService lo incrementa con
            // UPDATE condicional atomico (CAS con retry), sin SQL crudo (ADR-0013).
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        // ---- Motor de flujos BPMN (FASE 4, ADR-0014) ----

        modelBuilder.Entity<WorkflowDefinition>(b =>
        {
            b.Property(x => x.ProcessCode).HasMaxLength(25).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // El XML BPMN original, sin modificar (round-trip con bpmn.io).
            b.Property(x => x.BpmnXml).HasColumnType(longTextColumnType).IsRequired();
            b.Property(x => x.Version).HasDefaultValue(1);
            // Editor de flujos del prototipo (ADR-0022): categoria del indice y pausa.
            b.Property(x => x.Category).HasMaxLength(100);
            b.Property(x => x.IsPaused).HasDefaultValue(false);
            // Versionado inmutable: una fila por version del proceso.
            b.HasIndex(x => new { x.TenantId, x.ProcessCode, x.Version }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsPublished });
        });

        modelBuilder.Entity<WorkflowNode>(b =>
        {
            b.Property(x => x.BpmnElementId).HasMaxLength(100).IsRequired();
            b.Property(x => x.Name).HasMaxLength(300);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            // Self-FK del reinicio (ID_REINICIO legacy): NO ACTION siempre (nunca cascada).
            b.HasOne(x => x.RestartNode).WithMany()
                .HasForeignKey(x => x.RestartNodeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.DefinitionId, x.BpmnElementId }).IsUnique();
            // Layout del canvas (ADR-0022): coordenadas del bpmndi, con default 0
            // para filas preexistentes (el seeder les aplica auto-layout).
            b.Property(x => x.X).HasDefaultValue(0);
            b.Property(x => x.Y).HasDefaultValue(0);
        });

        modelBuilder.Entity<WorkflowEdge>(b =>
        {
            b.Property(x => x.BpmnElementId).HasMaxLength(100);
            b.Property(x => x.Name).HasMaxLength(300);
            b.Property(x => x.ConditionExpression).HasMaxLength(400);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            // SQL Server no admite dos rutas de cascada hacia esta tabla (error 1785:
            // definition->edges y definition->nodes->edges). Igual que TaskCardTagAssignment,
            // en ese motor las FKs hacia los nodos quedan NO ACTION en BD (ClientCascade);
            // los nodos y aristas de una definicion viven y mueren juntos via la FK de la
            // definicion, asi que no queda basura.
            b.HasOne(x => x.SourceNode).WithMany().HasForeignKey(x => x.SourceNodeId)
                .OnDelete(isNpgsql ? DeleteBehavior.Cascade : DeleteBehavior.ClientCascade);
            b.HasOne(x => x.TargetNode).WithMany().HasForeignKey(x => x.TargetNodeId)
                .OnDelete(isNpgsql ? DeleteBehavior.Cascade : DeleteBehavior.ClientCascade);
            b.HasIndex(x => new { x.DefinitionId, x.SourceNodeId });
        });

        modelBuilder.Entity<WorkflowInstance>(b =>
        {
            // Concurrencia optimista portable (ADR-0013), igual que TaskItem.
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
            // Vinculo 1:1 opcional con la tarea del nucleo; sin cascada en ninguna direccion.
            b.HasOne(x => x.TaskItem).WithMany()
                .HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TaskItemId).IsUnique()
                .HasFilter(isNpgsql ? "task_item_id IS NOT NULL" : "[task_item_id] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<WorkflowStepHistory>(b =>
        {
            b.Property(x => x.ApprovalResult).HasMaxLength(20);
            b.Property(x => x.ApprovalComment).HasMaxLength(2000);
            b.HasOne(x => x.Instance).WithMany()
                .HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION hacia el nodo: el historial es append-only y sobrevive a la
            // definicion (que de todos modos solo se archiva, nunca se borra).
            b.HasOne(x => x.Node).WithMany()
                .HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.InstanceId, x.IsCurrent });
            b.HasIndex(x => new { x.InstanceId, x.NodeId, x.CycleIndex });
        });

        // ---- Formularios dinamicos (FASE 4, ADR-0015) ----

        modelBuilder.Entity<FormDefinition>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Version de NEGOCIO (Revision, int) separada del token de concurrencia
            // (Version, long): comparten proposito distinto y por eso nombres distintos.
            b.Property(x => x.Revision).HasDefaultValue(1);
            // Concurrencia optimista portable (ADR-0013), igual que TaskItem.
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
        });

        modelBuilder.Entity<FormContainer>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Style).HasMaxLength(300);
            // Constructor del prototipo (ADR-0021).
            b.Property(x => x.TabsJson).HasColumnType(jsonColumnType);
            b.Property(x => x.Width).HasDefaultValue(12);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            // Self-FK del arbol: NO ACTION siempre (el servicio borra el subarbol explicitamente).
            b.HasOne(x => x.Parent).WithMany()
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.DefinitionId, x.SortOrder });
            b.HasIndex(x => x.ParentId);
        });

        modelBuilder.Entity<FormQuestion>(b =>
        {
            b.Property(x => x.FieldCode).HasMaxLength(60).IsRequired();
            b.Property(x => x.Label).HasMaxLength(500).IsRequired();
            b.Property(x => x.Caption).HasMaxLength(300);
            b.Property(x => x.HelpText).HasMaxLength(600);
            b.Property(x => x.OptionsJson).HasColumnType(jsonColumnType);
            b.Property(x => x.GridCol).HasMaxLength(20).IsRequired();
            b.Property(x => x.Numeral).HasMaxLength(20);
            b.Property(x => x.ValidationJson).HasColumnType(jsonColumnType);
            // Constructor del prototipo (ADR-0021): Width es la fuente del layout;
            // GridCol queda sincronizado por el servicio (renderer bootstrap + E2E).
            b.Property(x => x.Width).HasDefaultValue(12);
            b.Property(x => x.PlaceholderText).HasMaxLength(200);
            b.Property(x => x.DefaultValue).HasMaxLength(2000);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION hacia el contenedor: evita la doble ruta de cascada en SQL Server
            // (definition->questions y definition->containers->questions); el servicio decide
            // que pasa con las preguntas al borrar un contenedor (las mueve a la raiz).
            b.HasOne(x => x.Container).WithMany()
                .HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict);
            // FieldCode es la clave del documento JSON de respuestas: unico por definicion.
            b.HasIndex(x => new { x.DefinitionId, x.FieldCode }).IsUnique();
            b.HasIndex(x => new { x.DefinitionId, x.ContainerId, x.SortOrder });
        });

        modelBuilder.Entity<FormResponse>(b =>
        {
            b.Property(x => x.Reference).HasMaxLength(100);
            // Documento JSON { fieldCode: { value, type } }: jsonb en PG, nvarchar(max) en
            // SQL Server (mismo patron dual de TaskItem.CcEmails).
            b.Property(x => x.Data).HasColumnType(jsonColumnType).IsRequired();
            // Concurrencia optimista portable (ADR-0013): autosave + submit concurrentes.
            b.Property(x => x.Version).IsConcurrencyToken();
            // Sin cascada: las respuestas sobreviven (la definicion se archiva, no se borra).
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.DefinitionId, x.Reference });
        });

        modelBuilder.Entity<FormFlowLink>(b =>
        {
            b.HasOne(x => x.FormResponse).WithMany()
                .HasForeignKey(x => x.FormResponseId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.WorkflowInstance).WithMany()
                .HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION hacia el nodo (igual que WorkflowStepHistory): el vinculo es parte
            // de la historia del caso y el nodo nunca se borra por cascada desde aqui.
            b.HasOne(x => x.WorkflowNode).WithMany()
                .HasForeignKey(x => x.WorkflowNodeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.WorkflowInstanceId, x.WorkflowNodeId, x.FormResponseId }).IsUnique();
            b.HasIndex(x => new { x.FormResponseId, x.Status });
        });

        modelBuilder.Entity<FormToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.Reference).HasMaxLength(100);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            // Unico por tenant (spec); el hash aleatorio de 256 bits no colisiona entre
            // tenants en la practica y la resolucion anonima usa el indice global no-unico.
            b.HasIndex(x => new { x.TenantId, x.TokenHash }).IsUnique();
            // Busqueda del visor anonimo (IgnoreQueryFilters, ver FormTokenService).
            b.HasIndex(x => x.TokenHash);
        });

        modelBuilder.Entity<WorkflowNodeForm>(b =>
        {
            // Un nodo tiene a lo sumo UN formulario asignado.
            b.HasIndex(x => x.NodeId).IsUnique();
            b.HasIndex(x => x.DefinitionId);
            // El vinculo vive y muere con el nodo (definicion de flujo); la definicion de
            // formulario NO se borra mientras este asignada (restrict).
            b.HasOne(x => x.Node).WithMany()
                .HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Definition).WithMany()
                .HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- Motor de reglas (FASE 4 ola 3, ADR-0016) ----

        modelBuilder.Entity<RuleDocument>(b =>
        {
            b.Property(x => x.DocumentCode).HasMaxLength(25).IsRequired();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Category).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.DocumentCode }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
        });

        modelBuilder.Entity<Rule>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Clave del registro TIPADO de verbos en DI (nunca reflexion, ADR-0016).
            b.Property(x => x.VerbName).HasMaxLength(100).IsRequired();
            b.Property(x => x.ParamsJson).HasColumnType(jsonColumnType);
            b.HasOne(x => x.Document).WithMany()
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DocumentId, x.SortOrder });
        });

        modelBuilder.Entity<RuleExecutionLog>(b =>
        {
            b.Property(x => x.RuleNameSnapshot).HasMaxLength(100).IsRequired();
            b.Property(x => x.ContextJson).HasColumnType(jsonColumnType);
            b.Property(x => x.ErrorMessage).HasMaxLength(2000);
            // Historial append-only: NO ACTION hacia la regla (el historial sobrevive; una
            // regla con ejecuciones no se borra fisicamente, se inactiva o vence el TTL).
            b.HasOne(x => x.Rule).WithMany()
                .HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.RuleId, x.CreatedAt });
            // El worker de limpieza TTL barre por vencimiento (unico DELETE fisico permitido).
            b.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<FormFieldRule>(b =>
        {
            // El vinculo vive y muere con la pregunta; NO ACTION hacia la regla.
            b.HasOne(x => x.FormQuestion).WithMany()
                .HasForeignKey(x => x.FormQuestionId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Rule).WithMany()
                .HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.FormQuestionId, x.RuleId }).IsUnique();
            b.HasIndex(x => x.RuleId);
        });

        modelBuilder.Entity<WorkflowNodeRule>(b =>
        {
            // El vinculo vive y muere con el nodo (definicion de flujo); NO ACTION a la regla.
            b.HasOne(x => x.WorkflowNode).WithMany()
                .HasForeignKey(x => x.WorkflowNodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Rule).WithMany()
                .HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.WorkflowNodeId, x.RuleId }).IsUnique();
            b.HasIndex(x => x.RuleId);
        });

        // ---- Modulos de sistema (FASE 5, ADR-0017) ----

        modelBuilder.Entity<OrgUnit>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Self-FK del arbol con NO ACTION: una unidad con hijos no se borra en cascada
            // (y las unidades nunca se borran fisicamente: se archivan).
            b.HasOne(x => x.Parent).WithMany()
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            // Clasificador de asignacion por nodo (ADR-0035): default Dependencia para las
            // filas heredadas; TenantUserId solo se usa cuando Classifier=Funcionario.
            b.Property(x => x.Classifier).HasDefaultValue(OrgUnitClassifier.Dependencia);
            b.HasIndex(x => new { x.TenantId, x.ParentId });
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
            b.HasIndex(x => x.ResponsibleTenantUserId);
            b.HasIndex(x => x.TenantUserId);
        });

        modelBuilder.Entity<OrgUnitMember>(b =>
        {
            b.Property(x => x.Role).HasMaxLength(100);
            // El miembro vive y muere con su unidad.
            b.HasOne(x => x.OrgUnit).WithMany()
                .HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.OrgUnitId, x.TenantUserId }).IsUnique();
            b.HasIndex(x => x.TenantUserId);
        });

        // Asignacion por nodo (ADR-0035, ola F1): que Dependencia/Cargo atiende un paso Task.
        modelBuilder.Entity<WorkflowNodePolicy>(b =>
        {
            // El vinculo vive y muere con el nodo (definicion de flujo); NO ACTION a la unidad
            // (una unidad del organigrama nunca se borra fisicamente: se archiva).
            b.HasOne(x => x.WorkflowNode).WithMany()
                .HasForeignKey(x => x.WorkflowNodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.OrgUnit).WithMany()
                .HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.WorkflowNodeId, x.OrgUnitId }).IsUnique();
            b.HasIndex(x => x.OrgUnitId);
        });

        modelBuilder.Entity<ModuleDefinition>(b =>
        {
            // Catalogo GLOBAL de plataforma (ADR-0017): sin TenantId y sin query filter.
            b.Property(x => x.LegacyCode).HasMaxLength(6).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.Route).HasMaxLength(200);
            b.HasIndex(x => x.LegacyCode).IsUnique();
        });

        modelBuilder.Entity<TenantModule>(b =>
        {
            // Settings del tenant como documento JSON: jsonb en PG, nvarchar(max) en SQL Server.
            b.Property(x => x.SettingsJson).HasColumnType(jsonColumnType);
            // Restrict: el catalogo global no arrastra estados de tenants al borrarse
            // (una definicion con estados por tenant no se elimina).
            b.HasOne(x => x.ModuleDefinition).WithMany()
                .HasForeignKey(x => x.ModuleDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.ModuleDefinitionId }).IsUnique();
        });

        // ---- Directorio General (modulo 000232) ----
        // Terceros (empresas / personas) con perfiles de negocio ([Flags] como int), contactos
        // embebidos y fichas dinamicas por perfil (jsonb en PG / nvarchar(max) en SQL Server).

        modelBuilder.Entity<Tercero>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Vendedor).HasMaxLength(150);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            b.Property(x => x.IdValor).HasMaxLength(100);
            b.Property(x => x.Sector).HasMaxLength(150);
            b.Property(x => x.Cargo).HasMaxLength(150);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.Telefono).HasMaxLength(80);
            // Fichas dinamicas por perfil: documento JSON (jsonb en PG, nvarchar(max) en SQL Server).
            b.Property(x => x.FichasJson).HasColumnType(jsonColumnType);
            // Self-FK opcional persona -> empresa. NO ACTION (Restrict): una empresa con personas
            // asignadas no se borra en cascada, y se evitan rutas multiples de cascada (SQL Server).
            b.HasOne(x => x.Empresa).WithMany()
                .HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Contactos).WithOne(x => x.Tercero!)
                .HasForeignKey(x => x.TerceroId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Tipo });
            b.HasIndex(x => new { x.TenantId, x.Estado });
            b.HasIndex(x => new { x.TenantId, x.EmpresaId });
            b.HasIndex(x => x.Nombre);
        });

        modelBuilder.Entity<TerceroContacto>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Cargo).HasMaxLength(150);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.Telefono).HasMaxLength(80);
            // El contacto vive y muere con su empresa (la relacion se declara desde Tercero arriba).
            b.HasIndex(x => x.TerceroId);
        });

        // Campos configurables por ficha (fiscal/comercial/cliente/proveedor/empleado): datos que
        // vuelven las fichas del tercero personalizables por tenant. FieldType como texto.
        modelBuilder.Entity<TerceroFieldDefinition>(b =>
        {
            b.Property(x => x.FichaKey).HasMaxLength(40).IsRequired();
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Label).HasMaxLength(150).IsRequired();
            b.Property(x => x.Options).HasMaxLength(2000);
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.FichaKey, x.SortOrder });
            b.HasIndex(x => new { x.TenantId, x.FichaKey, x.FieldKey }).IsUnique();
        });

        // Notas / gestiones "Contacto cliente" del tercero. Viven y mueren con el tercero (cascade).
        modelBuilder.Entity<TerceroNota>(b =>
        {
            b.Property(x => x.Texto).HasMaxLength(4000).IsRequired();
            b.Property(x => x.Accion).HasMaxLength(40).IsRequired();
            b.Property(x => x.Categoria).HasMaxLength(120);
            b.Property(x => x.Subcategoria).HasMaxLength(120);
            b.Property(x => x.Autor).HasMaxLength(150);
            b.HasOne(x => x.Tercero).WithMany()
                .HasForeignKey(x => x.TerceroId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.TerceroId });
        });

        // ---- Conceptos de actividades (modulo 000270) ----
        // Jerarquia de dos niveles Categoria -> Subcategoria (concepto). Los vinculos a otros
        // modulos (flujo/formulario/tablero/columna) y a cargos/terceros son NO ACTION (Restrict)
        // para evitar rutas multiples de cascada en SQL Server; las subcategorias y sus relaciones
        // hijas mueren con la categoria (Cascade). Codigos unicos por (TenantId, Codigo).

        modelBuilder.Entity<ActividadCategoria>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            b.Property(x => x.Descripcion).HasMaxLength(600);
            b.HasMany(x => x.Subcategorias).WithOne(x => x.Categoria!)
                .HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsArchived, x.SortOrder });
        });

        modelBuilder.Entity<ActividadSubcategoria>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(60).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Chequeo).HasMaxLength(2000);
            b.Property(x => x.Descripcion).HasMaxLength(2000);
            b.Property(x => x.TituloAuto).HasMaxLength(300);
            b.Property(x => x.DetalleAuto).HasMaxLength(2000);
            // Vinculos opcionales a otros modulos: NO ACTION (borrar el destino no toca el catalogo).
            b.HasOne(x => x.WorkflowDefinition).WithMany()
                .HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.FormDefinition).WithMany()
                .HasForeignKey(x => x.FormDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.TaskBoard).WithMany()
                .HasForeignKey(x => x.TaskBoardId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.TaskBoardColumn).WithMany()
                .HasForeignKey(x => x.TaskBoardColumnId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CategoriaId, x.SortOrder });
        });

        modelBuilder.Entity<ActividadSubcategoriaCargo>(b =>
        {
            // Vive y muere con la subcategoria (Cascade). La FK al cargo es NO ACTION.
            b.HasOne(x => x.Subcategoria).WithMany(x => x.Cargos)
                .HasForeignKey(x => x.SubcategoriaId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.OrgUnit).WithMany()
                .HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.SubcategoriaId, x.OrgUnitId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.OrgUnitId });
        });

        modelBuilder.Entity<ActividadSubcategoriaTercero>(b =>
        {
            b.HasOne(x => x.Subcategoria).WithMany(x => x.Terceros)
                .HasForeignKey(x => x.SubcategoriaId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Tercero).WithMany()
                .HasForeignKey(x => x.TerceroId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.SubcategoriaId, x.TerceroId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TerceroId });
        });

        // ---- Inventarios (grupo Sistema - Inventarios) ----
        // Catalogos normalizados: nombre unico por tenant; FKs de catalogo NO ACTION (Restrict)
        // para evitar rutas multiples de cascada en SQL Server. Los items no se borran
        // fisicamente (IsActive); las imagenes y el stock viven y mueren con el item (cascade).

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.City).HasMaxLength(120).IsRequired();
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.Phone).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<Brand>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<ItemGroup>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<ItemSubgroup>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // NO ACTION: un grupo con subgrupos no se borra por cascada (se archiva).
            b.HasOne(x => x.Group).WithMany()
                .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.GroupId });
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<ItemType>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<Item>(b =>
        {
            b.Property(x => x.Sku).HasMaxLength(80);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.Specifications).HasColumnType(longTextColumnType);
            b.Property(x => x.Price).HasPrecision(14, 2);
            b.Property(x => x.FieldValuesJson).HasColumnType(jsonColumnType);
            // Catalogos normalizados: todas las FKs NO ACTION (Restrict = NO ACTION en ambos
            // motores). Borrar/archivar un catalogo nunca arrastra items por cascada.
            b.HasOne(x => x.Brand).WithMany()
                .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Group).WithMany()
                .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Subgroup).WithMany()
                .HasForeignKey(x => x.SubgroupId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ItemType).WithMany()
                .HasForeignKey(x => x.ItemTypeId).OnDelete(DeleteBehavior.Restrict);
            // SKU unico por tenant cuando no esta vacio (indice filtrado; el servicio valida el
            // duplicado con mensaje claro, el indice es la defensa en profundidad).
            b.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique()
                .HasFilter(isNpgsql ? "sku IS NOT NULL" : "[sku] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.IsActive });
            b.HasIndex(x => new { x.TenantId, x.BrandId });
            b.HasIndex(x => new { x.TenantId, x.GroupId });
            b.HasIndex(x => new { x.TenantId, x.ItemTypeId });
        });

        modelBuilder.Entity<ItemImage>(b =>
        {
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.FileName).HasMaxLength(255);
            // La imagen vive y muere con el item.
            b.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ItemId, x.SortOrder });
        });

        modelBuilder.Entity<ItemStock>(b =>
        {
            // Cascade hacia el item; NO ACTION hacia la bodega (una bodega con existencias no
            // se borra por cascada). En SQL Server la doble ruta item->stock y warehouse->stock
            // no aplica porque la FK de bodega es Restrict (una sola ruta de cascada).
            b.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Warehouse).WithMany()
                .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            // Una fila de stock por (item, bodega).
            b.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
            // Listado de stock por bodega (filtro de disponibles).
            b.HasIndex(x => new { x.TenantId, x.WarehouseId });
        });

        modelBuilder.Entity<WhatsAppTemplate>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(512).IsRequired();
            b.Property(x => x.Language).HasMaxLength(12).IsRequired();
            b.Property(x => x.HeaderText).HasMaxLength(60);
            b.Property(x => x.BodyText).HasColumnType(longTextColumnType).IsRequired();
            b.Property(x => x.FooterText).HasMaxLength(60);
            // Variables de ejemplo: jsonb en PostgreSQL, nvarchar(max) en SQL Server (DAL dual).
            b.Property(x => x.VariablesJson).HasColumnType(jsonColumnType).IsRequired();
            b.Property(x => x.WabaId).HasMaxLength(120);
            b.Property(x => x.ProviderTemplateId).HasMaxLength(200);
            b.Property(x => x.RejectionReason).HasMaxLength(1000);
            // NO ACTION hacia la linea: borrar/archivar una linea no arrastra plantillas por
            // cascada (Restrict = NO ACTION en ambos motores; evita rutas multiples en SQL Server).
            b.HasOne(x => x.WhatsAppLine).WithMany()
                .HasForeignKey(x => x.WhatsAppLineId).OnDelete(DeleteBehavior.Restrict);
            // Unica por tenant en (Name, Language): la misma plantilla en otro idioma coexiste.
            b.HasIndex(x => new { x.TenantId, x.Name, x.Language }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive });
            b.HasIndex(x => new { x.TenantId, x.WhatsAppLineId });
        });

        // Menu configurable por perfil (Ola 1): vistas del menu por tenant + arbol de nodos.
        modelBuilder.Entity<MenuView>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Nombre unico por tenant.
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsDefault });
        });

        modelBuilder.Entity<MenuNode>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.IconKey).HasMaxLength(60);
            b.Property(x => x.LegacyCode).HasMaxLength(20);
            b.Property(x => x.Route).HasMaxLength(300);
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.HelpText).HasColumnType(longTextColumnType);
            // La vista y sus nodos viven y mueren juntos.
            b.HasOne(x => x.MenuView).WithMany()
                .HasForeignKey(x => x.MenuViewId).OnDelete(DeleteBehavior.Cascade);
            // Self-ref NO ACTION: evita rutas de cascada multiples en SQL Server (borrado de la
            // rama se maneja por la app, no por cascada del padre).
            b.HasOne(x => x.Parent).WithMany()
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.MenuViewId });
            b.HasIndex(x => new { x.MenuViewId, x.ParentId, x.SortOrder });
        });

        // Roles de permisos dinamicos (Ola B1, ADR-0032): rol por tenant + filas de permiso por modulo.
        modelBuilder.Entity<Rol>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            // Nombre unico por tenant.
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive });
        });

        modelBuilder.Entity<RolPermiso>(b =>
        {
            b.Property(x => x.ModuleKey).HasMaxLength(300).IsRequired();
            // El permiso vive y muere con su rol.
            b.HasOne(x => x.Rol).WithMany()
                .HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Cascade);
            // Una fila por (rol, modulo).
            b.HasIndex(x => new { x.RolId, x.ModuleKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.RolId });
        });
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var applyMethod = typeof(EcorexDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                applyMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        // Fail-closed: si no hay tenant activo, TenantId del contexto es null y no devuelve filas.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }
}
