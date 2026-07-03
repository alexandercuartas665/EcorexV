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
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();

    // Motor de flujos BPMN (FASE 4, ADR-0014): definiciones versionadas, grafo materializado
    // (nodos + aristas), instancias por caso y historial de pasos append-only.
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowStepHistory> WorkflowStepHistories => Set<WorkflowStepHistory>();

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
            b.HasIndex(x => new { x.TenantId, x.PlatformUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.HasIndex(x => x.InvitationToken);
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
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
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
            // Consecutivo legible unico por tenant (emitido por TenantSequence).
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            b.HasIndex(x => new { x.TenantId, x.ProjectId });
            b.HasIndex(x => new { x.TenantId, x.AssigneeTenantUserId, x.Status });
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
