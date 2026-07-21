using System.Globalization;
using System.Security.Claims;
using Ecorex.Application;
using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure;
using Ecorex.Infrastructure.Persistence;
using Ecorex.SuperAdmin.Agents;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Override local NO versionado (gitignored: appsettings.*.local.json) para apuntar el dev a una
// BD propia (p.ej. la de la nube dev/staging) sin poner la credencial en el repo publico. Si el
// archivo no existe, no pasa nada; la conexion sigue saliendo de ECOREX_DB_CONNECTION / appsettings.
builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: false);

// Formato numerico uniforme en todo el sistema, independiente del locale del servidor (dev o Railway):
// coma = separador de miles, punto = decimal (ej. 3,500,000.50). Evita que el host cambie como se ven los montos.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Sube el limite de mensajes del circuito SignalR: al arrastrar y soltar archivos al chat,
    // el contenido viaja como base64 por invokeMethodAsync y el limite por defecto (32 KB) lo
    // rechazaba en silencio. 32 MB cubre el tope de 16 MB del archivo (~21 MB en base64).
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 32L * 1024 * 1024);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        // 30 dias deslizantes: con "Recordar sesion" marcado la cookie es persistente (IsPersistent)
        // y dura hasta 30 dias renovandose en cada visita; sin marcar es cookie de sesion (muere al
        // cerrar el navegador). Antes eran 8h, lo que obligaba a re-loguear con frecuencia.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    // Operador de plataforma (Super Admin / roles internos): tiene claim platform_role.
    .AddPolicy("PlatformOperator", p => p.RequireClaim("platform_role"))
    // Solo SuperAdmin (alta del equipo de plataforma).
    .AddPolicy("SuperAdminOnly", p => p.RequireClaim("platform_role", "SuperAdmin"))
    // Miembro de una agencia: tiene claim tenant_id.
    .AddPolicy("TenantMember", p => p.RequireClaim("tenant_id"))
    // ---- Policies por modulo ----
    // Ola 7 (endurecimiento) PASO 2 REALIZADO para la familia Tareas: estas policies ya no son
    // placeholder de tenant_id: derivan el requisito REAL del Module Registry (PermissionRequirement
    // Modulo x Accion), sin tocar las paginas -solo cambia la definicion aqui-. El handler concede a
    // Owner/Admin y sin-rol (Unrestricted, fail-open); solo un rol limitado sin el permiso queda fuera.
    // "Formularios.Disenar" es una POLICY COMPUESTA (multi-permiso del legacy): exige VER *y* EDITAR
    // formularios (dos PermissionRequirement = AND). Route-keys del catalogo: actividades/proyectos/
    // flujos/formularios (ModuleCatalogFallback / menu Item Route).
    .AddPolicy("Tareas.Ver", p => p.RequireClaim("tenant_id")
        .AddRequirements(new Ecorex.SuperAdmin.Auth.PermissionRequirement("actividades", Ecorex.Application.Roles.PermissionAction.View)))
    .AddPolicy("Proyectos.Ver", p => p.RequireClaim("tenant_id")
        .AddRequirements(new Ecorex.SuperAdmin.Auth.PermissionRequirement("proyectos", Ecorex.Application.Roles.PermissionAction.View)))
    .AddPolicy("Flujos.Ver", p => p.RequireClaim("tenant_id")
        .AddRequirements(new Ecorex.SuperAdmin.Auth.PermissionRequirement("flujos", Ecorex.Application.Roles.PermissionAction.View)))
    // ADR-0038: la policy "MisPasos.Ver" y la pagina /mis-pasos fueron RETIRADAS. El runtime de
    // flujos vive en el detalle de la tarea; los pasos pendientes se descubren en el tablero.
    // COMPUESTA (AND): disenar formularios exige ver Y editar el modulo formularios.
    .AddPolicy("Formularios.Disenar", p => p.RequireClaim("tenant_id")
        .AddRequirements(new Ecorex.SuperAdmin.Auth.PermissionRequirement("formularios", Ecorex.Application.Roles.PermissionAction.View))
        .AddRequirements(new Ecorex.SuperAdmin.Auth.PermissionRequirement("formularios", Ecorex.Application.Roles.PermissionAction.Edit)))
    .AddPolicy("Reglas.Editar", p => p.RequireClaim("tenant_id"))
    .AddPolicy("Conceptos.Editar", p => p.RequireClaim("tenant_id"))
    .AddPolicy("Dependencias.Ver", p => p.RequireClaim("tenant_id"))
    .AddPolicy("ModulosWeb.Administrar", p => p.RequireClaim("tenant_id"))
    .AddPolicy("ExtraccionDatos.Editar", p => p.RequireClaim("tenant_id"))
    // Inventarios (grupo Sistema - Inventarios, ADR-0027): items 000066 + catalogos
    // 000556/000502/000506/000606/000498. Paso 1: mismo requisito que TenantMember.
    .AddPolicy("Inventario.Ver", p => p.RequireClaim("tenant_id"))
    // Plantillas HSM de WhatsApp (ADR-0029): editor de plantillas del CRM heredado. Paso 1:
    // mismo requisito que TenantMember (claim tenant_id). Paso 2 (Module Registry) pendiente.
    .AddPolicy("PlantillasWhatsApp.Editar", p => p.RequireClaim("tenant_id"))
    // Administrador de Menu (menu configurable por perfil, Ola 2, ADR-0030): editor de vistas
    // y nodos del menu del workspace. Paso 1: mismo requisito que TenantMember (claim tenant_id)
    // para no cambiar el acceso. TODO (paso 2): restringir a Owner/Admin del tenant (tenant_role)
    // -es una pantalla de gobierno del tenant- derivandolo del rol del TenantUser.
    // Ola 7 (endurecimiento) - policy de GOBIERNO: el editor del menu del tenant es una pantalla de
    // gobierno, se restringe a Owner/Admin del tenant (claim tenant_role), no a cualquier miembro.
    .AddPolicy("ConfiguracionMenu.Administrar", p => p.RequireClaim("tenant_role", "Owner", "Admin"))
    // Administracion de usuarios del tenant (modulo 000073, ADR-0031): CRUD de usuarios del
    // tenant (invitar, rol, estado, clave, vista de menu). Paso 1: mismo requisito que
    // TenantMember (claim tenant_id) para no cambiar el acceso. TODO (paso 2): restringir a
    // Owner/Admin del tenant (claim tenant_role) -es gobierno del tenant- derivandolo del rol.
    .AddPolicy("AdmUsuarios.Editar", p => p.RequireClaim("tenant_id"))
    // Roles y permisos (Ola B1, ADR-0032): matriz de permisos Modulo x Accion por tenant y
    // asignacion de rol a usuario. Paso 1: mismo requisito que TenantMember (claim tenant_id)
    // para no cambiar el acceso. TODO (paso 2 / Ola B2): restringir a Owner/Admin del tenant
    // (claim tenant_role) -es gobierno del tenant- y hacer cumplir el set efectivo por modulo.
    .AddPolicy("RolesPermisos.Administrar", p => p.RequireClaim("tenant_id"))
    // Ficha de empresa / administracion de tenants (modulo 000072, ADR-0026). Es GOBIERNO
    // multi-tenant: vive en el area PlatformAdmin junto a /tenants y /plans, por eso exige
    // platform_role (igual que PlatformOperator), NO tenant_id. El item 000072 del NavMenu
    // se movio del menu del tenant al area de plataforma.
    .AddPolicy("AdmEmpresas.Ver", p => p.RequireClaim("platform_role"));

// Enforcement dinamico de permisos por rol (Ola B2, ADR-0033). El policy provider materializa al
// vuelo las policies con prefijo "Perm:{moduleKey}:{action}" (gate tenant_id + PermissionRequirement)
// y DELEGA el resto en el default provider, asi que las policies existentes no cambian. El handler
// consulta ICurrentPermissions (regla opt-in: Owner/Admin y sin-rol = Unrestricted; fail-open).
builder.Services.AddScoped<Ecorex.SuperAdmin.Auth.ICurrentPermissions, Ecorex.SuperAdmin.Auth.CurrentPermissions>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, Ecorex.SuperAdmin.Auth.PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Ecorex.SuperAdmin.Auth.PermissionAuthorizationHandler>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
// Guard SSRF del modulo de extraccion (ADR-0025): en Development se permite SOLO loopback
// para que la fuente demo apunte al endpoint propio /api/demo/scrape-sample sin depender
// de internet. En produccion queda el default seguro de Infrastructure (sin loopback).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton(new Ecorex.Application.Scraping.ScrapeGuardOptions { AllowLoopback = true });
    // DataProtection en DEV: persiste las llaves a un archivo LOCAL en vez del DbContext. Dos razones:
    // (1) los reinicios del dev ya no cierran la sesion (llaves estables entre arranques); (2) el dev
    // deja de escribir su keyring en la BD, que en este proyecto es la de PROD via tunel. Solo Development;
    // en produccion queda la persistencia al DbContext que registra Infrastructure. La carpeta esta gitignored.
    builder.Services.AddDataProtection()
        .SetApplicationName("Ecorex")
        .PersistKeysToFileSystem(new System.IO.DirectoryInfo(
            System.IO.Path.Combine(builder.Environment.ContentRootPath, ".dpkeys-dev")));
}
// Contexto de tenant con soporte ambient: por cookie en peticiones, fijable en background (webhook -> agente).
builder.Services.AddScoped<ITenantContext, Ecorex.SuperAdmin.Auth.AmbientTenantContext>();

// Chat en tiempo real (SignalR): reemplaza el broadcaster no-op por el real.
builder.Services.AddSignalR();
// Agente Conector On-Prem (doc 03): esquema bearer "Agent" (no-default), registro de presencia,
// emisor de token y endpoints. No altera la auth de cookies existente.
builder.Services.AddAgentChannel(builder.Configuration);
builder.Services.AddScoped<Ecorex.Application.Tenancy.IChatBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRChatBroadcaster>();
// Nucleo de tareas en tiempo real (FASE 3): reemplaza el broadcaster no-op por el real.
builder.Services.AddScoped<Ecorex.Application.Tenancy.ITaskBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRTaskBroadcaster>();
// #4b: badge de notificaciones en vivo.
builder.Services.AddScoped<Ecorex.Application.Notifications.INotificationBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRNotificationBroadcaster>();
// Formularios-modulo (ola F4): bandeja en vivo.
builder.Services.AddScoped<Ecorex.Application.Tenancy.IFormRecordBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRFormRecordBroadcaster>();

// Atencion automatica del agente de IA por lineas de WhatsApp: lector de recursos (wwwroot) +
// despachador en background con debounce (reemplaza la cola no-op de Application).
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IAgentAssetReader, Ecorex.SuperAdmin.RealTime.WebRootAgentAssetReader>();
builder.Services.AddSingleton<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>();
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IAgentReplyQueue>(sp => sp.GetRequiredService<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>());
// Motor de programaciones (modulo 000889, ola P2): dispara las programaciones vencidas. Vive AQUI y no en
// Ecorex.Workers porque el compose de prod solo levanta este servicio (ver deploy/docker-prod).
builder.Services.AddHostedService<Ecorex.SuperAdmin.RealTime.ScheduledJobWorker>();
// Importaciones programadas (contenedor de datos): dispara los refrescos vencidos via agente y cierra
// las peticiones colgadas. Mismo motivo para vivir aqui que el worker de arriba.
builder.Services.AddHostedService<Ecorex.SuperAdmin.RealTime.ImportSchedulerWorker>();
// Tunel de desarrollo real (cloudflared); reemplaza el no-op de Application.
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IDevTunnel, Ecorex.SuperAdmin.RealTime.CloudflaredTunnel>();
// Gate por circuito que serializa el acceso al DbContext desde todos los DynamicFormRenderer del
// mismo circuito (evita "second operation on this context" cuando dos formularios cargan a la vez).
builder.Services.AddScoped<Ecorex.SuperAdmin.Services.CircuitFormGate>();
// Sembrador one-shot del agente TravelFans (ver /admin/seed-travelfans).
builder.Services.AddScoped<Ecorex.SuperAdmin.Seeders.TravelFansAgentSeeder>();
// Onboarding one-shot desde db3dev (crea tenants cliente + usuarios). Se dispara con
// ECOREX_RUN_ONBOARDING=true al arrancar; los datos sensibles (cadena db3dev, cedulas) van en
// configuracion NO versionada (appsettings.Development.local.json).
builder.Services.AddScoped<Ecorex.SuperAdmin.Seeders.LegacyOnboardingSeeder>();

var app = builder.Build();

// Detras del proxy de Railway (TLS en el borde, HTTP al contenedor): leer
// X-Forwarded-Proto/For para que Request.Scheme sea "https". Asi las cookies
// seguras del login y UseHttpsRedirection funcionan sin bucles de redireccion.
// Debe ir lo antes posible en el pipeline.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownIPNetworks.Clear(); // KnownNetworks quedo obsoleto en .NET 10 (ASPDEPR005)
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();

    // En produccion las migraciones NO se aplican solas. Si ECOREX_RUN_MIGRATIONS=true
    // (variable de Railway), aplicar las migraciones pendientes al arrancar. Es seguro
    // con una sola instancia web; el seed de demo no corre en produccion.
    if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_RUN_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
        await db.Database.MigrateAsync();
        // Asegura que el Super Admin tambien sea Owner del tenant interno "Plataforma ECOREX" para
        // que pueda usar Pipeline, Tableros y los modulos comerciales como una agencia mas. Es
        // idempotente: si el tenant interno o la membresia ya existen no hace nada. No crea datos
        // demo. Esto es lo unico del seeder que tiene sentido correr en produccion.
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        // En una BD de produccion limpia no existe ningun Super Admin (SeedAsync solo corre en
        // Development). Crearlo aqui si falta, con la clave del entorno, para que el sistema sea
        // usable tras el primer arranque sin depender de restaurar un dump previo.
        await seeder.EnsureSuperAdminAsync(Environment.GetEnvironmentVariable("ECOREX_SEED_ADMIN_PASSWORD"));
        await seeder.EnsurePlatformAdminTenantAsync();
        // Clave fuerte del Super Admin definida como secreto en la plataforma (Railway), no versionada.
        await seeder.EnsureSuperAdminPasswordAsync(Environment.GetEnvironmentVariable("ECOREX_SEED_ADMIN_PASSWORD"));
    }
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    // Guard: cuando el dev local apunta a una BD real/compartida (p.ej. produccion via tunel SSH),
    // NO sembrar datos demo. Se activa con Ecorex:SkipDemoSeed=true (en appsettings.Development
    // .local.json, gitignored) o la env var ECOREX_SKIP_DEMO_SEED=true. Migraciones y el tenant de
    // plataforma corren igual (idempotentes, sin datos demo).
    var skipDemoSeed = app.Configuration.GetValue<bool>("Ecorex:SkipDemoSeed")
        || string.Equals(Environment.GetEnvironmentVariable("ECOREX_SKIP_DEMO_SEED"), "true", StringComparison.OrdinalIgnoreCase);
    if (skipDemoSeed)
    {
        await seeder.EnsurePlatformAdminTenantAsync();
        app.Logger.LogWarning("Ecorex:SkipDemoSeed activo -> se omite la siembra de datos demo (el dev apunta a una BD real).");
    }
    else
    {
        await seeder.SeedAsync();
        await seeder.EnsurePlatformAdminTenantAsync();
        await seeder.EnsureDemoTemplateAssetsAsync();
        // Perfil de contacto de la ficha de empresa demo (modulo 000072, ADR-0026). Idempotente:
        // rellena City/Address/Phone/Email del tenant demo si quedaron vacios tras la migracion.
        await seeder.EnsureTenantProfileDemoAsync();
        // Catalogo de planes SaaS demo (Free/Pro/Empresa) para que /mi-cuenta ("Cambiar de plan")
        // tenga opciones y /plans muestre datos. Idempotente por nombre, solo Development.
        await seeder.EnsureDemoPlansAsync();
        // Modelo de datos demo con 3 tablas relacionadas (Contenedor de datos). Idempotente, solo Development.
        await seeder.EnsureDataModelDemoAsync();
        // Nucleo de tareas/proyectos demo (FASE 3, ADR-0013). Idempotente, solo Development.
        await seeder.EnsureTaskCoreDemoAsync();
        // Flujo demo del WorkflowEngine (FASE 4, ADR-0014). El motor consulta a traves del
        // filtro global de tenant, asi que la siembra fija el ambient del tenant demo.
        var workflowDemoTenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Kind == TenantKind.Demo)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();
        if (workflowDemoTenantId is Guid workflowTenantId)
        {
            using (AmbientTenantContext.Begin(workflowTenantId))
            {
                var workflowEngine = scope.ServiceProvider
                    .GetRequiredService<Ecorex.Application.Workflows.IWorkflowEngine>();
                await seeder.EnsureWorkflowDemoAsync(workflowEngine);
                // Indice de flujos del prototipo (ADR-0022): backfill de layout + un borrador
                // y una definicion pausada, para que /flujos muestre KPIs y filtros con datos.
                var workflowDesign = scope.ServiceProvider
                    .GetRequiredService<Ecorex.Application.Workflows.IWorkflowDesignService>();
                await seeder.EnsureWorkflowIndexDemoAsync(workflowEngine, workflowDesign);
            }
        }
        // Formulario dinamico demo (FASE 4 ola 2, ADR-0015): FRM-001 activo, vinculado al
        // nodo "Cotizacion" del flujo demo. Escribe con TenantId explicito (sin ambient).
        await seeder.EnsureDynamicFormsDemoAsync();
        // Constructor de formularios (ADR-0021): FRM-002 borrador y FRM-003 activo con
        // contenedores Row/Col y tabla funcional, para que el indice muestre KPIs con datos.
        await seeder.EnsureFormBuilderDemoAsync();
        // Documento de reglas demo (FASE 4 ola 3, ADR-0016): RUL-005 con PASAR_CAMPOS y
        // BLOQUEAR_CAMPO_XCONDICION (FRM-001) y ASIGNAR_CONSECUTIVO autonoma (COT-COM).
        await seeder.EnsureRulesEngineDemoAsync();
        // Modulos de sistema (FASE 5, ADR-0017): organigrama demo (Dependencias, 000850) y
        // catalogo global de modulos con todos habilitados para SKY SYSTEM (Modulos web, 000109).
        await seeder.EnsureOrgUnitsDemoAsync();
        // Asignacion por nodo (ADR-0035, ola F1): mini organigrama con clasificador
        // (Dependencia/Cargo/Funcionario) + policies WorkflowNodePolicy sobre COT-COM, para que
        // la ola F2 (bandeja) tenga datos reales que resolver. Idempotente, solo Development.
        await seeder.EnsureOrgAssignmentDemoAsync();
        // Runtime operativo de flujos demo (bandeja "mis pasos", ola F2, ADR-0036): una tarea del
        // ActivityType vinculado a COT-COM arranca una instancia Running con el paso Requerimiento
        // Pending (candidato: cargo Asesor Comercial -> operator@). Requiere ambient del tenant demo
        // (ITaskItemService consulta via el filtro global). Corre despues de la asignacion por nodo.
        if (workflowDemoTenantId is Guid runtimeTenantId)
        {
            using (AmbientTenantContext.Begin(runtimeTenantId))
            {
                var taskService = scope.ServiceProvider
                    .GetRequiredService<Ecorex.Application.Tenancy.ITaskItemService>();
                await seeder.EnsureWorkflowRuntimeDemoAsync(taskService);
                // Alineacion idempotente (ADR-0037): las condiciones del gateway demo deben coincidir
                // con el nombre de la arista (opcion de decision). Corre ANTES de resolver varados.
                await seeder.AlignDemoGatewayConditionsAsync();
                // Limpieza idempotente (ADR-0037): resuelve compuertas que quedaron varadas como
                // paso pendiente por el GAP historico del camino de formulario (heredan la decision
                // del paso previo o toman la default). Sin varados es no-op.
                var runtimeEngine = scope.ServiceProvider
                    .GetRequiredService<Ecorex.Application.Workflows.IWorkflowEngine>();
                await seeder.ResolveStuckGatewaysAsync(runtimeEngine);
            }
        }
        await seeder.EnsureModuleRegistryAsync();
        // Inventario demo (grupo Sistema - Inventarios, ADR-0027): bodegas, marcas, grupos,
        // subgrupos, tipos e items con stock por bodega e imagenes. Idempotente, solo Development.
        await seeder.EnsureInventoryDemoAsync();
        // Directorio General demo (modulo 000232): 3 empresas + 2 personas + contactos del prototipo
        // para el tenant demo. Idempotente, solo Development. Estampa TenantId explicito.
        if (workflowDemoTenantId is Guid directorioTenantId)
        {
            await seeder.EnsureDirectorioGeneralDemoAsync(directorioTenantId);
            // Gestor de Clientes demo (modulo 000740): Bolsa, oportunidades, agenda, filtros y
            // prospectos scrapeados del tenant demo. Idempotente. Corre DESPUES del Directorio
            // (reutiliza sus terceros). Solo Development. Estampa TenantId explicito.
            await seeder.EnsureGestorContactosDemoAsync(directorioTenantId);
        }
        // Conceptos de actividades demo (modulo 000270): CAT-01..CAT-04 con 1-3 subcategorias del
        // prototipo para el tenant demo. Idempotente, solo Development. El servicio lee el tenant
        // del ambient (fija el ambient del tenant demo, como el runtime de flujos).
        if (workflowDemoTenantId is Guid conceptosTenantId)
        {
            using (AmbientTenantContext.Begin(conceptosTenantId))
            {
                var conceptosSvc = scope.ServiceProvider
                    .GetRequiredService<Ecorex.Application.Actividades.IActividadCatalogoService>();
                await conceptosSvc.EnsureConceptosDemoAsync();
            }
        }
        // Menu configurable por perfil (Ola 1): vista "Completo" (1:1 del menu actual) + vista
        // "Simple" reducida + 2 usuarios (completo@ / simple@) asignados a cada vista. Idempotente.
        await seeder.EnsureMenuConfigDemoAsync();
        // Roles de permisos dinamicos (Ola B1, ADR-0032): rol de sistema "Administrador" (todos los
        // modulos) + rol demo "Asesor limitado" asignado a simple@sky-system.local. El catalogo sale
        // de los Item Ready de la vista IsDefault (por eso corre despues del seed de menu). Idempotente.
        await seeder.EnsureRolesDemoAsync();
        // Plantillas HSM de WhatsApp demo (ADR-0029): 3 plantillas del tenant demo vinculadas a una
        // linea de WhatsApp. Idempotente, solo Development. Submit es un stub (sin integracion Meta).
        await seeder.EnsureWhatsAppTemplatesDemoAsync();
        // Tableros de actividades unificados (ADR-0020): PRY-0042 con 10 tareas del prototipo
        // + 2 tableros simples para el indice. Idempotente, solo Development.
        await seeder.EnsureActivityBoardsDemoAsync();
        // Extraccion de datos (ADR-0025): 1 fuente Json demo apuntando al endpoint PROPIO
        // /api/demo/scrape-sample (sin dependencia de internet; el guard SSRF permite loopback
        // SOLO en Development). La base URL sale de la configuracion de arranque de Kestrel.
        var scrapeDemoBaseUrl = (builder.Configuration["urls"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? "http://localhost:5253").Split(';', ',')[0].Trim().TrimEnd('/');
        await seeder.EnsureScrapingDemoAsync(scrapeDemoBaseUrl);
    }
    // Backfill idempotente (ADR-0045): asegura el item "Agentes Colmena" bajo el grupo
    // "Infraestructura IA" en toda vista de menu existente. Corre tras cualquiera de las dos
    // ramas (skip/demo) porque EnsureDefaultMenuAsync no reprocesa tenants ya sembrados.
    await seeder.EnsureAgentesColmenaMenuItemAsync();
}

// Onboarding one-shot desde db3dev (ECOREX_RUN_ONBOARDING=true). Corre despues de migraciones/seed,
// en cualquier entorno, una sola vez por arranque. Idempotente. Loguea el reporte (sin claves).
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_RUN_ONBOARDING"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var onboarder = scope.ServiceProvider.GetRequiredService<Ecorex.SuperAdmin.Seeders.LegacyOnboardingSeeder>();
    var rep = await onboarder.RunAsync();
    foreach (var c in rep.Creados) { app.Logger.LogWarning("[onboarding][OK] {Msg}", c); }
    foreach (var o in rep.Omitidos) { app.Logger.LogWarning("[onboarding][skip] {Msg}", o); }
    foreach (var e in rep.Errores) { app.Logger.LogError("[onboarding][ERR] {Msg}", e); }
}

// Siembra de ejemplos del Directorio General (000232) en cada tenant de negocio
// (ECOREX_SEED_DIRECTORIO=true). Idempotente. Corre despues de migraciones; util para
// poblar prod (los tenants BITCODE / SKY SYSTEM / agrometalicas) con clientes de ejemplo.
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_SEED_DIRECTORIO"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Kind != TenantKind.Internal)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        await seeder.EnsureDirectorioGeneralDemoAsync(t.Id);
        app.Logger.LogWarning("[directorio-seed] terceros de ejemplo sembrados en tenant {Name}", t.Name);
    }
}

// Siembra de datos demo del Gestor de Clientes (000740) en cada tenant de negocio
// (ECOREX_SEED_GESTOR=true). Idempotente. Corre despues de migraciones; asegura primero los
// terceros del Directorio y luego la Bolsa/oportunidades/agenda/filtros/prospectos por tenant.
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_SEED_GESTOR"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Kind != TenantKind.Internal)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        await seeder.EnsureDirectorioGeneralDemoAsync(t.Id);
        await seeder.EnsureGestorContactosDemoAsync(t.Id);
        app.Logger.LogWarning("[gestor-seed] datos demo del Gestor de Clientes sembrados en tenant {Name}", t.Name);
    }
}

// Siembra del catalogo de Conceptos de actividades (000270) en cada tenant de negocio
// (ECOREX_SEED_CONCEPTOS=true). Idempotente. Corre despues de migraciones; util para poblar
// prod con las categorias/subcategorias de ejemplo. El servicio lee el tenant del ambient.
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_SEED_CONCEPTOS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var conceptosSvc = scope.ServiceProvider
        .GetRequiredService<Ecorex.Application.Actividades.IActividadCatalogoService>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Kind != TenantKind.Internal)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        using (AmbientTenantContext.Begin(t.Id))
        {
            await conceptosSvc.EnsureConceptosDemoAsync();
        }
        app.Logger.LogWarning("[conceptos-seed] catalogo de actividades sembrado en tenant {Name}", t.Name);
    }
}

// Siembra de los Conceptos de actividad del CRM + sus formularios (000125, Ola C) en cada tenant
// de negocio (ECOREX_SEED_CRM_CONCEPTOS=true). Idempotente por Code. Deja los 5 botones
// (Anotacion/PQR/Solicitud/Oportunidad/Cotizacion) listos en la pestana "Contacto Cliente".
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_SEED_CRM_CONCEPTOS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Kind != TenantKind.Internal)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        await seeder.EnsureCrmConceptosAsync(t.Id);
        app.Logger.LogWarning("[crm-conceptos-seed] conceptos + formularios del CRM sembrados en tenant {Name}", t.Name);
    }
}

// Reorg del menu de Inventarios: los 5 catalogos pasan a un solo item "Configuracion"
// (ECOREX_MENU_INVENTARIO=true). Idempotente. Existe porque la reconciliacion completa del menu
// no corre cuando el dev apunta a una BD real (Ecorex:SkipDemoSeed).
// A DIFERENCIA de los seeds de datos demo, aqui van TODOS los tenants incluido el interno
// (PLATAFORMA ECOREX): la seccion de inventarios debe verse igual en todas partes, y dejarla a
// medias significaba que el tenant de plataforma conservaba las 5 entradas viejas.
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_MENU_INVENTARIO"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        await seeder.EnsureInventarioConfigMenuAsync(t.Id);
        app.Logger.LogWarning("[menu-inventario] catalogos unificados en 'Configuracion' para el tenant {Name}", t.Name);
    }
}

// Siembra + backfill de las etapas CONFIGURABLES del pipeline de oportunidades (000740) en cada
// tenant de negocio (ECOREX_SEED_OPP_ESTADOS=true). Idempotente: EnsureDefaultsAsync solo siembra si
// el tenant no tiene ninguna etapa; BackfillAsync rellena EstadoId (por SortOrder == (int)Etapa) en
// las oportunidades que aun lo tienen null. El servicio lee el tenant del ambient.
if (string.Equals(Environment.GetEnvironmentVariable("ECOREX_SEED_OPP_ESTADOS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
    var estadosSvc = scope.ServiceProvider
        .GetRequiredService<Ecorex.Application.Crm.IOportunidadEstadoService>();
    var tenantIds = await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Kind != TenantKind.Internal)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    foreach (var t in tenantIds)
    {
        using (AmbientTenantContext.Begin(t.Id))
        {
            await estadosSvc.EnsureDefaultsAsync();
            await estadosSvc.BackfillAsync();
        }
        app.Logger.LogWarning("[opp-estados-seed] etapas del pipeline sembradas + backfill en tenant {Name}", t.Name);
    }
}

app.UseHttpsRedirection();
// Sirve archivos subidos en tiempo de ejecucion (logos de agencias en wwwroot/uploads).
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<Ecorex.SuperAdmin.RealTime.ChatHub>("/hubs/chat");
app.MapHub<Ecorex.SuperAdmin.RealTime.TaskHub>("/hubs/tasks");
app.MapHub<Ecorex.SuperAdmin.RealTime.NotificationHub>("/hubs/notifications");
// Agente Conector On-Prem (doc 03): hub autenticado + endpoints token/push/status.
app.MapHub<Ecorex.SuperAdmin.RealTime.AgenteHub>(AgentChannel.HubPath);
app.MapAgentEndpoints();

app.MapPost("/auth/login", async (
    HttpContext http,
    [FromForm] string email,
    [FromForm] string password,
    [FromForm] string? remember,
    IApplicationDbContext db,
    IPasswordHasher hasher) =>
{
    var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
    var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized);

    if (user is null
        || string.IsNullOrEmpty(user.PasswordHash)
        || !hasher.Verify(user.PasswordHash, password ?? string.Empty))
    {
        return Results.Redirect("/login?error=1");
    }
    // Si la clave es correcta pero la cuenta esta pendiente de activacion, redirige al flujo de activacion.
    if (user.Status == PlatformUserStatus.PendingActivation)
    {
        return Results.Redirect($"/activar?email={Uri.EscapeDataString(normalized)}&error={Uri.EscapeDataString("Activa tu cuenta antes de iniciar sesion. Te enviamos un codigo a tu correo.")}");
    }
    if (user.Status != PlatformUserStatus.Active)
    {
        return Results.Redirect("/login?error=1");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.DisplayName ?? user.Email),
        new(ClaimTypes.Email, user.Email)
    };

    string redirect;
    var isOperator = user.PlatformRole is PlatformRole platformRole;
    if (isOperator)
    {
        claims.Add(new Claim("platform_role", user.PlatformRole!.Value.ToString()));
    }

    // Membresia de agencia: la resolvemos para TODOS los usuarios (operador o no). Un operador
    // de plataforma que ademas sea miembro de un tenant (ej. el Super Admin como Owner del tenant
    // interno "Plataforma ECOREX") recibe los dos claims y puede usar tanto la consola de gobierno
    // como los modulos comerciales tenant-scoped (Pipeline, Conversaciones, etc.).
    var membership = await db.TenantUsers
        .IgnoreQueryFilters()
        .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
        .OrderBy(tu => tu.CreatedAt)
        .FirstOrDefaultAsync();

    if (!isOperator && membership is null)
    {
        // Identidad valida pero sin rol de plataforma ni membresia activa: sin acceso.
        return Results.Redirect("/login?error=1");
    }

    if (membership is not null)
    {
        claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
        claims.Add(new Claim("tenant_role", membership.TenantRole.ToString()));
    }

    // Operador de plataforma -> Dashboard; usuario de tenant -> Inicio del workspace (prototipo final).
    redirect = isOperator ? "/" : "/inicio";

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    // "Recordar sesion": checkbox marcado -> cookie PERSISTENTE (sobrevive al cierre del navegador,
    // 30 dias deslizantes). Sin marcar -> cookie de sesion (muere al cerrar), con la expiracion de 8h.
    var rememberMe = string.Equals(remember, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(remember, "on", StringComparison.OrdinalIgnoreCase);
    var authProps = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
    {
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
    };
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);
    return Results.Redirect(redirect);
}).DisableAntiforgery();

// Auto-registro (autogestion): un visitante crea su propia agencia + usuario Owner. La cuenta
// queda en PendingActivation; se envia un codigo de 6 digitos por correo y el visitante debe
// ingresarlo en /activar antes de poder iniciar sesion. La agencia nace activa sin plan.
app.MapPost("/auth/register", async (
    HttpContext http,
    [FromForm] string agencyName,
    [FromForm] string displayName,
    [FromForm] string email,
    [FromForm] string password,
    Ecorex.Application.Auth.ISelfSignupService signup) =>
{
    var result = await signup.SignUpAsync(
        new Ecorex.Application.Auth.SelfSignupRequest(agencyName, displayName, email, password));

    if (!result.Success)
    {
        var msg = Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta.");
        return Results.Redirect($"/login?mode=signup&regerror={msg}");
    }

    // No iniciamos sesion: el usuario debe activar la cuenta con el codigo enviado por correo.
    // Si la cuenta se creo pero el envio del correo fallo (SMTP mal configurado, etc.), llevamos
    // al visitante a /activar con un aviso en lugar de a /login con un error opaco. Alli puede
    // usar "Reenviar codigo" cuando el correo este disponible.
    var qs = $"email={Uri.EscapeDataString(result.Email)}";
    if (!string.IsNullOrWhiteSpace(result.EmailDeliveryWarning))
    {
        qs += $"&error={Uri.EscapeDataString(result.EmailDeliveryWarning)}";
    }
    else
    {
        qs += "&sent=1";
    }
    return Results.Redirect($"/activar?{qs}");
}).DisableAntiforgery();

// Activa la cuenta del visitante usando el codigo recibido por correo. Si es valido, inicia
// la sesion automaticamente y redirige a "Mi cuenta".
app.MapPost("/auth/activate", async (
    HttpContext http,
    [FromForm] string email,
    [FromForm] string code,
    Ecorex.Application.Auth.IAccountActivationService activation) =>
{
    var result = await activation.ActivateAsync(email, code);
    if (!result.Ok || result.PlatformUserId is null)
    {
        var msg = Uri.EscapeDataString(result.Error ?? "Codigo invalido o expirado.");
        return Results.Redirect($"/activar?email={Uri.EscapeDataString(email ?? string.Empty)}&error={msg}");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.PlatformUserId.Value.ToString()),
        new(ClaimTypes.Name, result.Email ?? string.Empty),
        new(ClaimTypes.Email, result.Email ?? string.Empty)
    };
    if (result.TenantId is { } tid)
    {
        claims.Add(new Claim("tenant_id", tid.ToString()));
        claims.Add(new Claim("tenant_role", TenantRole.Owner.ToString()));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/mi-cuenta");
}).DisableAntiforgery();

// Reenvio del codigo de activacion: invalida los codigos previos y emite uno nuevo. La respuesta
// siempre es uniforme para no revelar si el correo existe o ya esta activado.
app.MapPost("/auth/resend-activation", async (
    [FromForm] string email,
    Ecorex.Application.Auth.IAccountActivationService activation,
    Ecorex.Application.Common.IEmailSender emailSender,
    Ecorex.Application.Admin.IPlatformBrandingService branding) =>
{
    var result = await activation.ResendAsync(email);
    if (result.Ok && !string.IsNullOrEmpty(result.Code))
    {
        var brand = await branding.GetAsync();
        var html = $@"<div style=""font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:0 auto;color:#1f2937;"">
  <h2 style=""color:#4f46e5;"">{brand.PlatformName}</h2>
  <p>Aqui esta tu nuevo codigo de activacion:</p>
  <p style=""text-align:center;margin:28px 0;"">
    <span style=""display:inline-block;background:#eef2ff;color:#1e1b4b;font-size:26px;letter-spacing:6px;font-weight:bold;padding:14px 24px;border-radius:10px;border:1px solid #c7d2fe;"">{result.Code}</span>
  </p>
  <p>Este codigo vence en 24 horas y solo puede usarse una vez.</p>
</div>";
        await emailSender.SendAsync(email, $"Tu codigo de activacion - {brand.PlatformName}", html);
    }
    return Results.Redirect($"/activar?email={Uri.EscapeDataString(email ?? string.Empty)}&sent=1");
}).DisableAntiforgery();

// Recuperar contrasena (autogestion): envia un enlace de reseteo por correo. Nunca revela si el
// correo existe. El enlace usa el host de la peticion (sirve en dev y en prod tras forwarded headers).
app.MapPost("/auth/forgot", async (
    HttpContext http,
    [FromForm] string email,
    Ecorex.Application.Auth.IPasswordResetService reset) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var result = await reset.RequestAsync(email, baseUrl);
    if (!result.Success)
    {
        return Results.Redirect($"/recuperar?error={Uri.EscapeDataString(result.Error ?? "No se pudo procesar la solicitud.")}");
    }
    return Results.Redirect("/recuperar?sent=1");
}).DisableAntiforgery();

// Aplica la nueva contrasena usando el token del enlace del correo.
app.MapPost("/auth/reset", async (
    [FromForm] string token,
    [FromForm] string password,
    Ecorex.Application.Auth.IPasswordResetService reset) =>
{
    var result = await reset.ResetAsync(token, password);
    if (!result.Success)
    {
        return Results.Redirect($"/restablecer?token={Uri.EscapeDataString(token)}&error={Uri.EscapeDataString(result.Error ?? "No se pudo restablecer la contrasena.")}");
    }
    return Results.Redirect("/login?reset=1");
}).DisableAntiforgery();

// Inicia el flujo OIDC con Google: arma la URL de challenge y guarda un state (proteccion CSRF).
// Con mode=signup se recuerda el nombre de la agencia para crear el tenant al volver del callback.
app.MapGet("/connect/google", async (
    HttpContext http,
    [FromQuery] string? mode,
    [FromQuery] string? agency,
    Ecorex.Application.Auth.IGoogleSignInService google) =>
{
    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var state = Guid.NewGuid().ToString("N");
    var url = await google.BuildAuthorizeUrlAsync(redirectUri, state);
    if (url is null) { return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("El ingreso con Google no esta habilitado.")); }

    var cookieOpts = new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/"
    };
    http.Response.Cookies.Append("g_oauth_state", state, cookieOpts);

    var isSignup = string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
    if (isSignup && !string.IsNullOrWhiteSpace(agency))
    {
        http.Response.Cookies.Append("g_signup_agency", Uri.EscapeDataString(agency.Trim()), cookieOpts);
    }
    else
    {
        http.Response.Cookies.Delete("g_signup_agency");
    }
    return Results.Redirect(url);
}).AllowAnonymous();

// Callback de Google: valida el state, intercambia el code y, si el usuario existe y esta activo,
// inicia sesion por cookie. No hay auto-registro: usuarios desconocidos reciben un mensaje claro.
app.MapGet("/signin-google", async (
    HttpContext http,
    [FromQuery] string? code,
    [FromQuery] string? state,
    [FromQuery] string? error,
    Ecorex.Application.Auth.IGoogleSignInService google,
    EcorexDbContext db) =>
{
    if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("No se completo el ingreso con Google."));
    }

    var expectedState = http.Request.Cookies["g_oauth_state"];
    http.Response.Cookies.Delete("g_oauth_state");

    var signupAgencyRaw = http.Request.Cookies["g_signup_agency"];
    http.Response.Cookies.Delete("g_signup_agency");
    var signupAgency = string.IsNullOrWhiteSpace(signupAgencyRaw) ? null : Uri.UnescapeDataString(signupAgencyRaw);

    if (string.IsNullOrEmpty(state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("Sesion de ingreso invalida. Intenta de nuevo."));
    }

    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var result = await google.ResolveAsync(code, redirectUri, signupAgency);
    if (!result.Success)
    {
        // Si venia del formulario de registro, mostramos el error dentro del panel "Crear cuenta".
        if (signupAgency is not null)
        {
            return Results.Redirect("/login?mode=signup&regerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta con Google."));
        }
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo iniciar sesion con Google."));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
        new(ClaimTypes.Name, result.DisplayName ?? result.Email ?? string.Empty),
        new(ClaimTypes.Email, result.Email ?? string.Empty)
    };

    string redirect;
    var isOperator = result.PlatformRole is not null;
    if (isOperator)
    {
        claims.Add(new Claim("platform_role", result.PlatformRole!));
    }

    // Si el resultado de Google ya trae tenant_id (login de tenant), lo usamos; si no, miramos si
    // el usuario es miembro de algun tenant (caso Super Admin con tenant interno "Plataforma ECOREX").
    if (result.TenantId is { } resultTenantId)
    {
        claims.Add(new Claim("tenant_id", resultTenantId.ToString()));
        claims.Add(new Claim("tenant_role", result.TenantRole ?? TenantRole.Owner.ToString()));
    }
    else if (isOperator)
    {
        var membership = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == result.UserId && tu.Status == PlatformUserStatus.Active)
            .OrderBy(tu => tu.CreatedAt)
            .FirstOrDefaultAsync();
        if (membership is not null)
        {
            claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
            claims.Add(new Claim("tenant_role", membership.TenantRole.ToString()));
        }
    }

    // Operador de plataforma -> Dashboard; usuario de tenant -> Inicio del workspace (prototipo final).
    redirect = isOperator ? "/" : "/inicio";

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(redirect);
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// Endpoint DEMO del modulo de extraccion de datos (000730, ADR-0025): JSON estatico de
// items para que la fuente demo y los tests E2E corran sin depender de internet. Es
// AllowAnonymous a proposito: el ejecutor hace un GET sin cookies (datos ficticios, sin
// informacion de tenant). El guard SSRF solo lo alcanza via loopback en Development.
app.MapGet("/api/demo/scrape-sample", () => Results.Json(new
{
    items = new object[]
    {
        new { sku = "PIN-1001", nombre = "Pintura interior blanca 1 gl", precio = 89900, stock = 42 },
        new { sku = "PIN-1002", nombre = "Pintura interior marfil 1 gl", precio = 92500, stock = 31 },
        new { sku = "PIN-1003", nombre = "Pintura exterior gris 1 gl", precio = 104900, stock = 18 },
        new { sku = "PIN-1004", nombre = "Esmalte sintetico negro 1/4 gl", precio = 38900, stock = 77 },
        new { sku = "PIN-1005", nombre = "Vinilo tipo 1 blanco 5 gl", precio = 319000, stock = 12 },
        new { sku = "PIN-1006", nombre = "Anticorrosivo rojo 1/4 gl", precio = 27400, stock = 54 },
        new { sku = "PIN-1007", nombre = "Barniz mate 1/2 gl", precio = 61200, stock = 23 },
        new { sku = "PIN-1008", nombre = "Rodillo felpa 9 pulgadas", precio = 15900, stock = 96 }
    }
})).AllowAnonymous();

// API publica de ingestion de leads por agencia. Auth por API key (header X-Api-Key) que resuelve
// el tenant. Permite crear un lead y llenar cualquier campo del embudo desde sistemas externos.
app.MapPost("/api/public/leads", async (
    HttpRequest request,
    Ecorex.Application.Tenancy.ITenantApiService api,
    Ecorex.Application.Tenancy.ApiCreateLeadRequest body,
    CancellationToken ct) =>
{
    var apiKey = request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Json(new { error = "Falta el header X-Api-Key." }, statusCode: 401);
    }
    var tenantId = await api.ResolveTenantAsync(apiKey, ct);
    if (tenantId is null)
    {
        return Results.Json(new { error = "API key invalida o deshabilitada." }, statusCode: 401);
    }
    var result = await api.CreateLeadAsync(tenantId.Value, body, ct);
    return result.Ok
        ? Results.Json(new { ok = true, leadId = result.LeadId }, statusCode: 201)
        : Results.Json(new { ok = false, error = result.Error }, statusCode: 400);
}).AllowAnonymous().DisableAntiforgery();

// Pagina publica de la cotizacion de un lead (HTML del diseno con los datos del lead). La usa el
// boton "Ver cotizacion" y tambien el render de PDF (Chromium navega aqui). Clave: el id del lead.
app.MapGet("/cotizacion/{leadId:guid}", async (
    Guid leadId,
    [FromQuery] Guid? templateId,
    Ecorex.Application.Tenancy.IQuoteRenderService render,
    CancellationToken ct) =>
{
    var html = await render.RenderHtmlAsync(leadId, templateId, ct);
    return html is null ? Results.NotFound() : Results.Content(html, "text/html; charset=utf-8");
}).AllowAnonymous();

// PDF de la cotizacion (render headless de la pagina anterior). Para descargar/ver como PDF.
app.MapGet("/cotizacion/{leadId:guid}/pdf", async (
    Guid leadId,
    [FromQuery] Guid? templateId,
    HttpRequest httpReq,
    Ecorex.Application.Common.IQuotePdfRenderer pdf,
    CancellationToken ct) =>
{
    // Chromium corre en el MISMO contenedor que la app: navega al loopback interno (Kestrel escucha
    // en ASPNETCORE_HTTP_PORTS), no al dominio publico. El contenedor no puede alcanzar su propia URL
    // publica desde adentro (hairpin) y GoToAsync expira. La pagina /cotizacion es AllowAnonymous.
    var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0].Trim();
    var url = $"http://localhost:{port}/cotizacion/{leadId}" + (templateId is Guid t ? $"?templateId={t}" : "");
    var bytes = await pdf.RenderUrlToPdfAsync(url, ct);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf", $"cotizacion-{leadId}.pdf");
}).AllowAnonymous();

// Descarga del comprobante de pago (PDF). Solo pagos aprobados; el usuario de agencia solo
// puede descargar comprobantes de su propio tenant; el operador de plataforma puede cualquiera.
app.MapGet("/comprobante/{paymentId:guid}", async (
    Guid paymentId,
    HttpContext http,
    Ecorex.Application.Admin.IPaymentReceiptService receipts) =>
{
    var receipt = await receipts.GenerateAsync(paymentId);
    if (receipt is null)
    {
        return Results.NotFound();
    }

    var isOperator = http.User.FindFirst("platform_role") is not null;
    var ownsTenant = Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var tid) && tid == receipt.TenantId;
    if (!isOperator && !ownsTenant)
    {
        return Results.Forbid();
    }

    return Results.File(receipt.Content, "application/pdf", receipt.FileName);
}).RequireAuthorization();

// Webhook crudo de Evolution: traduce el evento, deduce el tenant del nombre de instancia,
// valida un token global y persiste el entrante (con difusion SignalR en este mismo proceso).
app.MapPost("/webhooks/evolution", async (
    HttpRequest request,
    IApplicationDbContext db,
    Ecorex.Application.Tenancy.IChatIngestService ingest,
    Ecorex.Application.Tenancy.IWhatsAppConnectorService connector,
    IWebHostEnvironment env,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    // Log de diagnostico (visible en los logs de Railway): permite saber si Evolution esta ENTREGANDO los
    // entrantes a este host y con que resultado, sin necesidad de tunel ni Bitacora. NO se loggea el contenido
    // del mensaje ni el token (regla de seguridad): solo metadata operativa (instancia, evento, resultado).
    var log = loggerFactory.CreateLogger("EvolutionWebhook");

    var master = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
    var expected = master?.WebhookToken
        ?? Environment.GetEnvironmentVariable("ECOREX_EVOLUTION_WEBHOOK_TOKEN");
    if (string.IsNullOrEmpty(expected))
    {
        log.LogWarning("Webhook Evolution recibido pero RECHAZADO: no hay token de webhook configurado.");
        return Results.StatusCode(503);
    }

    var provided = request.Headers["x-webhook-token"].ToString();
    if (string.IsNullOrEmpty(provided)) { provided = request.Query["token"].ToString(); }
    if (!string.Equals(provided, expected, StringComparison.Ordinal))
    {
        // Importante: este 401 significa que Evolution SI esta llegando a este host; lo que falla es el token.
        log.LogWarning("Webhook Evolution recibido pero RECHAZADO: token invalido o ausente.");
        return Results.Unauthorized();
    }

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var instance = doc.RootElement.TryGetProperty("instance", out var instEl) && instEl.ValueKind == System.Text.Json.JsonValueKind.String
        ? instEl.GetString() : "(sin instancia)";
    var evt = doc.RootElement.TryGetProperty("event", out var evEl) && evEl.ValueKind == System.Text.Json.JsonValueKind.String
        ? evEl.GetString() : "(sin evento)";

    var parsed = Ecorex.SuperAdmin.RealTime.EvolutionWebhookParser.Parse(doc.RootElement);
    if (parsed is null)
    {
        log.LogInformation("Webhook Evolution IGNORADO (evento no procesable). instancia={Instance} evento={Event}", instance, evt);
        return Results.Ok(new { status = "ignored" });
    }

    var payload = parsed.Payload;
    // Imagen entrante: descargamos la media (por el id del mensaje) y la guardamos como adjunto, para que
    // el agente y la consola puedan verla. Fijamos el tenant para resolver el servidor.
    if (payload.MessageType == "image" && payload.WhatsAppLineId is Guid lid)
    {
        using (Ecorex.SuperAdmin.Auth.AmbientTenantContext.Begin(parsed.TenantId))
        {
            try
            {
                var media = await connector.FetchInboundMediaAsync(lid, payload.ExternalMessageId, ct);
                if (media.Ok && !string.IsNullOrWhiteSpace(media.Base64))
                {
                    var bytes = Convert.FromBase64String(media.Base64!);
                    var mime = string.IsNullOrWhiteSpace(media.Mime) ? "image/jpeg" : media.Mime!;
                    var ext = mime.Contains("png") ? ".png" : mime.Contains("webp") ? ".webp" : ".jpg";
                    var dir = System.IO.Path.Combine(env.WebRootPath, "uploads", "chat");
                    System.IO.Directory.CreateDirectory(dir);
                    var fname = $"wa-{Guid.NewGuid():N}{ext}";
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, fname), bytes, ct);
                    payload = payload with
                    {
                        MediaType = Ecorex.Domain.Enums.MessageMediaType.Image,
                        MediaUrl = $"/uploads/chat/{fname}",
                        MediaMimeType = mime
                    };
                }
            }
            catch { /* si falla la descarga, ingerimos igual como texto "(imagen)" */ }
        }
    }

    var result = await ingest.IngestTrustedAsync(parsed.TenantId, payload, cancellationToken: ct);
    log.LogInformation("Webhook Evolution INGERIDO. instancia={Instance} tenant={Tenant} tipo={Type} resultado={Result}",
        instance, parsed.TenantId, payload.MessageType, result);
    return result == Ecorex.Application.Tenancy.ChatIngestResult.Duplicate
        ? Results.Ok(new { status = "duplicate" })
        : Results.Accepted();
}).AllowAnonymous().DisableAntiforgery();

// Webhook de Meta (WhatsApp Cloud API) - handshake de verificacion (GET).
app.MapGet("/webhooks/meta", async (HttpRequest request, IApplicationDbContext db, CancellationToken ct) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var token = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();
    var master = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
    var expected = master?.MetaWebhookVerifyToken;
    if (string.Equals(mode, "subscribe", StringComparison.Ordinal)
        && !string.IsNullOrEmpty(expected) && string.Equals(token, expected, StringComparison.Ordinal))
    {
        return Results.Text(challenge); // Meta espera el challenge en texto plano.
    }
    return Results.StatusCode(403);
}).AllowAnonymous().DisableAntiforgery();

// Webhook de Meta - mensajes entrantes (POST). Resuelve la linea por phone_number_id y reutiliza el pipeline.
app.MapPost("/webhooks/meta", async (
    HttpRequest request,
    IApplicationDbContext db,
    Ecorex.Application.Tenancy.IChatIngestService ingest,
    CancellationToken ct) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var messages = Ecorex.SuperAdmin.RealTime.MetaWebhookParser.Parse(doc.RootElement);
    if (messages.Count == 0) { return Results.Ok(new { status = "ignored" }); }

    foreach (var m in messages)
    {
        // Sin contexto de tenant aun: la linea Cloud se identifica por su phone_number_id.
        var line = await db.WhatsAppLines.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.CloudPhoneNumberId == m.PhoneNumberId
                && l.Provider == Ecorex.Domain.Enums.WhatsAppProvider.Cloud, ct);
        if (line is null) { continue; } // numero no registrado en ninguna linea

        var payload = new Ecorex.Application.Tenancy.IngestMessageRequest(
            m.Phone, m.Name, m.ExternalId, m.Body, "text", m.SentAt, line.Id);
        await ingest.IngestTrustedAsync(line.TenantId, payload, cancellationToken: ct);
    }
    return Results.Ok(new { status = "ok" });
}).AllowAnonymous().DisableAntiforgery();

// ===== Emulador de canal WhatsApp (pruebas) =====
// Inyecta un mensaje entrante en un canal SIMULADO (linea Provider=Emulator, sin nada externo) y corre
// la atencion del agente de forma SINCRONA. Toda la comunicacion (entrante, prompts, herramientas,
// respuesta) queda en la bitacora del agente y en la conversacion. Sirve para probar sin numero real.
app.MapPost("/api/test/agent", async (
    Ecorex.SuperAdmin.TestAgentRequest body,
    System.Security.Claims.ClaimsPrincipal user,
    Ecorex.Application.Common.IApplicationDbContext db,
    Ecorex.Application.Tenancy.IChatIngestService ingest,
    IServiceScopeFactory scopes,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.BadRequest(new { error = "No hay un tenant activo en la sesion." });
    }
    if (body is null || (string.IsNullOrWhiteSpace(body.Text) && string.IsNullOrWhiteSpace(body.ImageBase64)))
    {
        return Results.BadRequest(new { error = "Envia un texto o una imagen." });
    }

    var now = DateTimeOffset.UtcNow;

    // 1. Linea emulada del tenant (una sola, reutilizable).
    var line = await db.WhatsAppLines.FirstOrDefaultAsync(
        l => l.TenantId == tenantId && l.Provider == Ecorex.Domain.Enums.WhatsAppProvider.Emulator, ct);
    if (line is null)
    {
        line = new Ecorex.Domain.Entities.WhatsAppLine
        {
            TenantId = tenantId,
            InstanceName = "Canal de pruebas",
            PhoneNumber = "Emulador",
            Provider = Ecorex.Domain.Enums.WhatsAppProvider.Emulator,
            Status = Ecorex.Domain.Enums.WhatsAppLineStatus.Connected,
            LastConnectedAt = now,
            LastStatusAt = now
        };
        db.WhatsAppLines.Add(line);
        await db.SaveChangesAsync(ct);
    }
    else if (line.Status != Ecorex.Domain.Enums.WhatsAppLineStatus.Connected)
    {
        line.Status = Ecorex.Domain.Enums.WhatsAppLineStatus.Connected;
        line.LastStatusAt = now;
        await db.SaveChangesAsync(ct);
    }

    // 2. Agente a probar: el indicado o el primer agente activo.
    var agent = body.AgentId is Guid aid
        ? await db.AiAgents.FirstOrDefaultAsync(a => a.Id == aid, ct)
        : await db.AiAgents.Where(a => a.IsActive).OrderBy(a => a.SortOrder).FirstOrDefaultAsync(ct);
    if (agent is null)
    {
        return Results.BadRequest(new { error = "No hay un agente activo para probar. Enciende un agente en Agentes." });
    }
    if (!agent.IsActive)
    {
        return Results.BadRequest(new { error = "El agente elegido esta apagado; enciendelo para probarlo." });
    }

    // 3. Vincular el agente al canal emulado (una linea = un agente).
    var binding = await db.AiAgentLineBindings.FirstOrDefaultAsync(b => b.WhatsAppLineId == line.Id, ct);
    if (binding is null)
    {
        binding = new Ecorex.Domain.Entities.AiAgentLineBinding
        {
            TenantId = tenantId,
            AgentId = agent.Id,
            WhatsAppLineId = line.Id,
            IsConnected = true,
            AutoConfirm = true
        };
        db.AiAgentLineBindings.Add(binding);
    }
    else
    {
        binding.AgentId = agent.Id;
        binding.IsConnected = true;
    }
    await db.SaveChangesAsync(ct);

    // 4. Inyectar el mensaje entrante (sin encolar: corremos la atencion de inmediato).
    var phone = string.IsNullOrWhiteSpace(body.ContactPhone) ? "573001112233" : new string(body.ContactPhone.Where(char.IsDigit).ToArray());
    if (phone.Length == 0) { phone = "573001112233"; }
    var name = string.IsNullOrWhiteSpace(body.ContactName) ? "Cliente de prueba" : body.ContactName.Trim();
    var text = string.IsNullOrWhiteSpace(body.Text) ? "(foto)" : body.Text.Trim();
    var payload = new Ecorex.Application.Tenancy.IngestMessageRequest(
        phone, name, "emu-in-" + Guid.NewGuid().ToString("N"), text, "text", now, line.Id);
    await ingest.IngestTrustedAsync(tenantId, payload, enqueueDispatch: false, cancellationToken: ct);

    var conv = await db.Conversations.FirstOrDefaultAsync(
        c => c.TenantId == tenantId && c.WhatsAppLineId == line.Id && c.ContactPhone == phone, ct);
    if (conv is null) { return Results.Problem("No se pudo crear la conversacion de prueba."); }

    // Si llego una imagen, la guardamos en uploads/chat y la ingerimos como mensaje ENTRANTE de imagen,
    // para que el agente la vea (las herramientas de vision usan la ultima foto entrante de la conversacion).
    if (!string.IsNullOrWhiteSpace(body.ImageBase64))
    {
        try
        {
            var bytes = Convert.FromBase64String(body.ImageBase64!);
            var mime = string.IsNullOrWhiteSpace(body.ImageMime) ? "image/jpeg" : body.ImageMime!;
            var ext = mime.Contains("png") ? ".png" : mime.Contains("webp") ? ".webp" : ".jpg";
            var dir = System.IO.Path.Combine(env.WebRootPath, "uploads", "chat");
            System.IO.Directory.CreateDirectory(dir);
            var fname = $"emu-{Guid.NewGuid():N}{ext}";
            await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, fname), bytes, ct);
            db.Messages.Add(new Ecorex.Domain.Entities.Message
            {
                TenantId = tenantId,
                ConversationId = conv.Id,
                Direction = Ecorex.Domain.Enums.MessageDirection.Inbound,
                ExternalId = "emu-img-" + Guid.NewGuid().ToString("N"),
                Body = "",
                MessageType = "image",
                MediaType = Ecorex.Domain.Enums.MessageMediaType.Image,
                MediaUrl = $"/uploads/chat/{fname}",
                MediaMimeType = mime,
                SentAt = now.AddSeconds(1)
            });
            conv.LastMessageAt = now.AddSeconds(1);
            await db.SaveChangesAsync(ct);
        }
        catch { /* imagen invalida: seguimos solo con el texto */ }
    }

    // 5. Atender de forma sincrona (fija el tenant en el scope, igual que el despachador en background).
    using (Ecorex.SuperAdmin.Auth.AmbientTenantContext.Begin(tenantId))
    using (var scope = scopes.CreateScope())
    {
        var runner = scope.ServiceProvider.GetRequiredService<Ecorex.Application.Tenancy.IAgentConversationService>();
        await runner.RunAsync(conv.Id, ct);
    }

    // 6. Devolver la respuesta de TEXTO del agente (no los adjuntos) para inspeccion rapida.
    var reply = await db.Messages.AsNoTracking()
        .Where(m => m.ConversationId == conv.Id
            && m.Direction == Ecorex.Domain.Enums.MessageDirection.Outbound
            && m.MediaType == Ecorex.Domain.Enums.MessageMediaType.None
            && m.Body != "")
        .OrderByDescending(m => m.SentAt)
        .Select(m => m.Body)
        .FirstOrDefaultAsync(ct);

    return Results.Ok(new { conversationId = conv.Id, lineId = line.Id, agentId = agent.Id, agentName = agent.Name, reply });
}).RequireAuthorization("TenantMember").DisableAntiforgery();

app.Run();

namespace Ecorex.SuperAdmin
{
    /// <summary>Cuerpo del emulador de canal: texto del cliente + opciones de prueba + imagen opcional (base64).</summary>
    public sealed record TestAgentRequest(string? Text = null, Guid? AgentId = null, string? ContactPhone = null, string? ContactName = null, string? ImageBase64 = null, string? ImageMime = null);
}
