using System.Reflection;
using CubotNails.Application.Common;
using CubotNails.Domain.Common;
using CubotNails.Domain.Entities;
using CubotNails.Domain.Enums;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Infrastructure.Persistence;

public class CubotNailsDbContext : DbContext, IApplicationDbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;

    public CubotNailsDbContext(DbContextOptions<CubotNailsDbContext> options, ITenantContext tenantContext)
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

    // Modulo Configuracion del salon (Capa 2): catalogo, recursos, turnos base y excepciones.
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceImage> ServiceImages => Set<ServiceImage>();
    public DbSet<ServicePriceTier> ServicePriceTiers => Set<ServicePriceTier>();
    public DbSet<HairLengthCategory> HairLengthCategories => Set<HairLengthCategory>();
    public DbSet<HairLengthReferenceImage> HairLengthReferenceImages => Set<HairLengthReferenceImage>();
    public DbSet<HairLengthClassification> HairLengthClassifications => Set<HairLengthClassification>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourcePhoto> ResourcePhotos => Set<ResourcePhoto>();
    public DbSet<ResourceServiceLink> ResourceServiceLinks => Set<ResourceServiceLink>();
    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<SalonFieldDefinition> SalonFieldDefinitions => Set<SalonFieldDefinition>();
    public DbSet<Sede> Sedes => Set<Sede>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseRegistration> CourseRegistrations => Set<CourseRegistration>();

    // Modulo Citas / Agenda (Capa 2 - nucleo operativo).
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentServiceItem> AppointmentServiceItems => Set<AppointmentServiceItem>();
    public DbSet<AppointmentMessage> AppointmentMessages => Set<AppointmentMessage>();

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
        configurationBuilder.Properties<ResourceKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ExceptionScope>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<ExceptionReason>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AppointmentStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<Punctuality>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<BookingChannel>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AiAgentRunLogKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<SalonFieldScope>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<SalonFieldType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppProvider>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntities(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(250);
            b.Property(x => x.TaxId).HasMaxLength(80);
            b.Property(x => x.Country).HasMaxLength(80);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.LogoUrl).HasMaxLength(500);
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
            b.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL");
        });

        modelBuilder.Entity<SuperAdminAuditLog>(b =>
        {
            b.Property(x => x.ActionName).HasMaxLength(200).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            b.Property(x => x.IpAddress).HasMaxLength(80);
            b.Property(x => x.PreviousValue).HasColumnType("jsonb");
            b.Property(x => x.NewValue).HasColumnType("jsonb");
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
            b.Property(x => x.RawPayload).HasColumnType("jsonb");
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
            b.Property(x => x.CloudAccessTokenEncrypted).HasColumnType("text");
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
            b.Property(x => x.FieldValuesJson).HasColumnType("jsonb");
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
            // dos lineas distintas del salon como hilos separados (clave de sesion del agente de IA).
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
            b.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
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
            b.Property(x => x.HtmlContent).HasColumnType("text");
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
            b.Property(x => x.SystemPrompt).HasColumnType("text");
            b.Property(x => x.DisabledToolsJson).HasColumnType("jsonb");
            b.Property(x => x.PromptHistoryJson).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentResource>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Detail).HasColumnType("text");
            b.Property(x => x.FileUrl).HasMaxLength(500);
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentPrompt>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Rule).HasMaxLength(500);
            b.Property(x => x.Body).HasColumnType("text");
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
            b.Property(x => x.Content).HasColumnType("text");
            b.Property(x => x.Response).HasColumnType("text");
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
            b.Property(x => x.Description).HasColumnType("text");
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
            b.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
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
            b.Property(x => x.Text).HasColumnType("text").IsRequired();
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

        // ---- Modulo Configuracion del salon (Servicios, Asesores/Recursos, Turnos base, Excepciones) ----

        modelBuilder.Entity<Service>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasColumnType("text");
            b.Property(x => x.Currency).HasMaxLength(8);
            b.Property(x => x.Category).HasMaxLength(80);
            b.Property(x => x.Color).HasMaxLength(20);
            b.Property(x => x.Price).HasPrecision(14, 2);
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<ServiceImage>(b =>
        {
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ServiceId, x.SortOrder });
        });

        modelBuilder.Entity<ServicePriceTier>(b =>
        {
            b.Property(x => x.Price).HasPrecision(14, 2);
            b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            // Una tarifa por (servicio, largo de cabello).
            b.HasIndex(x => new { x.ServiceId, x.Length }).IsUnique();
        });

        modelBuilder.Entity<HairLengthCategory>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(80).IsRequired();
            b.Property(x => x.Description).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<HairLengthReferenceImage>(b =>
        {
            b.Property(x => x.ContentType).HasMaxLength(120);
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.CategoryId, x.SortOrder });
        });

        modelBuilder.Entity<HairLengthClassification>(b =>
        {
            b.Property(x => x.PhotoFileName).HasMaxLength(255);
            b.Property(x => x.PredictedName).HasMaxLength(120);
            b.Property(x => x.Rationale).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<Resource>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.Kind, x.Name });
        });

        modelBuilder.Entity<ResourcePhoto>(b =>
        {
            b.Property(x => x.ContentType).HasMaxLength(120);
            b.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ResourceId }).IsUnique();
        });

        modelBuilder.Entity<ResourceServiceLink>(b =>
        {
            b.Property(x => x.PriceOverride).HasPrecision(14, 2);
            b.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ResourceId, x.ServiceId }).IsUnique();
        });

        modelBuilder.Entity<ShiftTemplate>(b =>
        {
            b.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ResourceId, x.DayOfWeek });
        });

        modelBuilder.Entity<ScheduleException>(b =>
        {
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.ResourceId, x.DateFrom, x.DateTo });
        });

        // ---- Modulo Citas / Agenda (nucleo operativo) ----

        modelBuilder.Entity<Client>(b =>
        {
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.PreferencesJson).HasColumnType("jsonb");
            b.Property(x => x.FieldValuesJson).HasColumnType("jsonb");
            b.Property(x => x.BusinessUnitIdsJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.Phone });
            b.HasIndex(x => new { x.TenantId, x.FullName });
        });

        modelBuilder.Entity<SalonFieldDefinition>(b =>
        {
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Label).HasMaxLength(150).IsRequired();
            b.Property(x => x.Options).HasColumnType("text");
            b.Property(x => x.Description).HasMaxLength(600);
            b.HasIndex(x => new { x.TenantId, x.Scope, x.SortOrder });
            // FieldKey unica por (tenant, scope): identifica el valor dentro del JSON.
            b.HasIndex(x => new { x.TenantId, x.Scope, x.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<Sede>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.City).HasMaxLength(100).IsRequired();
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Sku).HasMaxLength(80);
            b.Property(x => x.Description).HasColumnType("text");
            b.Property(x => x.Specifications).HasColumnType("text");
            b.Property(x => x.Category).HasMaxLength(100);
            b.Property(x => x.Price).HasPrecision(14, 2);
            b.Property(x => x.FieldValuesJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<ProductImage>(b =>
        {
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.SortOrder });
        });

        modelBuilder.Entity<ProductStock>(b =>
        {
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Sede).WithMany().HasForeignKey(x => x.SedeId).OnDelete(DeleteBehavior.Cascade);
            // Una fila de stock por (producto, sede).
            b.HasIndex(x => new { x.ProductId, x.SedeId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SedeId });
        });

        modelBuilder.Entity<Course>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnType("text");
            b.Property(x => x.Price).HasPrecision(14, 2);
            b.HasIndex(x => new { x.TenantId, x.Date });
        });

        modelBuilder.Entity<CourseRegistration>(b =>
        {
            b.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(40);
            b.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.CourseId });
        });

        modelBuilder.Entity<Appointment>(b =>
        {
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.Property(x => x.EstimatedValue).HasPrecision(14, 2);
            b.Property(x => x.FieldValuesJson).HasColumnType("jsonb");
            // ANTI-OVERBOOKING por SOLAPAMIENTO: un exclusion constraint GiST (ck_appointments_no_overlap)
            // prohibe que dos citas activas del mismo (tenant, recurso, fecha) crucen su intervalo
            // [inicio, inicio + duracion + buffer). Se crea por SQL crudo en la migracion (EF no modela
            // EXCLUDE). Subsume el viejo UNIQUE(start_time): dos citas a la misma hora siempre se cruzan,
            // pero dos pegadas (rango medio-abierto) no. Las Cancelled/Rescheduled liberan el cupo.
            b.HasIndex(x => new { x.TenantId, x.ResourceId, x.AppointmentDate });
            b.HasIndex(x => new { x.TenantId, x.ClientId, x.AppointmentDate });
            b.HasIndex(x => x.ChainId);
        });

        modelBuilder.Entity<AppointmentServiceItem>(b =>
        {
            b.Property(x => x.PriceSnapshot).HasPrecision(14, 2);
            b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AppointmentId, x.SortOrder });
        });

        modelBuilder.Entity<AppointmentMessage>(b =>
        {
            b.Property(x => x.Body).HasColumnType("text").IsRequired();
            b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AppointmentId, x.SentAt });
        });
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var applyMethod = typeof(CubotNailsDbContext)
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
