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

    // ---- Formulario dinamico demo (FASE 4 ola 2, ADR-0015) ----

    public const string DemoFormCode = "FRM-001";

    /// <summary>
    /// Siembra el formulario demo "Solicitud de cotizacion" (FRM-001) para el tenant demo
    /// (SKY SYSTEM): 2 contenedores y 7 preguntas Tier 1 variadas, ACTIVO, y vinculado al
    /// nodo "Cotizacion" del flujo demo COT-COM via WorkflowNodeForm (si el flujo existe).
    /// Idempotente por Code. Solo Development.
    /// </summary>
    public async Task EnsureDynamicFormsDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.FormDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == tenant.Id && d.Code == DemoFormCode, cancellationToken))
        {
            return;
        }

        var definition = new FormDefinition
        {
            TenantId = tenant.Id,
            Code = DemoFormCode,
            Title = "Solicitud de cotizacion",
            Description = "Formulario demo del paso Cotizacion del flujo COT-COM.",
            Status = FormStatus.Active,
            Revision = 1
        };
        _db.FormDefinitions.Add(definition);

        var datosCliente = new FormContainer
        {
            TenantId = tenant.Id,
            DefinitionId = definition.Id,
            Name = "Datos del solicitante",
            ContainerType = FormContainerType.Segment,
            SortOrder = 0
        };
        var detalle = new FormContainer
        {
            TenantId = tenant.Id,
            DefinitionId = definition.Id,
            Name = "Detalle de la solicitud",
            ContainerType = FormContainerType.Segment,
            SortOrder = 1
        };
        _db.FormContainers.AddRange(datosCliente, detalle);

        FormQuestion Q(FormContainer container, int order, string fieldCode, string label,
            FormControlType type, bool required, string gridCol,
            string? optionsJson = null, string? validationJson = null,
            string? caption = null, string? helpText = null, string? numeral = null)
            => new()
            {
                TenantId = tenant.Id,
                DefinitionId = definition.Id,
                ContainerId = container.Id,
                FieldCode = fieldCode,
                Label = label,
                ControlType = type,
                Required = required,
                SortOrder = order,
                GridCol = gridCol,
                OptionsJson = optionsJson,
                ValidationJson = validationJson,
                Caption = caption,
                HelpText = helpText,
                Numeral = numeral
            };

        _db.FormQuestions.AddRange(
            Q(datosCliente, 0, "nombre_solicitante", "Nombre del solicitante", FormControlType.Text,
                required: true, "col-md-6",
                validationJson: """{"minLength":3,"maxLength":120}""", numeral: "1.1"),
            Q(datosCliente, 1, "email_solicitante", "Correo electronico", FormControlType.Text,
                required: true, "col-md-6",
                validationJson: """{"pattern":"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$"}""",
                helpText: "Se usa para enviar la cotizacion.", numeral: "1.2"),
            Q(detalle, 0, "tipo_servicio", "Tipo de servicio", FormControlType.Select,
                required: true, "col-md-6",
                optionsJson: """[{"id":"licencias","label":"Licencias de software"},{"id":"desarrollo","label":"Desarrollo a la medida"},{"id":"soporte","label":"Soporte y mantenimiento"}]""",
                numeral: "2.1"),
            Q(detalle, 1, "prioridad", "Prioridad de la solicitud", FormControlType.Radio,
                required: true, "col-md-6",
                optionsJson: """[{"id":"alta","label":"Alta"},{"id":"media","label":"Media"},{"id":"baja","label":"Baja"}]""",
                numeral: "2.2"),
            Q(detalle, 2, "cantidad", "Cantidad estimada", FormControlType.Number,
                required: true, "col-md-4",
                validationJson: """{"minValue":1,"maxValue":10000}""", numeral: "2.3"),
            Q(detalle, 3, "fecha_requerida", "Fecha requerida", FormControlType.Date,
                required: false, "col-md-4", numeral: "2.4"),
            Q(detalle, 4, "acepta_contacto", "Acepta ser contactado por WhatsApp", FormControlType.Toggle,
                required: false, "col-md-4", numeral: "2.5"),
            Q(detalle, 5, "descripcion", "Descripcion de la necesidad", FormControlType.TextArea,
                required: true, "col-12",
                validationJson: """{"minLength":10,"maxLength":2000}""",
                caption: "Describe el alcance con el mayor detalle posible.", numeral: "2.6"));

        // Vinculo nodo "Cotizacion" del flujo demo COT-COM -> este formulario.
        var workflowDefinitionId = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id && d.ProcessCode == DemoWorkflowProcessCode && d.IsPublished)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (workflowDefinitionId is Guid wfId)
        {
            var cotizacionNode = await _db.WorkflowNodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.DefinitionId == wfId && n.BpmnElementId == "Task_Cotizacion", cancellationToken);
            if (cotizacionNode is not null
                && !await _db.WorkflowNodeForms.IgnoreQueryFilters()
                    .AnyAsync(f => f.NodeId == cotizacionNode.Id, cancellationToken))
            {
                _db.WorkflowNodeForms.Add(new WorkflowNodeForm
                {
                    TenantId = tenant.Id,
                    NodeId = cotizacionNode.Id,
                    DefinitionId = definition.Id
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Formulario demo {Code} sembrado para {Tenant} (2 contenedores, 8 preguntas, vinculado al nodo Cotizacion: {Linked}).",
            DemoFormCode, tenant.Name, workflowDefinitionId is not null);
    }

    // ---- Documento de reglas demo (FASE 4 ola 3, ADR-0016) ----

    public const string DemoRuleDocumentCode = "RUL-005";

    /// <summary>
    /// Siembra el documento de reglas demo "OPERACIONES DE FORMULARIOS" (RUL-005) para el
    /// tenant demo (SKY SYSTEM) con 3 reglas: PASAR_CAMPOS y BLOQUEAR_CAMPO_XCONDICION
    /// vinculadas a preguntas del formulario demo FRM-001 (FormFieldRule), y una regla
    /// ASIGNAR_CONSECUTIVO autonoma vinculada al nodo Task_Cotizacion del flujo COT-COM
    /// (WorkflowNodeRule, sin autoComplete para no saltarse el formulario del paso).
    /// Idempotente por DocumentCode. Solo Development.
    /// </summary>
    public async Task EnsureRulesEngineDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.RuleDocuments.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == tenant.Id && d.DocumentCode == DemoRuleDocumentCode, cancellationToken))
        {
            return;
        }

        var document = new RuleDocument
        {
            TenantId = tenant.Id,
            DocumentCode = DemoRuleDocumentCode,
            Name = "OPERACIONES DE FORMULARIOS",
            Category = "FORMULARIOS",
            Description = "Reglas demo del formulario FRM-001 y del flujo COT-COM (port de cl_gestion_reglas).",
            Status = RuleStatus.Active
        };
        _db.RuleDocuments.Add(document);

        var pasarCampos = new Rule
        {
            TenantId = tenant.Id,
            DocumentId = document.Id,
            Name = "Copiar solicitante a descripcion",
            Description = "Al cambiar el nombre del solicitante, copia el valor al campo descripcion.",
            VerbName = "PASAR_CAMPOS",
            SortOrder = 0,
            ParamsJson = """{"mappings":[{"source":"nombre_solicitante","target":"descripcion"}]}""",
            Status = RuleStatus.Active
        };
        var bloquearCampo = new Rule
        {
            TenantId = tenant.Id,
            DocumentId = document.Id,
            Name = "Ocultar fecha si prioridad baja",
            Description = "Si la prioridad es baja, oculta el campo fecha_requerida (opcional).",
            VerbName = "BLOQUEAR_CAMPO_XCONDICION",
            SortOrder = 1,
            ParamsJson = """{"sourceField":"prioridad","operator":"equals","value":"baja","targetField":"fecha_requerida","effect":"hide"}""",
            Status = RuleStatus.Active
        };
        var asignarConsecutivo = new Rule
        {
            TenantId = tenant.Id,
            DocumentId = document.Id,
            Name = "Consecutivo de cotizacion",
            Description = "Regla autonoma del nodo Cotizacion: emite el consecutivo COT- y lo anota en la tarea.",
            VerbName = "ASIGNAR_CONSECUTIVO",
            SortOrder = 2,
            ParamsJson = """{"sequenceCode":"RUL","prefix":"COT-","padding":5}""",
            Status = RuleStatus.Active
        };
        _db.Rules.AddRange(pasarCampos, bloquearCampo, asignarConsecutivo);

        // Vinculos a preguntas del formulario demo FRM-001 (si existe).
        var formDefinitionId = await _db.FormDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id && d.Code == DemoFormCode)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (formDefinitionId is Guid formId)
        {
            var questions = await _db.FormQuestions.IgnoreQueryFilters()
                .Where(q => q.DefinitionId == formId
                    && (q.FieldCode == "nombre_solicitante" || q.FieldCode == "prioridad"))
                .ToListAsync(cancellationToken);
            var nombre = questions.FirstOrDefault(q => q.FieldCode == "nombre_solicitante");
            var prioridad = questions.FirstOrDefault(q => q.FieldCode == "prioridad");
            if (nombre is not null)
            {
                _db.FormFieldRules.Add(new FormFieldRule
                {
                    TenantId = tenant.Id,
                    FormQuestionId = nombre.Id,
                    RuleId = pasarCampos.Id,
                    SortOrder = 0
                });
            }
            if (prioridad is not null)
            {
                _db.FormFieldRules.Add(new FormFieldRule
                {
                    TenantId = tenant.Id,
                    FormQuestionId = prioridad.Id,
                    RuleId = bloquearCampo.Id,
                    SortOrder = 0
                });
            }
        }

        // Vinculo autonomo al nodo Task_Cotizacion del flujo demo COT-COM publicado.
        var workflowDefinitionId = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id && d.ProcessCode == DemoWorkflowProcessCode && d.IsPublished)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (workflowDefinitionId is Guid wfId)
        {
            var cotizacionNode = await _db.WorkflowNodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.DefinitionId == wfId && n.BpmnElementId == "Task_Cotizacion", cancellationToken);
            if (cotizacionNode is not null)
            {
                _db.WorkflowNodeRules.Add(new WorkflowNodeRule
                {
                    TenantId = tenant.Id,
                    WorkflowNodeId = cotizacionNode.Id,
                    RuleId = asignarConsecutivo.Id,
                    SortOrder = 0,
                    IsAutonomous = true
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Documento de reglas demo {Code} sembrado para {Tenant} (3 reglas; vinculos a FRM-001: {FormLinked}, a COT-COM: {FlowLinked}).",
            DemoRuleDocumentCode, tenant.Name, formDefinitionId is not null, workflowDefinitionId is not null);
    }

    // ================= FASE 5 (ADR-0017): Dependencias + Modulos web =================

    /// <summary>
    /// Organigrama demo del tenant SKY SYSTEM (modulo Dependencias, legacy 000850): arbol de
    /// 5 unidades (Direccion General &gt; Comercial / Tecnologia &gt; Desarrollo / Gestion Humana)
    /// con el owner como responsable de la raiz y miembros demo. Idempotente por tenant.
    /// Solo Development.
    /// </summary>
    public async Task EnsureOrgUnitsDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.OrgUnits.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        var members = await _db.TenantUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        var owner = members.FirstOrDefault(u => u.Email == TenantOwnerEmail)
            ?? members.OrderBy(u => u.TenantRole == TenantRole.Owner ? 0 : 1).ThenBy(u => u.CreatedAt).FirstOrDefault();
        var admin = members.FirstOrDefault(u => u.Email == TenantAdminEmail);
        var operatorUser = members.FirstOrDefault(u => u.Email == TenantOperatorEmail);
        var viewer = members.FirstOrDefault(u => u.Email == TenantViewerEmail);
        if (owner is null) { return; }

        var direccion = new OrgUnit
        {
            TenantId = tenant.Id,
            Name = "Direccion General",
            Kind = OrgUnitKind.Area,
            ResponsibleTenantUserId = owner.Id,
            Description = "Raiz del organigrama: direccion de la compania.",
            SortOrder = 0
        };
        var comercial = new OrgUnit
        {
            TenantId = tenant.Id,
            Name = "Comercial",
            Kind = OrgUnitKind.Area,
            ParentId = direccion.Id,
            ResponsibleTenantUserId = admin?.Id,
            Description = "Gestion de relaciones comerciales, cotizaciones y ventas.",
            SortOrder = 0
        };
        var tecnologia = new OrgUnit
        {
            TenantId = tenant.Id,
            Name = "Tecnologia",
            Kind = OrgUnitKind.Area,
            ParentId = direccion.Id,
            ResponsibleTenantUserId = admin?.Id,
            Description = "Plataforma, infraestructura y desarrollo de producto.",
            SortOrder = 1
        };
        var desarrollo = new OrgUnit
        {
            TenantId = tenant.Id,
            Name = "Desarrollo",
            Kind = OrgUnitKind.Team,
            ParentId = tecnologia.Id,
            ResponsibleTenantUserId = operatorUser?.Id,
            Description = "Equipo de construccion de software.",
            SortOrder = 0
        };
        var gestionHumana = new OrgUnit
        {
            TenantId = tenant.Id,
            Name = "Gestion Humana",
            Kind = OrgUnitKind.Area,
            ParentId = direccion.Id,
            Description = "Seleccion, bienestar y nomina.",
            SortOrder = 2
        };
        _db.OrgUnits.AddRange(direccion, comercial, tecnologia, desarrollo, gestionHumana);

        void AddMember(OrgUnit unit, TenantUser? user, string role)
        {
            if (user is null) { return; }
            _db.OrgUnitMembers.Add(new OrgUnitMember
            {
                TenantId = tenant.Id,
                OrgUnitId = unit.Id,
                TenantUserId = user.Id,
                Role = role
            });
        }
        AddMember(direccion, owner, "Director general");
        AddMember(comercial, admin, "Lider comercial");
        AddMember(comercial, viewer, "Analista");
        AddMember(desarrollo, operatorUser, "Desarrollador");

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Organigrama demo sembrado para {Tenant}: 5 dependencias con responsables y miembros.", tenant.Name);
    }

    /// <summary>
    /// Catalogo GLOBAL de modulos (module registry, legacy 000109, ADR-0017) con los modulos
    /// reales del sistema, y su estado por tenant: TODOS habilitados para el tenant demo
    /// SKY SYSTEM. Idempotente por LegacyCode (upsert) y por (tenant, modulo).
    /// </summary>
    public async Task EnsureModuleRegistryAsync(CancellationToken cancellationToken = default)
    {
        // (LegacyCode, Name, Description, Route, Area, IsCore)
        (string Code, string Name, string Description, string? Route, ModuleArea Area, bool IsCore)[] catalog =
        {
            ("000038", "Actividades", "Crear una actividad (tarea) con tipo, prioridad y flujo.", "/actividades", ModuleArea.Principal, true),
            ("000042", "Proyectos", "Proyectos con equipo, tablero y avance.", "/proyectos", ModuleArea.Principal, true),
            ("000636", "Administrar actividades", "Bandeja de administracion de actividades del tenant.", "/actividades", ModuleArea.Operaciones, false),
            ("000889", "Programar actividad", "Programacion de actividades recurrentes o futuras.", "/actividades", ModuleArea.Operaciones, false),
            ("000291", "Flujos", "Motor de flujos de proceso BPMN 2.0.", "/flujos", ModuleArea.Automatizacion, false),
            ("000131", "Formularios", "Formularios dinamicos configurables sin codigo.", "/formularios", ModuleArea.Automatizacion, false),
            ("000802", "Reglas", "Motor de reglas de negocio con verbos tipados.", "/reglas", ModuleArea.Automatizacion, false),
            ("000850", "Dependencias", "Organigrama del tenant: areas, equipos y responsables.", "/dependencias", ModuleArea.Sistema, false),
            ("000109", "Modulos web", "Registro de modulos del sistema y estado por tenant.", "/modulos-web", ModuleArea.Sistema, true),
            ("000788", "Power BI", "Tableros analiticos embebidos (placeholder).", null, ModuleArea.Sistema, false),
            ("000867", "Agentes IA", "Agentes de IA gobernados por el AI Gateway (placeholder).", null, ModuleArea.Sistema, false)
        };

        var existing = await _db.ModuleDefinitions
            .ToDictionaryAsync(d => d.LegacyCode, cancellationToken);
        foreach (var item in catalog)
        {
            if (!existing.TryGetValue(item.Code, out var definition))
            {
                definition = new ModuleDefinition { LegacyCode = item.Code };
                _db.ModuleDefinitions.Add(definition);
                existing[item.Code] = definition;
            }
            definition.Name = item.Name;
            definition.Description = item.Description;
            definition.Route = item.Route;
            definition.Area = item.Area;
            definition.IsCore = item.IsCore;
        }
        await _db.SaveChangesAsync(cancellationToken);

        // Estado por tenant: todos habilitados para el tenant demo.
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        var enabledIds = await _db.TenantModules.IgnoreQueryFilters()
            .Where(tm => tm.TenantId == tenant.Id)
            .Select(tm => tm.ModuleDefinitionId)
            .ToListAsync(cancellationToken);
        var enabledSet = enabledIds.ToHashSet();
        var added = 0;
        foreach (var definition in existing.Values)
        {
            if (enabledSet.Contains(definition.Id)) { continue; }
            _db.TenantModules.Add(new TenantModule
            {
                TenantId = tenant.Id,
                ModuleDefinitionId = definition.Id,
                IsEnabled = true
            });
            added++;
        }
        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        _logger.LogInformation(
            "Catalogo de modulos sembrado ({Count} definiciones); {Added} habilitados nuevos para {Tenant}.",
            catalog.Length, added, tenant.Name);
    }

}
