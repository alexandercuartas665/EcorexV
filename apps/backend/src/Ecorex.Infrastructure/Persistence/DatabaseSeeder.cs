using Ecorex.Application.Common.Auth;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Infrastructure.Persistence;

/// <summary>
/// Siembra datos iniciales de desarrollo de forma idempotente: un Platform Admin, el plan
/// "Plan Empresa", el tenant demo "SKY SYSTEM" (replica del tenant legacy sucursal 01 = BITCODE)
/// con sus usuarios por rol y una suscripcion. Solo crea si la base esta vacia.
/// Credenciales SOLO de Development (throwaway), segun el vault del proyecto.
/// </summary>
public sealed class DatabaseSeeder
{
    public const string SuperAdminEmail = "admin@ecorex.local";
    public const string SuperAdminPassword = "Admin123*";
    public const string DemoTenantName = "SKY SYSTEM";
    public const string TenantOwnerEmail = "owner@sky-system.local";
    public const string TenantAdminEmail = "admin@sky-system.local";
    public const string TenantOperatorEmail = "operator@sky-system.local";
    public const string TenantViewerEmail = "viewer@sky-system.local";
    public const string TenantUsersPassword = "Demo123*";

    private readonly EcorexDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(EcorexDbContext db, IPasswordHasher hasher, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.PlatformUsers.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        var superAdmin = new PlatformUser
        {
            Email = SuperAdminEmail,
            EmailVerified = true,
            DisplayName = "Super Admin",
            Status = PlatformUserStatus.Active,
            PlatformRole = PlatformRole.SuperAdmin,
            PasswordHash = _hasher.Hash(SuperAdminPassword)
        };

        var plan = new SaasPlan
        {
            Name = "Plan Empresa",
            Description = "Plan de arranque para agencias pequenas.",
            MonthlyPrice = 99000m,
            YearlyPrice = 990000m,
            Currency = "COP",
            IsActive = true,
            Limits =
            [
                new SaasPlanLimit { LimitKey = "max_users", LimitValue = 10, LimitUnit = "users", EnforcementMode = LimitEnforcementMode.Hard },
                new SaasPlanLimit { LimitKey = "max_whatsapp_lines", LimitValue = 2, LimitUnit = "lines", EnforcementMode = LimitEnforcementMode.Hard },
                new SaasPlanLimit { LimitKey = "max_ai_tokens_monthly", LimitValue = 100000, LimitUnit = "tokens", EnforcementMode = LimitEnforcementMode.Soft }
            ]
        };

        // Tenant demo SKY SYSTEM: replica del tenant legacy sucursal 01 = BITCODE.
        var tenant = new Tenant
        {
            Name = DemoTenantName,
            LegalName = "SKY SYSTEM SAS",
            TaxId = "900123456-7",
            Country = "CO",
            Currency = "COP",
            Status = TenantStatus.Active,
            Kind = TenantKind.Demo
        };

        // Usuarios del tenant demo por rol, segun el vault. El enum TenantRole actual solo tiene
        // Owner/Admin/Supervisor/Advisor: Operator y Viewer se mapean a Advisor.
        // TODO: cuando TenantRole tenga roles Operator/Viewer (o equivalentes), ajustar este mapeo.
        (string Email, string DisplayName, TenantRole Role)[] tenantMembers =
        {
            (TenantOwnerEmail, "Owner SKY SYSTEM", TenantRole.Owner),
            (TenantAdminEmail, "Admin SKY SYSTEM", TenantRole.Admin),
            (TenantOperatorEmail, "Operator SKY SYSTEM", TenantRole.Advisor),
            (TenantViewerEmail, "Viewer SKY SYSTEM", TenantRole.Advisor)
        };

        _db.PlatformUsers.Add(superAdmin);
        _db.SaasPlans.Add(plan);
        _db.Tenants.Add(tenant);

        _db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            BillingFrequency = BillingFrequency.Monthly,
            StartsAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
        });

        foreach (var (email, displayName, role) in tenantMembers)
        {
            var member = new PlatformUser
            {
                Email = email,
                EmailVerified = true,
                DisplayName = displayName,
                Status = PlatformUserStatus.Active,
                PasswordHash = _hasher.Hash(TenantUsersPassword)
            };
            _db.PlatformUsers.Add(member);
            _db.TenantUsers.Add(new TenantUser
            {
                TenantId = tenant.Id,
                PlatformUserId = member.Id,
                Email = email,
                TenantRole = role,
                Status = PlatformUserStatus.Active
            });
        }

        _db.TenantConfigurations.AddRange(
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "tono", ConfigValue = "cordial" },
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "horario", ConfigValue = "8-18" });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Seed inicial creado. Platform Admin: {SuperAdmin} / {SuperPass}. Tenant {Tenant}: owner/admin/operator/viewer@sky-system.local / {TenantPass}",
            SuperAdminEmail, SuperAdminPassword, DemoTenantName, TenantUsersPassword);
    }

    /// <summary>
    /// Fija la clave del Super Admin a partir de un valor provisto por el entorno (ECOREX_SEED_ADMIN_PASSWORD
    /// en Railway). Sirve para que en produccion el super admin tenga una clave FUERTE sin versionarla ni
    /// pasarla en claro: el operador la define como secreto en la plataforma y aqui solo se hashea. Es
    /// idempotente y seguro de correr en cada arranque. No hace nada si el valor es vacio.
    /// </summary>
    public async Task EnsureSuperAdminPasswordAsync(string? newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) { return; }
        var superAdmin = await _db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PlatformRole == PlatformRole.SuperAdmin && u.Status == PlatformUserStatus.Active, cancellationToken);
        if (superAdmin is null) { return; }

        var pwd = newPassword.Trim();
        // Si la clave actual ya coincide, no reescribir (evita un update por cada arranque).
        if (!string.IsNullOrEmpty(superAdmin.PasswordHash) && _hasher.Verify(superAdmin.PasswordHash, pwd)) { return; }
        superAdmin.PasswordHash = _hasher.Hash(pwd);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Clave del Super Admin {Email} actualizada desde el entorno.", superAdmin.Email);
    }

    /// <summary>
    /// Asegura que el Super Admin (admin@ecorex.local) tambien sea Owner de un tenant interno
    /// "Plataforma ECOREX". Asi el Super Admin puede usar Pipeline y los modulos comerciales como
    /// si fuera una agencia mas, sin perder su rol de gobierno de la plataforma. Idempotente: si
    /// el tenant interno o la membresia ya existen, no hace nada.
    /// </summary>
    public async Task EnsurePlatformAdminTenantAsync(CancellationToken cancellationToken = default)
    {
        var superAdmin = await _db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PlatformRole == PlatformRole.SuperAdmin && u.Status == PlatformUserStatus.Active, cancellationToken);
        if (superAdmin is null) { return; }

        var platformTenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Internal, cancellationToken);
        if (platformTenant is null)
        {
            platformTenant = new Tenant
            {
                Name = "Plataforma ECOREX",
                LegalName = "ECOREX.tareas SAS",
                Country = "CO",
                Currency = "COP",
                Status = TenantStatus.Active,
                Kind = TenantKind.Internal
            };
            _db.Tenants.Add(platformTenant);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Tenant interno 'Plataforma ECOREX' creado para el Super Admin (id={Id}).", platformTenant.Id);
        }

        var membership = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(tu => tu.TenantId == platformTenant.Id && tu.PlatformUserId == superAdmin.Id, cancellationToken);
        if (membership is null)
        {
            _db.TenantUsers.Add(new TenantUser
            {
                TenantId = platformTenant.Id,
                PlatformUserId = superAdmin.Id,
                Email = superAdmin.Email,
                TenantRole = TenantRole.Owner,
                Status = PlatformUserStatus.Active
            });
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Super Admin {Email} agregado como Owner del tenant interno.", superAdmin.Email);
        }
    }

    // Recursos de ejemplo (imagenes) de la galeria de plantillas para la agencia demo. Idempotente:
    // solo registra si la agencia aun no tiene recursos. Se llama en cada arranque de Desarrollo.
    public async Task EnsureDemoTemplateAssetsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.TemplateAssets.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        (string name, string file)[] assets =
        {
            ("Logo agencia", "demo-logo.svg"),
            ("Hotel (foto)", "demo-hotel.svg"),
            ("Avianca (aerolinea)", "demo-avianca.svg"),
            ("Icono Vuelos", "demo-icon-vuelo.svg"),
            ("Icono Traslados", "demo-icon-traslado.svg"),
            ("Icono Hotel", "demo-icon-hotel.svg"),
            ("Icono Asistencia", "demo-icon-salud.svg")
        };
        foreach (var (name, file) in assets)
        {
            _db.TemplateAssets.Add(new TemplateAsset
            {
                TenantId = tenant.Id,
                FileName = name,
                Url = $"/uploads/templates/{file}",
                MimeType = "image/svg+xml",
                SizeBytes = 600
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Recursos demo de la galeria de plantillas registrados ({Count}).", assets.Length);
    }

    /// <summary>
    /// Datos demo del nucleo de tareas/proyectos (FASE 3, ADR-0013) para el tenant demo
    /// SKY SYSTEM: tipos de actividad, etiquetas por tenant, proyecto PRJ-001 y 5 tareas
    /// variadas (una con worklog y comentarios). Idempotente por tabla vacia (guard por
    /// tenant en cada bloque). Solo Development.
    /// </summary>
    public async Task EnsureTaskCoreDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // Owner del proyecto demo: el owner de SKY SYSTEM; si la base dev tiene un tenant demo
        // anterior (sin esos correos), cae al primer usuario con rol Owner (o al primero que haya).
        var owner = await _db.TenantUsers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == TenantOwnerEmail, cancellationToken)
            ?? await _db.TenantUsers.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenant.Id)
                .OrderBy(u => u.TenantRole == TenantRole.Owner ? 0 : 1).ThenBy(u => u.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        var operatorUser = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == TenantOperatorEmail, cancellationToken);
        if (owner is null) { return; }

        // ---- Tipos de actividad ----
        var activityTypes = new List<ActivityType>();
        if (!await _db.ActivityTypes.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id, cancellationToken))
        {
            (string Category, string Name)[] types =
            {
                ("Direccion Comercial", "Cotizacion"),
                ("Direccion Comercial", "Seguimiento"),
                ("Direccion General", "Requerimiento"),
                ("Gestion Humana", "Solicitud")
            };
            for (int i = 0; i < types.Length; i++)
            {
                activityTypes.Add(new ActivityType
                {
                    TenantId = tenant.Id,
                    Category = types[i].Category,
                    Name = types[i].Name,
                    SortOrder = i
                });
            }
            _db.ActivityTypes.AddRange(activityTypes);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            activityTypes = await _db.ActivityTypes.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenant.Id)
                .OrderBy(t => t.SortOrder)
                .ToListAsync(cancellationToken);
        }

        // ---- Etiquetas por tenant ----
        var tags = new List<TaskItemTag>();
        if (!await _db.TaskItemTags.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id, cancellationToken))
        {
            (string Name, string Color)[] tagDefs =
            {
                ("#urgente", "#ef4444"),
                ("#proveedor", "#3b82f6"),
                ("#facturacion", "#22c55e")
            };
            foreach (var (name, color) in tagDefs)
            {
                tags.Add(new TaskItemTag { TenantId = tenant.Id, Name = name, Color = color });
            }
            _db.TaskItemTags.AddRange(tags);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            tags = await _db.TaskItemTags.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenant.Id)
                .OrderBy(t => t.Name)
                .ToListAsync(cancellationToken);
        }

        // ---- Proyecto demo ----
        Project? project;
        if (!await _db.Projects.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant.Id, cancellationToken))
        {
            project = new Project
            {
                TenantId = tenant.Id,
                Code = "PRJ-001",
                Name = "Implantacion ECOREX",
                Description = "Proyecto demo de implantacion del sistema de tareas.",
                Status = ProjectStatus.Active,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                OwnerTenantUserId = owner.Id
            };
            _db.Projects.Add(project);
            if (operatorUser is not null)
            {
                _db.ProjectMembers.Add(new ProjectMember
                {
                    TenantId = tenant.Id,
                    ProjectId = project.Id,
                    TenantUserId = operatorUser.Id,
                    CanEdit = true
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            project = await _db.Projects.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenant.Id, cancellationToken);
        }

        // ---- Tareas demo (con consecutivo + secuencia coherente) ----
        if (await _db.TaskItems.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var typeAt = (int i) => activityTypes[i % activityTypes.Count];
        var tagAt = (string name) => tags.FirstOrDefault(t => t.Name == name);

        (string Title, int TypeIdx, TaskPriority Priority, TaskItemStatus Status, Guid? Assignee,
         DateTimeOffset? Due, Guid? ProjectId, string? TagName)[] taskDefs =
        {
            ("Cotizar renovacion de licencias", 0, TaskPriority.High, TaskItemStatus.InProgress,
                owner.Id, now.AddDays(2), project?.Id, "#urgente"),
            ("Seguimiento a cliente Alfa", 1, TaskPriority.Medium, TaskItemStatus.Active,
                operatorUser?.Id ?? owner.Id, now.AddDays(5), null, "#proveedor"),
            ("Requerimiento de tablero gerencial", 2, TaskPriority.Medium, TaskItemStatus.Pending,
                null, now.AddDays(10), project?.Id, null),
            ("Solicitud de vacaciones equipo", 3, TaskPriority.Low, TaskItemStatus.Suspended,
                operatorUser?.Id ?? owner.Id, null, null, null),
            ("Conciliar facturacion de junio", 0, TaskPriority.High, TaskItemStatus.Done,
                owner.Id, now.AddDays(-1), project?.Id, "#facturacion")
        };

        var createdTasks = new List<TaskItem>();
        for (int i = 0; i < taskDefs.Length; i++)
        {
            var def = taskDefs[i];
            var task = new TaskItem
            {
                TenantId = tenant.Id,
                Number = "T" + (i + 1).ToString().PadLeft(5, '0'),
                Title = def.Title,
                ActivityTypeId = typeAt(def.TypeIdx).Id,
                Priority = def.Priority,
                Status = def.Status,
                AssigneeTenantUserId = def.Assignee,
                DueDate = def.Due,
                ProjectId = def.ProjectId,
                RequesterName = i == 0 ? "Cliente Alfa SAS" : null,
                RequesterEmail = i == 0 ? "compras@cliente-alfa.example" : null
            };
            createdTasks.Add(task);
            _db.TaskItems.Add(task);
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = tenant.Id,
                TaskItemId = task.Id,
                Type = TaskActivityType.Action,
                ActorUserId = owner.PlatformUserId,
                ActorName = "Owner SKY SYSTEM",
                Text = $"creo la tarea {task.Number}"
            });
            if (def.TagName is not null && tagAt(def.TagName) is TaskItemTag tag)
            {
                _db.TaskItemTagAssignments.Add(new TaskItemTagAssignment
                {
                    TenantId = tenant.Id,
                    TaskItemId = task.Id,
                    TagId = tag.Id
                });
            }
        }

        // La primera tarea lleva worklog y comentarios de ejemplo.
        var richTask = createdTasks[0];
        _db.TaskWorkLogs.Add(new TaskWorkLog
        {
            TenantId = tenant.Id,
            TaskItemId = richTask.Id,
            TenantUserId = owner.Id,
            Seconds = 3600,
            Note = "Revision inicial de la cotizacion",
            Kind = WorkLogKind.Manual,
            LoggedAt = now.AddHours(-4)
        });
        _db.TaskWorkLogs.Add(new TaskWorkLog
        {
            TenantId = tenant.Id,
            TaskItemId = richTask.Id,
            TenantUserId = owner.Id,
            Seconds = 1800,
            Note = "Llamada con el proveedor",
            Kind = WorkLogKind.Timer,
            LoggedAt = now.AddHours(-2)
        });
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = tenant.Id,
            TaskItemId = richTask.Id,
            Type = TaskActivityType.Comment,
            ActorUserId = owner.PlatformUserId,
            ActorName = "Owner SKY SYSTEM",
            Text = "El proveedor envia la propuesta el jueves."
        });
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = tenant.Id,
            TaskItemId = richTask.Id,
            Type = TaskActivityType.Comment,
            ActorUserId = operatorUser?.PlatformUserId,
            ActorName = "Operator SKY SYSTEM",
            Text = "Confirmado: incluir soporte extendido en la cotizacion."
        });

        // Secuencia coherente con los numeros sembrados (proximo: T00006).
        if (!await _db.TenantSequences.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenant.Id && s.Code == "T05", cancellationToken))
        {
            _db.TenantSequences.Add(new TenantSequence
            {
                TenantId = tenant.Id,
                Code = "T05",
                NextValue = taskDefs.Length + 1
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seed del nucleo de tareas creado para {Tenant}: {Types} tipos, {Tags} etiquetas, 1 proyecto, {Tasks} tareas.",
            tenant.Name, activityTypes.Count, tags.Count, taskDefs.Length);
    }

    // ---- Flujo demo del WorkflowEngine (FASE 4, ADR-0014) ----

    public const string DemoWorkflowProcessCode = "COT-COM";
    public const string DemoWorkflowName = "Cotizacion Comercial";

    /// <summary>
    /// XML BPMN 2.0 estandar del flujo demo: start -> Requerimiento -> Cotizacion ->
    /// gateway Aprobacion (Approved -> Facturacion -> Entrega -> end; Rejected -> endEvent
    /// de reinicio, cuyo RestartNodeId se configura tras importar porque los reinicios no
    /// forman parte del estandar BPMN). Compatible con bpmn.io (sin extensiones).
    /// </summary>
    private const string DemoWorkflowBpmnXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="ecorex-demo-cotizacion" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="Process_CotizacionComercial" isExecutable="false">
            <bpmn:startEvent id="Start_Inicio" name="Inicio" />
            <bpmn:task id="Task_Requerimiento" name="Requerimiento" />
            <bpmn:task id="Task_Cotizacion" name="Cotizacion" />
            <bpmn:exclusiveGateway id="Gateway_Aprobacion" name="Aprobacion" />
            <bpmn:task id="Task_Facturacion" name="Facturacion" />
            <bpmn:task id="Task_Entrega" name="Entrega" />
            <bpmn:endEvent id="End_Fin" name="Fin" />
            <bpmn:endEvent id="End_Reinicio" name="Rechazada: reinicia cotizacion" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_Inicio" targetRef="Task_Requerimiento" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_Requerimiento" targetRef="Task_Cotizacion" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="Task_Cotizacion" targetRef="Gateway_Aprobacion" />
            <bpmn:sequenceFlow id="Flow_4" name="Aprobada" sourceRef="Gateway_Aprobacion" targetRef="Task_Facturacion">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Approved'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_5" name="Rechazada" sourceRef="Gateway_Aprobacion" targetRef="End_Reinicio">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Rejected'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_6" sourceRef="Task_Facturacion" targetRef="Task_Entrega" />
            <bpmn:sequenceFlow id="Flow_7" sourceRef="Task_Entrega" targetRef="End_Fin" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Siembra el flujo demo "Cotizacion Comercial" para el tenant demo (SKY SYSTEM) usando
    /// el propio motor (ImportBpmnAsync + SetRestartTargetAsync + PublishAsync) y vincula el
    /// ActivityType "Direccion Comercial/Cotizacion" a la definicion. Idempotente por
    /// ProcessCode. REQUIERE tenant activo en el ITenantContext del scope (el motor consulta
    /// a traves del filtro global): el llamador debe fijar el ambient del tenant demo.
    /// Solo Development.
    /// </summary>
    public async Task EnsureWorkflowDemoAsync(IWorkflowEngine engine, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.WorkflowDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == tenant.Id && d.ProcessCode == DemoWorkflowProcessCode, cancellationToken))
        {
            return;
        }

        var imported = await engine.ImportBpmnAsync(new ImportBpmnRequest(
            DemoWorkflowProcessCode, DemoWorkflowName, DemoWorkflowBpmnXml,
            "Flujo demo de cotizacion comercial con aprobacion y reinicio por rechazo."), cancellationToken);
        if (!imported.IsOk)
        {
            _logger.LogWarning("No se pudo sembrar el flujo demo: {Error}", imported.Error);
            return;
        }
        var definition = imported.Value!;

        // Reinicio (no es parte del XML BPMN estandar): el endEvent "Rechazada" reabre la
        // Cotizacion en un ciclo nuevo (CycleIndex+1).
        var restartTrigger = definition.Nodes.First(n => n.BpmnElementId == "End_Reinicio");
        var restartTarget = definition.Nodes.First(n => n.BpmnElementId == "Task_Cotizacion");
        await engine.SetRestartTargetAsync(restartTrigger.Id, restartTarget.Id, cancellationToken);

        await engine.PublishAsync(definition.Id, cancellationToken);

        // Las tareas nuevas de "Direccion Comercial/Cotizacion" arrancan este flujo.
        var activityType = await _db.ActivityTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenant.Id
                && t.Category == "Direccion Comercial" && t.Name == "Cotizacion", cancellationToken);
        if (activityType is not null)
        {
            activityType.WorkflowDefinitionId = definition.Id;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Flujo demo {Process} v{Version} sembrado y publicado para {Tenant} ({Nodes} nodos, {Edges} aristas).",
            DemoWorkflowProcessCode, definition.Version, tenant.Name,
            definition.Nodes.Count, definition.Edges.Count);
    }

}
