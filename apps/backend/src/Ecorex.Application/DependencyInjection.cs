using Ecorex.Application.Admin;
using Ecorex.Application.Auth;
using Ecorex.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ITenantAdminService, TenantAdminService>();
        services.AddScoped<IPlanAdminService, PlanAdminService>();
        services.AddScoped<ISubscriptionAdminService, SubscriptionAdminService>();
        services.AddScoped<IPaymentAdminService, PaymentAdminService>();
        services.AddScoped<IPaymentReceiptService, PaymentReceiptService>();
        services.AddScoped<IAuditAdminService, AuditAdminService>();
        services.AddScoped<IWompiConfigService, WompiConfigService>();
        services.AddScoped<IEvolutionMasterConfigService, EvolutionMasterConfigService>();
        services.AddScoped<IAiServerConfigService, AiServerConfigService>();
        services.AddScoped<IWompiWebhookService, WompiWebhookService>();
        services.AddScoped<IWompiCheckoutService, WompiCheckoutService>();
        services.AddScoped<IRecurringBillingService, RecurringBillingService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IPlatformOperatorService, PlatformOperatorService>();
        services.AddScoped<ISelfSignupService, SelfSignupService>();
        services.AddScoped<IAccountActivationService, AccountActivationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();
        services.AddScoped<IPlatformBrandingService, PlatformBrandingService>();
        services.AddScoped<IEmailConfigService, EmailConfigService>();
        services.AddScoped<IGoogleAuthConfigService, GoogleAuthConfigService>();
        services.AddScoped<Tenancy.ITenantUserService, Tenancy.TenantUserService>();
        services.AddScoped<Tenancy.IAdvisorService, Tenancy.AdvisorService>();
        services.AddScoped<Tenancy.IEvolutionConfigService, Tenancy.EvolutionConfigService>();
        services.AddScoped<Tenancy.IWhatsAppLineService, Tenancy.WhatsAppLineService>();
        services.AddScoped<Tenancy.IWhatsAppConnectorService, Tenancy.WhatsAppConnectorService>();
        services.AddScoped<Tenancy.IPipelineService, Tenancy.PipelineService>();
        services.AddScoped<Tenancy.ILeadService, Tenancy.LeadService>();
        services.AddScoped<Tenancy.IContactLoaderService, Tenancy.ContactLoaderService>();
        services.AddScoped<Tenancy.ITenantApiService, Tenancy.TenantApiService>();
        services.AddScoped<Tenancy.IFollowUpTaskService, Tenancy.FollowUpTaskService>();
        services.AddScoped<Tenancy.IChatService, Tenancy.ChatService>();
        services.AddScoped<Tenancy.IBlockedNumberService, Tenancy.BlockedNumberService>();
        services.AddScoped<Tenancy.IMessageTemplateService, Tenancy.MessageTemplateService>();
        services.AddScoped<Tenancy.IQuoteTemplateService, Tenancy.QuoteTemplateService>();
        services.AddScoped<Tenancy.ITemplateAssetService, Tenancy.TemplateAssetService>();
        services.AddScoped<Tenancy.IQuoteRenderService, Tenancy.QuoteRenderService>();
        // Broadcaster por defecto (no-op); la app host con SignalR lo reemplaza.
        services.AddScoped<Tenancy.IChatBroadcaster, Tenancy.NoOpChatBroadcaster>();
        // Broadcaster del nucleo de tareas por defecto (no-op); la app host con SignalR lo reemplaza.
        services.AddScoped<Tenancy.ITaskBroadcaster, Tenancy.NoOpTaskBroadcaster>();
        services.AddScoped<Tenancy.IWebhookAdminService, Tenancy.WebhookAdminService>();
        // Tunel por defecto (no-op); la app host con cloudflared lo reemplaza por singleton.
        services.AddSingleton<Tenancy.IDevTunnel, Tenancy.NoOpDevTunnel>();
        services.AddScoped<Tenancy.IChatIngestService, Tenancy.ChatIngestService>();
        services.AddScoped<Tenancy.IDashboardService, Tenancy.DashboardService>();
        services.AddScoped<Tenancy.IAiAgentService, Tenancy.AiAgentService>();
        services.AddScoped<Tenancy.IAiAgentCacheService, Tenancy.AiAgentCacheService>();
        services.AddScoped<Tenancy.IAiUsageService, Tenancy.AiUsageService>();
        services.AddScoped<Tenancy.IAiInferenceService, Tenancy.AiInferenceService>();
        services.AddScoped<Tenancy.IAutomationService, Tenancy.AutomationService>();
        services.AddScoped<Tenancy.ITaskBoardService, Tenancy.TaskBoardService>();
        services.AddScoped<Tenancy.ITaskCardService, Tenancy.TaskCardService>();
        // Nucleo de tareas/proyectos (FASE 3, ADR-0013).
        services.AddScoped<Tenancy.ISequenceService, Tenancy.SequenceService>();
        services.AddScoped<Tenancy.IActivityTypeService, Tenancy.ActivityTypeService>();
        services.AddScoped<Tenancy.IProjectService, Tenancy.ProjectService>();
        services.AddScoped<Tenancy.ITaskItemService, Tenancy.TaskItemService>();
        // Notificaciones in-app (Ola 7 - entrega real). La entrega la escriben los servicios de
        // dominio (TaskItemService al asignar); este servicio cubre lectura/campana y marcado.
        services.AddScoped<Notifications.INotificationService, Notifications.NotificationService>();
        // Tableros de actividades unificados (ADR-0020): tarjetas = TaskItem.
        services.AddScoped<Tenancy.IActivityBoardService, Tenancy.ActivityBoardService>();
        services.AddScoped<Tenancy.IBusinessUnitService, Tenancy.BusinessUnitService>();
        // Motor de flujos BPMN (FASE 4, ADR-0014). El hook de reglas es el REAL del
        // RulesEngine (FASE 4 ola 3, ADR-0016): ejecuta las reglas autonomas del nodo.
        services.AddScoped<Workflows.IWorkflowEngine, Workflows.WorkflowEngine>();
        services.AddScoped<Workflows.IWorkflowRuleHook, Rules.WorkflowRuleHook>();
        // Editor de flujos del prototipo (ADR-0022): indice con metricas + mutaciones del canvas.
        services.AddScoped<Workflows.IWorkflowDesignService, Workflows.WorkflowDesignService>();
        // Bandeja operativa de flujos (runtime, ola F2, ADR-0036): "mis pasos pendientes" +
        // atender (formulario o completar/aprobar) + reclamar/reasignar. Une la asignacion por
        // nodo (INodeAssigneeResolver) con el motor (IWorkflowEngine).
        services.AddScoped<Workflows.IWorkflowInboxService, Workflows.WorkflowInboxService>();
        // Arranque de tareas-proceso (Ola A1): camina el flujo EN SECO desde el startEvent hasta el
        // primer nodo Task para saber QUIEN lo atendera antes de crear la actividad (el encargado lo
        // dicta el flujo, no el usuario). Lo consumen el wizard y el arranque form-first.
        services.AddScoped<Workflows.IWorkflowStartService, Workflows.WorkflowStartService>();
        // Formularios dinamicos (FASE 4 ola 2, ADR-0015): definiciones, respuestas y tokens.
        services.AddScoped<Forms.IFormDefinitionService, Forms.FormDefinitionService>();
        services.AddScoped<Forms.IFormResponseService, Forms.FormResponseService>();
        services.AddScoped<Forms.IFormTokenService, Forms.FormTokenService>();
        // Formularios avanzados (ola F1, doc 01 D4): lookup/autocompletado desde tablas del
        // tenant. Un adaptador por origen (Tercero, Item, DataContainer) + fachada que despacha.
        // Sumar una fuente = registrar otro IFormLookupSource, sin tocar consumidores.
        services.AddScoped<Forms.Lookups.IFormLookupSource, Forms.Lookups.TerceroLookupSource>();
        services.AddScoped<Forms.Lookups.IFormLookupSource, Forms.Lookups.ItemLookupSource>();
        services.AddScoped<Forms.Lookups.IFormLookupSource, Forms.Lookups.DataContainerLookupSource>();
        services.AddScoped<Forms.Lookups.IFormLookupService, Forms.Lookups.FormLookupService>();
        // Motor de reglas (FASE 4 ola 3, ADR-0016): REGISTRO TIPADO de verbos en DI (el
        // ejecutor resuelve por diccionario IRuleVerb.Name; verbo desconocido = error
        // tipado, nunca Activator.CreateInstance sobre texto como el legacy).
        services.AddScoped<Rules.IRulesEngine, Rules.RulesEngine>();
        services.AddScoped<Rules.IRuleDocumentService, Rules.RuleDocumentService>();
        services.AddScoped<Rules.IFormRuleDispatcher, Rules.FormRuleDispatcher>();
        services.AddScoped<Rules.IRuleExecutionLogCleaner, Rules.RuleExecutionLogCleaner>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.PasarCamposVerb>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.BloquearCampoPorCondicionVerb>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.AsignarConsecutivoVerb>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.GenerarTareasDesdeTablaVerb>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.NotificarVerb>();
        // Modulos de sistema (FASE 5, ADR-0017): organigrama de dependencias (legacy 000850)
        // y registro de modulos web (legacy 000109).
        services.AddScoped<Organization.IOrgUnitService, Organization.OrgUnitService>();
        // Asignacion por nodo (ADR-0035, ola F1): policies Dependencia/Cargo por nodo Task y
        // resolver de candidatos (nodo -> TenantUserIds). La bandeja/atender es la ola F2.
        services.AddScoped<Organization.IWorkflowNodePolicyService, Organization.WorkflowNodePolicyService>();
        services.AddScoped<Organization.INodeAssigneeResolver, Organization.NodeAssigneeResolver>();
        services.AddScoped<Modules.IModuleRegistryService, Modules.ModuleRegistryService>();
        // Inventarios (grupo Sistema - Inventarios): catalogos normalizados (bodegas, marcas,
        // grupos, subgrupos, tipos) + items con stock por bodega e imagenes por URL.
        services.AddScoped<Crm.IConceptoActividadService, Crm.ConceptoActividadService>();
        // Etapas configurables del pipeline de oportunidades del CRM (000740): catalogo por tenant
        // (nombre/orden/color/tipo) que reemplaza el enum fijo OportunidadEtapa; seed + backfill.
        services.AddScoped<Crm.IOportunidadEstadoService, Crm.OportunidadEstadoService>();
        services.AddScoped<Inventory.IInventoryCatalogService, Inventory.InventoryCatalogService>();
        services.AddScoped<Inventory.IItemService, Inventory.ItemService>();
        // Campos configurables del item POR tipo (000066): definiciones que gobiernan la ficha.
        services.AddScoped<Inventory.IItemFieldService, Inventory.ItemFieldService>();
        // Contenedor de datos: modelos dinamicos EAV (arbol/submodelos) + import/export Excel, y
        // la configuracion de importacion (conectores con credenciales cifradas, clientes, procesos).
        services.AddScoped<DataContainers.IDataContainerService, DataContainers.DataContainerService>();
        // Nucleo de ingesta EAV reutilizable (doc 03 s6): lo comparten el import REST y el
        // importador via agente (Append/Replace/Upsert sobre fila+celdas).
        services.AddScoped<DataContainers.IRowIngestService, DataContainers.RowIngestService>();
        // Contenedor (DataModel): agrupa varias tablas + relaciones internas (lienzo ER). Reusa el
        // nivel tabla via IDataContainerService.
        services.AddScoped<DataContainers.IDataModelService, DataContainers.DataModelService>();
        // Motor COMPARTIDO de campos tipo lista alimentados por el Contenedor de datos. Lo
        // consumen los campos configurables del tercero y del item (y mas adelante el motor de
        // formularios): un solo lugar donde vive "elegir una fila y propagar sus valores".
        services.AddScoped<DataLookups.IDataLookupService, DataLookups.DataLookupService>();
        // Cliente/agente colmena como recurso transversal propio (ADR-0045): duenio del ciclo de vida de
        // los clientes; Contenedores/Extraccion lo reusan. DataImportConfigService delega aqui.
        services.AddScoped<Agents.IAgentClientService, Agents.AgentClientService>();
        services.AddScoped<Agents.IAgentActivityQuery, Agents.AgentActivityQuery>();
        services.AddScoped<DataContainers.IDataImportConfigService, DataContainers.DataImportConfigService>();
        // Publicacion de una tabla como modulo del menu (nodo de menu + ruta inmutable).
        services.AddScoped<DataContainers.IDataContainerModuleService, DataContainers.DataContainerModuleService>();
        // Vinculos dato-a-dato de las relaciones (FASE 2 del rediseno de relaciones).
        services.AddScoped<DataContainers.IDataRelationLinkService, DataContainers.DataRelationLinkService>();
        // Menu configurable por perfil (Ola 1): vistas del menu por tenant + asignacion usuario->vista.
        services.AddScoped<MenuConfig.IMenuConfigService, MenuConfig.MenuConfigService>();
        // Roles de permisos dinamicos (Ola B1, ADR-0032): matriz Modulo x Accion por tenant,
        // catalogo derivado del menu, asignacion de rol a usuario y resolucion de permisos
        // efectivos (lista para el enforcement de Ola B2). La aplicacion en backend NO va aqui.
        services.AddScoped<Roles.IRolService, Roles.RolService>();
        // Directorio General (modulo 000232): terceros (empresas / personas) con perfiles de
        // negocio, contactos embebidos, fichas dinamicas (jsonb) y sub-permisos nombrados.
        services.AddScoped<Directorio.ITerceroService, Directorio.TerceroService>();
        // Campos configurables por ficha (000232): vuelven las fichas del tercero datos por tenant.
        services.AddScoped<Directorio.ITerceroFieldService, Directorio.TerceroFieldService>();
        services.AddScoped<Directorio.ITerceroFormService, Directorio.TerceroFormService>();
        // Conceptos de actividades (modulo 000270): catalogo de dos niveles Categoria ->
        // Subcategoria (concepto) con flags RQ07, vinculos a flujo/formulario/tablero y M:N cargos/terceros.
        services.AddScoped<Actividades.IActividadCatalogoService, Actividades.ActividadCatalogoService>();
        // Motor de programaciones (modulo 000889 "Programar actividad"): CRUD de programaciones
        // (cabecera + reglas + canales). El worker de disparo + bitacora llega en P2.
        services.AddScoped<Scheduling.IScheduledJobService, Scheduling.ScheduledJobService>();
        // Runner del motor (ola P2): dispara las ventanas vencidas, escribe la bitacora y avanza NextRunAt.
        services.AddScoped<Scheduling.IScheduledJobDispatcher, Scheduling.ScheduledJobDispatcher>();
        // Canales de entrega (ola P4): ALLOW-LIST TIPADA, sin reflexion. Un canal SIN sender registrado
        // (Slack/SMS, que no tienen integracion en el sistema) NO se entrega y queda asi en la bitacora.
        services.AddScoped<Scheduling.IScheduledJobChannelSender, Scheduling.EmailChannelSender>();
        services.AddScoped<Scheduling.IScheduledJobChannelSender, Scheduling.WhatsAppChannelSender>();
        // Configuracion de la entidad (000615): agencias/areas/sucursales del tenant + campos dinamicos.
        services.AddScoped<Entidades.IEntidadService, Entidades.EntidadService>();
        // Gestor de Clientes (modulo 000740): prospectos scrapeados, Bolsa de contactos (kanban de
        // terceros), oportunidades (embudo), agenda de citas y filtros dinamicos con conteo en vivo.
        services.AddScoped<Gestor.IGestorContactosService, Gestor.GestorContactosService>();
        // Plantillas HSM de WhatsApp (ADR-0029): CRUD con resultados tipados. Submit/SyncStatus
        // son STUBS: sin integracion real con la WhatsApp Cloud API de Meta.
        services.AddScoped<Tenancy.IWhatsAppTemplateService, Tenancy.WhatsAppTemplateService>();
        // Extraccion de datos / web scraping acotado (modulo 000730, ADR-0025). El fetcher
        // HTTP (IScrapeFetcher) y las opciones del guard SSRF se registran en Infrastructure;
        // la app host puede sobreescribir ScrapeGuardOptions (AllowLoopback SOLO en dev).
        services.AddScoped<Scraping.IScrapeService, Scraping.ScrapeService>();
        // Flujos de extraccion por navegador (modulo 000730, capitulo "Extraccion de Datos"): CRUD de
        // configuracion (flujo + pasos + variables cifradas). Solo config; el runtime es diferido.
        services.AddScoped<Scraping.IScrapeFlowService, Scraping.ScrapeFlowService>();
        // Costura de cierre comercial (ADR-0028): el runtime de agentes depende de IAgentLeadSink, no de
        // Lead/CRM. Default No-Op (funciona sin CRM); el adaptador PipelineLeadSink lo reemplaza como
        // implementacion VIVA para conservar el comportamiento actual (crea el lead en el pipeline).
        services.AddScoped<Tenancy.IAgentLeadSink, Tenancy.NoOpAgentLeadSink>();
        services.AddScoped<Tenancy.IAgentLeadSink, Tenancy.PipelineLeadSink>();
        // Herramientas (function calling / "MCP") que el agente de IA puede usar. Cada toolset se registra
        // tambien como IAgentToolset para que el motor de inferencia los agregue todos y filtre por agente.
        services.AddScoped<Tenancy.PipelineToolset>();
        services.AddScoped<Tenancy.IPipelineToolset>(sp => sp.GetRequiredService<Tenancy.PipelineToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.PipelineToolset>());
        // Atencion del agente por lineas de WhatsApp (binding, orquestacion, bitacora).
        services.AddScoped<Tenancy.IAiAgentLineService, Tenancy.AiAgentLineService>();
        services.AddScoped<Tenancy.IAgentConversationService, Tenancy.AgentConversationService>();
        // Cola de auto-respuesta No-Op por defecto; el host con webhook (SuperAdmin) la reemplaza.
        services.AddSingleton<Tenancy.IAgentReplyQueue, Tenancy.NoOpAgentReplyQueue>();
        return services;
    }
}
