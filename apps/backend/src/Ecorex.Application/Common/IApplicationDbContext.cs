using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Common;

/// <summary>
/// Abstraccion del DbContext para los casos de uso de Application, sin acoplar a la
/// implementacion concreta de Infrastructure. Expone solo los conjuntos que la capa necesita.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<PlatformUser> PlatformUsers { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantConfiguration> TenantConfigurations { get; }
    DbSet<TenantEvolutionConfig> TenantEvolutionConfigs { get; }
    DbSet<WhatsAppLine> WhatsAppLines { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<PipelineFieldDefinition> PipelineFieldDefinitions { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadActivity> LeadActivities { get; }
    DbSet<LeadNote> LeadNotes { get; }
    DbSet<LeadFile> LeadFiles { get; }
    DbSet<ContactImportBatch> ContactImportBatches { get; }
    DbSet<FollowUpTask> FollowUpTasks { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<TenantBlockedNumber> TenantBlockedNumbers { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<QuoteTemplate> QuoteTemplates { get; }
    DbSet<TemplateAsset> TemplateAssets { get; }
    DbSet<AiAgent> AiAgents { get; }
    DbSet<AiAgentResource> AiAgentResources { get; }
    DbSet<AiAgentPrompt> AiAgentPrompts { get; }
    DbSet<AiAgentCacheField> AiAgentCacheFields { get; }
    DbSet<AiAgentCacheValue> AiAgentCacheValues { get; }
    DbSet<AiAgentLineBinding> AiAgentLineBindings { get; }
    DbSet<AiAgentRunLog> AiAgentRunLogs { get; }
    DbSet<AiUsageLog> AiUsageLogs { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<TaskBoard> TaskBoards { get; }
    DbSet<TaskBoardColumn> TaskBoardColumns { get; }
    DbSet<TaskCard> TaskCards { get; }
    DbSet<TaskCardAssignment> TaskCardAssignments { get; }
    DbSet<TaskCardTag> TaskCardTags { get; }
    DbSet<TaskCardTagAssignment> TaskCardTagAssignments { get; }
    DbSet<TaskCardChecklistItem> TaskCardChecklistItems { get; }
    DbSet<TaskCardActivity> TaskCardActivities { get; }
    DbSet<TaskCardAttachment> TaskCardAttachments { get; }
    DbSet<ActivityType> ActivityTypes { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<TaskItem> TaskItems { get; }
    DbSet<TaskItemTag> TaskItemTags { get; }
    DbSet<TaskItemTagAssignment> TaskItemTagAssignments { get; }
    DbSet<TaskWorkLog> TaskWorkLogs { get; }
    DbSet<TaskItemActivity> TaskItemActivities { get; }
    DbSet<TaskItemAttachment> TaskItemAttachments { get; }
    DbSet<TaskItemChecklistItem> TaskItemChecklistItems { get; }
    DbSet<TaskItemAssignment> TaskItemAssignments { get; }
    DbSet<TenantSequence> TenantSequences { get; }
    DbSet<WorkflowDefinition> WorkflowDefinitions { get; }
    DbSet<WorkflowNode> WorkflowNodes { get; }
    DbSet<WorkflowEdge> WorkflowEdges { get; }
    DbSet<WorkflowInstance> WorkflowInstances { get; }
    DbSet<WorkflowStepHistory> WorkflowStepHistories { get; }
    DbSet<FormDefinition> FormDefinitions { get; }
    DbSet<FormContainer> FormContainers { get; }
    DbSet<FormQuestion> FormQuestions { get; }
    DbSet<FormResponse> FormResponses { get; }
    DbSet<FormFlowLink> FormFlowLinks { get; }
    DbSet<FormToken> FormTokens { get; }
    DbSet<WorkflowNodeForm> WorkflowNodeForms { get; }
    DbSet<RuleDocument> RuleDocuments { get; }
    DbSet<Rule> Rules { get; }
    DbSet<RuleExecutionLog> RuleExecutionLogs { get; }
    DbSet<FormFieldRule> FormFieldRules { get; }
    DbSet<WorkflowNodeRule> WorkflowNodeRules { get; }
    DbSet<OrgUnit> OrgUnits { get; }
    DbSet<OrgUnitMember> OrgUnitMembers { get; }
    DbSet<WorkflowNodePolicy> WorkflowNodePolicies { get; }
    DbSet<ModuleDefinition> ModuleDefinitions { get; }
    DbSet<TenantModule> TenantModules { get; }
    DbSet<SaasPlan> SaasPlans { get; }
    DbSet<SaasPlanLimit> SaasPlanLimits { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<TenantPayment> TenantPayments { get; }
    DbSet<WompiMasterConfig> WompiMasterConfigs { get; }
    DbSet<WompiWebhookEvent> WompiWebhookEvents { get; }
    DbSet<EvolutionMasterConfig> EvolutionMasterConfigs { get; }
    DbSet<AiProviderConfig> AiProviderConfigs { get; }
    DbSet<PlatformBranding> PlatformBrandings { get; }
    DbSet<EmailConfig> EmailConfigs { get; }
    DbSet<GoogleAuthConfig> GoogleAuthConfigs { get; }
    DbSet<TenantApiConfig> TenantApiConfigs { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<AccountActivationCode> AccountActivationCodes { get; }
    DbSet<SuperAdminAuditLog> SuperAdminAuditLogs { get; }
    DbSet<ScrapeSource> ScrapeSources { get; }
    DbSet<ScrapeRun> ScrapeRuns { get; }

    // Inventarios (grupo Sistema - Inventarios): catalogos normalizados + items con stock por bodega.
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Brand> Brands { get; }
    DbSet<ItemGroup> ItemGroups { get; }
    DbSet<ItemSubgroup> ItemSubgroups { get; }
    DbSet<ItemType> ItemTypes { get; }
    DbSet<Item> Items { get; }
    DbSet<ItemImage> ItemImages { get; }
    DbSet<ItemStock> ItemStocks { get; }

    // Plantillas HSM de WhatsApp (ADR-0029): mensajes plantilla con ciclo de aprobacion.
    DbSet<WhatsAppTemplate> WhatsAppTemplates { get; }

    // Menu configurable por perfil (Ola 1): vistas del menu por tenant y sus nodos (arbol).
    DbSet<MenuView> MenuViews { get; }
    DbSet<MenuNode> MenuNodes { get; }

    // Roles de permisos dinamicos (Ola B1, ADR-0032): matriz Modulo x Accion por tenant.
    DbSet<Rol> Roles { get; }
    DbSet<RolPermiso> RolPermisos { get; }

    // Directorio General (modulo 000232): terceros (empresas / personas) con perfiles de
    // negocio, contactos embebidos y fichas dinamicas (jsonb).
    DbSet<Tercero> Terceros { get; }
    DbSet<TerceroContacto> TerceroContactos { get; }
    DbSet<TerceroFieldDefinition> TerceroFieldDefinitions { get; }
    DbSet<TerceroNota> TerceroNotas { get; }

    // Gestor de Clientes (modulo 000740): bolsa (kanban de terceros por estado), oportunidades,
    // citas/agenda, filtros dinamicos guardados y prospectos scrapeados (demo).
    DbSet<BolsaColumna> BolsaColumnas { get; }
    DbSet<Oportunidad> Oportunidades { get; }
    DbSet<Cita> Citas { get; }
    DbSet<TerceroFiltro> TerceroFiltros { get; }
    DbSet<ProspectoScrapeado> ProspectosScrapeados { get; }

    // Conceptos de actividades (modulo 000270): catalogo de dos niveles Categoria ->
    // Subcategoria (concepto) con flags RQ07, vinculos opcionales (flujo/formulario/tablero)
    // y relaciones M:N (cargos/terceros) en tablas hijas.
    DbSet<ActividadCategoria> ActividadCategorias { get; }
    DbSet<ActividadSubcategoria> ActividadSubcategorias { get; }
    DbSet<ActividadSubcategoriaCargo> ActividadSubcategoriaCargos { get; }
    DbSet<ActividadSubcategoriaTercero> ActividadSubcategoriaTerceros { get; }
    DbSet<ActividadSubcategoriaNotificacion> ActividadSubcategoriaNotificaciones { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre una transaccion explicita para casos de uso multi-paso (ej. emitir consecutivo +
    /// insertar TaskItem de forma atomica). Los casos simples siguen usando SaveChangesAsync solo.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si ya hay una transaccion abierta sobre la conexion. Permite que un caso de
    /// uso anidado (ej. WorkflowEngine.StartInstanceAsync dentro de TaskItemService.CreateAsync)
    /// se una a la transaccion del llamador en vez de intentar abrir otra.
    /// </summary>
    bool HasActiveTransaction { get; }
}
