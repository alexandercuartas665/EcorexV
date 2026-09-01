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
        // Servidor SMTP propio por tenant (Mi cuenta); el sender lo prefiere sobre el global.
        services.AddScoped<Tenancy.ITenantEmailConfigService, Tenancy.TenantEmailConfigService>();
        services.AddScoped<IGoogleAuthConfigService, GoogleAuthConfigService>();
        services.AddScoped<Tenancy.ITenantUserService, Tenancy.TenantUserService>();
        services.AddScoped<Tenancy.IAdvisorService, Tenancy.AdvisorService>();
        services.AddScoped<Tenancy.IEvolutionConfigService, Tenancy.EvolutionConfigService>();
        services.AddScoped<Tenancy.IWhatsAppLineService, Tenancy.WhatsAppLineService>();
        services.AddScoped<Tenancy.IWhatsAppConnectorService, Tenancy.WhatsAppConnectorService>();
        services.AddScoped<Tenancy.IPipelineService, Tenancy.PipelineService>();
        services.AddScoped<Tenancy.ILeadService, Tenancy.LeadService>();
        services.AddScoped<Tenancy.IContactLoaderService, Tenancy.ContactLoaderService>();
        services.AddScoped<Contactos.IContactSearchService, Contactos.ContactSearchService>();
        services.AddScoped<Tenancy.ITenantApiService, Tenancy.TenantApiService>();
        services.AddScoped<Tenancy.IFollowUpTaskService, Tenancy.FollowUpTaskService>();
        services.AddScoped<Tenancy.IChatService, Tenancy.ChatService>();
        services.AddScoped<Tenancy.IBlockedNumberService, Tenancy.BlockedNumberService>();
        services.AddScoped<Tenancy.IMessageTemplateService, Tenancy.MessageTemplateService>();
        services.AddScoped<Tenancy.IQuoteTemplateService, Tenancy.QuoteTemplateService>();
        // Plantillas de correo del motor de acciones por filtro (ADR-0056, paso E-mail).
        services.AddScoped<Gestor.IEmailTemplateService, Gestor.EmailTemplateService>();
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
        // Campos personalizados de la tarea por tablero (ADR-0065): calcado de IItemFieldService.
        services.AddScoped<Tenancy.ITaskFieldService, Tenancy.TaskFieldService>();
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
        // Salto de flujo -> tarea hija (ADR-0076): el motor lo resuelve perezosamente al alcanzar un fin con
        // JumpToDefinitionId (evita el ciclo de DI con TaskItemService).
        services.AddScoped<Workflows.IChildTaskStarter, Workflows.ChildTaskStarter>();
        // Editor de flujos del prototipo (ADR-0022): indice con metricas + mutaciones del canvas.
        services.AddScoped<Workflows.IWorkflowDesignService, Workflows.WorkflowDesignService>();
        // Agentes de IA en nodos (ola 1): arma el contexto del paso (nodo+formulario, datos
        // previos, tarea/tercero, historial). La EJECUCION del agente es la ola 2.
        services.AddScoped<Workflows.IWorkflowAgentContextBuilder, Workflows.WorkflowAgentContextBuilder>();
        // Agentes de IA en nodos (ola 2): el agente ATIENDE el paso de verdad. El invoker es lo
        // unico que habla con el proveedor (por eso es una interfaz aparte: la llamada de red queda
        // fuera de la transaccion del motor, y en pruebas se sustituye por un doble); el runner
        // decide y persiste; el dispatcher lo dispara de forma asincrona desde el worker.
        services.AddScoped<Workflows.IWorkflowAgentInvoker, Workflows.WorkflowAgentInvoker>();
        services.AddScoped<Workflows.IWorkflowAgentStepRunner, Workflows.WorkflowAgentStepRunner>();
        services.AddScoped<Workflows.IWorkflowAgentStepDispatcher, Workflows.WorkflowAgentStepDispatcher>();
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
        services.AddScoped<Forms.IFormTemplateRenderService, Forms.FormTemplateRenderService>();
        services.AddScoped<Forms.IFormTokenService, Forms.FormTokenService>();
        services.AddScoped<Forms.IFormTextAssistService, Forms.FormTextAssistService>();
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
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.ImprimirPlantillaVerb>();
        services.AddScoped<Rules.IRuleVerb, Rules.Verbs.ConvertirAFormularioVerb>();
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
        services.AddScoped<Actividades.IActivityCatalogService, Actividades.ActivityCatalogService>();
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
        // Gestor Documental (portado del modulo 2.15 del hermano PROPIA). Dos mitades: el Archivo
        // central (categoria/carpeta/documento con versiones) y los Expedientes (TRD).
        // IDocumentoFileStore lo registra la capa de presentacion: es la unica que conoce wwwroot.
        services.AddScoped<Documentos.IDocumentoService, Documentos.DocumentoService>();
        services.AddScoped<Documentos.IExpedienteService, Documentos.ExpedienteService>();
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
        // Catalogo de asesores/vendedores del tenant (000074), alimenta "Vendedor asignado".
        services.AddScoped<Asesores.IAsesorService, Asesores.AsesorService>();
        // Campos configurables por ficha (000232): vuelven las fichas del tercero datos por tenant.
        services.AddScoped<Directorio.ITerceroFieldService, Directorio.TerceroFieldService>();
        services.AddScoped<Directorio.ITerceroFichaService, Directorio.TerceroFichaService>();
        services.AddScoped<Directorio.ITerceroFormService, Directorio.TerceroFormService>();
        // Catalogo GLOBAL de ciudades / municipios (Colombia): alimenta el selector de ciudad del
        // Directorio y del modal de Tercero (reemplaza el input libre). No tenant-scoped.
        services.AddScoped<Catalogos.ICiudadCatalogService, Catalogos.CiudadCatalogService>();
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
        // Disenador de acciones por filtro de contactos (ADR-0056, Fase 1): persiste la LISTA de
        // pasos + ventanas de horario atada 1:1 a un filtro guardado. El motor de ejecucion es Fase 2.
        services.AddScoped<Tenancy.IContactWorkflowService, Tenancy.ContactWorkflowService>();
        // Motor de ejecucion del disenador de acciones (ADR-0056, Fase 2): resuelve el segmento del
        // filtro y dispara cada paso sobre los contactos, con dedupe/ventana/rate. Lo arranca el
        // ContactWorkflowWorker (Ecorex.SuperAdmin/RealTime), igual patron que ScheduledJobWorker.
        services.AddScoped<Gestor.IContactWorkflowDispatcher, Gestor.ContactWorkflowDispatcher>();
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
        // Toolset de Tareas: el agente crea una tarea en un tablero y adjunta los archivos del cliente.
        services.AddScoped<Tenancy.TasksToolset>();
        services.AddScoped<Tenancy.ITasksToolset>(sp => sp.GetRequiredService<Tenancy.TasksToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.TasksToolset>());
        // Toolset de Directorio: el agente registra un contacto (tercero) en el Directorio General.
        services.AddScoped<Tenancy.DirectorioToolset>();
        services.AddScoped<Tenancy.IDirectorioToolset>(sp => sp.GetRequiredService<Tenancy.DirectorioToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.DirectorioToolset>());
        // Toolset de Inventario: el agente CONSULTA (solo lectura) items, precios y existencias.
        services.AddScoped<Tenancy.InventarioToolset>();
        services.AddScoped<Tenancy.IInventarioToolset>(sp => sp.GetRequiredService<Tenancy.InventarioToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.InventarioToolset>());
        // Toolset de Autoria de formularios (ADR-0058): construye formularios/plantillas/enlaces/modulos sin
        // SQL, delegando en los servicios de aplicacion. Se expone externo por /api/mgmt/agent/tools.
        services.AddScoped<Tenancy.FormAuthoringToolset>();
        services.AddScoped<Tenancy.IFormAuthoringToolset>(sp => sp.GetRequiredService<Tenancy.FormAuthoringToolset>());
        services.AddScoped<Tenancy.IAgentToolset>(sp => sp.GetRequiredService<Tenancy.FormAuthoringToolset>());
        // Atencion del agente por lineas de WhatsApp (binding, orquestacion, bitacora).
        services.AddScoped<Tenancy.IAiAgentLineService, Tenancy.AiAgentLineService>();
        services.AddScoped<Tenancy.IAgentConversationService, Tenancy.AgentConversationService>();
        // Cola de auto-respuesta No-Op por defecto; el host con webhook (SuperAdmin) la reemplaza.
        services.AddSingleton<Tenancy.IAgentReplyQueue, Tenancy.NoOpAgentReplyQueue>();
        // Motor de Reportes y BI (ADR-0051, Ola 1): la capa PROPIA e independiente de la libreria.
        // Catalogo semantico (nativas curadas + contenedores derivados) + datasource tenant-safe
        // (traduce el spec declarativo a EF parametrizado; el aislamiento lo garantiza el filtro
        // global del DbContext). Sumar una entidad nativa reportable = registrar otro IReportableSource.
        services.AddScoped<Reporting.IReportableSource, Reporting.Sources.TaskItemReportSource>();
        services.AddScoped<Reporting.Sources.ContainerReportReader>();
        services.AddScoped<Reporting.IReportCatalog, Reporting.ReportCatalog>();
        services.AddScoped<Reporting.IReportDataSource, Reporting.ReportDataSource>();
        // Ola 4: definiciones guardadas (ReportDefinition) + autoria por IA (instruccion -> JSON-spec
        // validado contra el catalogo -> dashboard). El generador real usa el agente/proveedor del
        // tenant (AiUsageLog); en pruebas se falsea IReportSpecGenerator.
        services.AddScoped<Reporting.IReportDefinitionService, Reporting.ReportDefinitionService>();
        // Plantillas de reportes reutilizables entre tenants (ADR-0062, modelo hibrido): catalogo
        // maestro de plataforma (CRUD auditado) + servicio de activacion tenant-scoped (snapshot +
        // vinculo TemplateId, re-sincronizacion y barrido por compatibilidad de fuente).
        services.AddScoped<Reporting.Templates.IReportActivationService, Reporting.Templates.ReportActivationService>();
        services.AddScoped<Reporting.Templates.IReportTemplateService, Reporting.Templates.ReportTemplateService>();
        services.AddScoped<Reporting.Authoring.IReportSpecGenerator, Reporting.Authoring.AiReportSpecGenerator>();
        services.AddScoped<Reporting.Authoring.IReportAuthoringService, Reporting.Authoring.ReportAuthoringService>();
        // Conector de datos externos gobernado (ADR-0064): lector externo (analogo al de contenedor),
        // administracion del catalogo (CRUD auditado, solo PlatformAdmin) y binding de imprimibles a
        // datos externos. El executor ADO.NET de solo lectura lo aporta Infrastructure (tiene drivers).
        services.AddScoped<Reporting.External.ExternalReportReader>();
        services.AddScoped<Reporting.External.IExternalDataSourceService, Reporting.External.ExternalDataSourceService>();
        // Conexiones de datos externas PROPIAS del tenant (tenant-scoped, con escritura por conexion).
        services.AddScoped<Tenancy.DataConnections.ITenantDataConnectionService, Tenancy.DataConnections.TenantDataConnectionService>();
        services.AddScoped<Reporting.External.IExternalReportBindingService, Reporting.External.ExternalReportBindingService>();
        return services;
    }
}
