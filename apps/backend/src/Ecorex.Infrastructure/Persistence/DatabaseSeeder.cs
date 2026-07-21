using Ecorex.Application.Common.Auth;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Application.MenuConfig;
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
public sealed class DatabaseSeeder : IMenuProvisioningService
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
            // Perfil de contacto/domicilio de la ficha de empresa (modulo 000072, ADR-0026).
            City = "Bogota",
            Address = "Cra 68 #23-45, Puente Aranda",
            Phone = "+57 601 234 5678",
            Email = "contacto@sky-system.local",
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
    /// Crea el Super Admin base (admin@ecorex.local) si aun no existe ninguno, usando la clave
    /// entregada como secreto del entorno (ECOREX_SEED_ADMIN_PASSWORD). Pensado para el arranque en
    /// Production, donde SeedAsync (que crea el admin + datos demo) NO corre. Idempotente: si ya hay
    /// un SuperAdmin no hace nada. Si la clave viene vacia tampoco crea (no se crea admin sin credencial).
    /// </summary>
    public async Task EnsureSuperAdminAsync(string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password)) { return; }
        if (await _db.PlatformUsers.IgnoreQueryFilters()
                .AnyAsync(u => u.PlatformRole == PlatformRole.SuperAdmin, cancellationToken))
        {
            return;
        }

        _db.PlatformUsers.Add(new PlatformUser
        {
            Email = SuperAdminEmail,
            EmailVerified = true,
            DisplayName = "Super Admin",
            Status = PlatformUserStatus.Active,
            PlatformRole = PlatformRole.SuperAdmin,
            PasswordHash = _hasher.Hash(password.Trim())
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Super Admin {Email} creado en el arranque de produccion.", SuperAdminEmail);
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
    /// Rellena el perfil de contacto/domicilio de la ficha de empresa (modulo 000072, ADR-0026)
    /// del tenant demo SKY SYSTEM cuando la BD ya existia antes de la migracion AddTenantProfile
    /// (los campos nuevos quedan null en filas previas). Idempotente: solo escribe los campos
    /// que esten vacios, nunca pisa datos ya cargados. Cross-tenant acotado al tenant demo.
    /// </summary>
    public async Task EnsureTenantProfileDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        var changed = false;
        if (string.IsNullOrWhiteSpace(tenant.City)) { tenant.City = "Bogota"; changed = true; }
        if (string.IsNullOrWhiteSpace(tenant.Address)) { tenant.Address = "Cra 68 #23-45, Puente Aranda"; changed = true; }
        if (string.IsNullOrWhiteSpace(tenant.Phone)) { tenant.Phone = "+57 601 234 5678"; changed = true; }
        if (string.IsNullOrWhiteSpace(tenant.Email)) { tenant.Email = "contacto@sky-system.local"; changed = true; }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Perfil de contacto del tenant demo (ficha 000072) completado.");
        }
    }

    /// <summary>
    /// Catalogo de planes SaaS demo (Free / Pro / Empresa) para que /mi-cuenta ("Cambiar de plan")
    /// tenga opciones reales que seleccionar y /plans muestre un catalogo con datos. Los planes son
    /// GLOBALES (no tenant-scoped: es el catalogo de la plataforma). Idempotente por Nombre: solo
    /// agrega los que falten. Solo Development (en prod el super admin define sus planes reales via
    /// /plans; este metodo NO corre bajo Ecorex:SkipDemoSeed).
    /// </summary>
    public async Task EnsureDemoPlansAsync(CancellationToken cancellationToken = default)
    {
        var existentes = await _db.SaasPlans.IgnoreQueryFilters()
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);
        var yaHay = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);

        var catalogo = new[]
        {
            new SaasPlan
            {
                Name = "Plan Free",
                Description = "Para empezar: lo esencial para un equipo pequeno.",
                MonthlyPrice = 0m,
                YearlyPrice = 0m,
                Currency = "COP",
                IsActive = true,
                Limits =
                [
                    new SaasPlanLimit { LimitKey = "max_users", LimitValue = 3, LimitUnit = "users", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_whatsapp_lines", LimitValue = 1, LimitUnit = "lines", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_ai_tokens_monthly", LimitValue = 10000, LimitUnit = "tokens", EnforcementMode = LimitEnforcementMode.Soft }
                ]
            },
            new SaasPlan
            {
                Name = "Plan Pro",
                Description = "Para equipos en crecimiento: mas usuarios, lineas e IA.",
                MonthlyPrice = 49000m,
                YearlyPrice = 490000m,
                Currency = "COP",
                IsActive = true,
                Limits =
                [
                    new SaasPlanLimit { LimitKey = "max_users", LimitValue = 25, LimitUnit = "users", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_whatsapp_lines", LimitValue = 5, LimitUnit = "lines", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_ai_tokens_monthly", LimitValue = 500000, LimitUnit = "tokens", EnforcementMode = LimitEnforcementMode.Soft }
                ]
            },
            new SaasPlan
            {
                Name = "Plan Empresa",
                Description = "Para operaciones exigentes: limites altos y soporte.",
                MonthlyPrice = 99000m,
                YearlyPrice = 990000m,
                Currency = "COP",
                IsActive = true,
                Limits =
                [
                    new SaasPlanLimit { LimitKey = "max_users", LimitValue = 100, LimitUnit = "users", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_whatsapp_lines", LimitValue = 20, LimitUnit = "lines", EnforcementMode = LimitEnforcementMode.Hard },
                    new SaasPlanLimit { LimitKey = "max_ai_tokens_monthly", LimitValue = 2000000, LimitUnit = "tokens", EnforcementMode = LimitEnforcementMode.Soft }
                ]
            }
        };

        var nuevos = catalogo.Where(p => !yaHay.Contains(p.Name)).ToList();
        if (nuevos.Count == 0) { return; }

        _db.SaasPlans.AddRange(nuevos);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Catalogo de planes demo: agregados {Count} planes ({Nombres}).",
            nuevos.Count, string.Join(", ", nuevos.Select(p => p.Name)));
    }

    /// <summary>
    /// Modelo de datos demo "Ventas (demo)" con 3 tablas RELACIONADAS para que el Contenedor de datos
    /// muestre relaciones ER de entrada (sin esto el modelo demo tenia 1 tabla y el selector "Tabla
    /// destino" salia vacio). Tablas: Clientes, Productos, Pedidos. Relaciones (derivadas de columnas de
    /// tipo Reference/RelationMany): Pedidos.Cliente -> Clientes (N:1) y Pedidos.Productos <-> Productos
    /// (N:N). Idempotente por nombre de modelo. Solo Development.
    /// </summary>
    public async Task EnsureDataModelDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.DataModels.IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenant.Id && m.Name == "Ventas (demo)", cancellationToken))
        {
            return;
        }

        var model = new DataModel
        {
            TenantId = tenant.Id,
            Name = "Ventas (demo)",
            Description = "Ejemplo de 3 tablas relacionadas (modelo ER)."
        };

        // Las 3 tablas, ya con posicion en el lienzo (Clientes/Productos a la izquierda, Pedidos al centro).
        var clientes = new DataContainer { TenantId = tenant.Id, ModelId = model.Id, Name = "Clientes", CanvasX = 60, CanvasY = 60 };
        var productos = new DataContainer { TenantId = tenant.Id, ModelId = model.Id, Name = "Productos", CanvasX = 60, CanvasY = 360 };
        var pedidos = new DataContainer { TenantId = tenant.Id, ModelId = model.Id, Name = "Pedidos", CanvasX = 440, CanvasY = 200 };

        // Columnas: SOLO tipos de dato escalares. Las relaciones van aparte (DataModelRelation).
        DataContainerColumn Col(DataContainer c, string name, DataContainerColumnType type, int order, bool req = false)
            => new() { TenantId = tenant.Id, ContainerId = c.Id, Name = name, Type = type, SortOrder = order, IsRequired = req };

        clientes.Columns = new List<DataContainerColumn>
        {
            Col(clientes, "Nombre", DataContainerColumnType.Text, 0, req: true),
            Col(clientes, "Email", DataContainerColumnType.Text, 1),
            Col(clientes, "Ciudad", DataContainerColumnType.Text, 2)
        };
        productos.Columns = new List<DataContainerColumn>
        {
            Col(productos, "Nombre", DataContainerColumnType.Text, 0, req: true),
            Col(productos, "Precio", DataContainerColumnType.Decimal, 1),
            Col(productos, "Stock", DataContainerColumnType.Number, 2)
        };
        pedidos.Columns = new List<DataContainerColumn>
        {
            Col(pedidos, "Fecha", DataContainerColumnType.Date, 0, req: true),
            Col(pedidos, "Total", DataContainerColumnType.Decimal, 1)
        };

        model.Tables = new List<DataContainer> { clientes, productos, pedidos };
        _db.DataModels.Add(model);

        // Relaciones como aristas del ER (ortogonales a los tipos de dato):
        _db.DataModelRelations.AddRange(
            // N:1 (morada): un pedido pertenece a un cliente.
            new DataModelRelation { TenantId = tenant.Id, ModelId = model.Id, FromTableId = pedidos.Id, ToTableId = clientes.Id, Kind = DataModelRelationKind.ManyToOne, Name = "Cliente" },
            // N:N (naranja punteada): un pedido lleva varios productos y un producto va en varios pedidos.
            new DataModelRelation { TenantId = tenant.Id, ModelId = model.Id, FromTableId = pedidos.Id, ToTableId = productos.Id, Kind = DataModelRelationKind.ManyToMany, Name = "Productos" });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Modelo de datos demo 'Ventas (demo)' sembrado: 3 tablas + 2 relaciones (aristas ER).");
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

        // ---- Hitos demo del proyecto (Proyectos P1) ----
        if (project is not null &&
            !await _db.ProjectMilestones.IgnoreQueryFilters().AnyAsync(m => m.ProjectId == project.Id, cancellationToken))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            _db.ProjectMilestones.AddRange(
                new ProjectMilestone { TenantId = tenant.Id, ProjectId = project.Id, Name = "Kickoff y alcance", DueDate = today.AddDays(7), SortOrder = 0 },
                new ProjectMilestone { TenantId = tenant.Id, ProjectId = project.Id, Name = "Puesta en produccion", DueDate = today.AddDays(30), SortOrder = 1 });
            // Proyectos P2: presupuesto/costos + DOFA demo.
            _db.ProjectBudgetItems.AddRange(
                new ProjectBudgetItem { TenantId = tenant.Id, ProjectId = project.Id, Name = "Licencias y software", Category = "Software", PlannedAmount = 5000, ActualAmount = 4800, SortOrder = 0 },
                new ProjectBudgetItem { TenantId = tenant.Id, ProjectId = project.Id, Name = "Consultoria de implantacion", Category = "Servicios", PlannedAmount = 8000, ActualAmount = 9200, SortOrder = 1 });
            _db.ProjectDofas.AddRange(
                new ProjectDofa { TenantId = tenant.Id, ProjectId = project.Id, Quadrant = DofaQuadrant.Fortaleza, Text = "Equipo con experiencia en el dominio", SortOrder = 0 },
                new ProjectDofa { TenantId = tenant.Id, ProjectId = project.Id, Quadrant = DofaQuadrant.Oportunidad, Text = "Demanda creciente del mercado", SortOrder = 0 },
                new ProjectDofa { TenantId = tenant.Id, ProjectId = project.Id, Quadrant = DofaQuadrant.Debilidad, Text = "Dependencia de un proveedor clave", SortOrder = 0 },
                new ProjectDofa { TenantId = tenant.Id, ProjectId = project.Id, Quadrant = DofaQuadrant.Amenaza, Text = "Cambios regulatorios", SortOrder = 0 });
            await _db.SaveChangesAsync(cancellationToken);
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
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Aprobada'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_5" name="Rechazada" sourceRef="Gateway_Aprobacion" targetRef="End_Reinicio">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Rechazada'</bpmn:conditionExpression>
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

    /// <summary>
    /// Alineacion idempotente (ADR-0037) del flujo demo COT-COM: la decision que la UI ofrece es
    /// el NOMBRE de la arista del gateway (Aprobada/Rechazada) y se evalua contra su
    /// ConditionExpression, asi que el literal de la condicion debe COINCIDIR con el nombre. El
    /// seed historico traia condiciones en ingles (approval == 'Approved'/'Rejected') que nunca
    /// casaban con las opciones en espanol. Este paso reescribe la ConditionExpression de las
    /// aristas del gateway a "approval == '{Name}'" cuando difieren. Solo Development.
    /// </summary>
    public async Task<int> AlignDemoGatewayConditionsAsync(CancellationToken cancellationToken = default)
    {
        var edges = await (
            from edge in _db.WorkflowEdges
            join def in _db.WorkflowDefinitions on edge.DefinitionId equals def.Id
            join source in _db.WorkflowNodes on edge.SourceNodeId equals source.Id
            where def.ProcessCode == DemoWorkflowProcessCode
                && source.NodeType == WorkflowNodeType.ExclusiveGateway
                && edge.Name != null
            select edge).ToListAsync(cancellationToken);

        var fixedCount = 0;
        foreach (var edge in edges)
        {
            var expected = $"approval == '{edge.Name!.Trim()}'";
            if (!string.Equals(edge.ConditionExpression, expected, StringComparison.Ordinal))
            {
                edge.ConditionExpression = expected;
                fixedCount++;
            }
        }
        if (fixedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Condiciones de compuerta del flujo demo alineadas (ADR-0037): {Count}.", fixedCount);
        }
        return fixedCount;
    }

    /// <summary>
    /// Limpieza idempotente (ADR-0037): resuelve compuertas exclusivas que quedaron varadas como
    /// paso Pending-current de instancias Running (GAP historico: el camino de formulario no
    /// llevaba approvalResult al gateway). Hereda la decision del paso Completed que ENTRO al
    /// gateway (o toma la arista default si no hay decision) y delega en el motor, que las
    /// auto-resuelve y continua el avance. Sin gateways varados es no-op. REQUIERE ambient del
    /// tenant. Solo Development.
    /// </summary>
    public async Task<int> ResolveStuckGatewaysAsync(IWorkflowEngine engine, CancellationToken cancellationToken = default)
    {
        // Pasos current+Pending de compuertas exclusivas en instancias Running (filtro global).
        var stuck = await (
            from step in _db.WorkflowStepHistories
            where step.IsCurrent && step.Status == WorkflowStepStatus.Pending
            join instance in _db.WorkflowInstances on step.InstanceId equals instance.Id
            where instance.Status == WorkflowInstanceStatus.Running
            join node in _db.WorkflowNodes on step.NodeId equals node.Id
            where node.NodeType == WorkflowNodeType.ExclusiveGateway
            select new { Step = step, Node = node }).ToListAsync(cancellationToken);
        if (stuck.Count == 0)
        {
            return 0;
        }

        var resolved = 0;
        foreach (var row in stuck)
        {
            // Decision heredada: el paso Completed mas reciente de un nodo con arista hacia el
            // gateway (normalmente el Task de Cotizacion). Si no hay decision, el motor tomara la
            // arista default (o marcara Stuck si no existe: flujo mal modelado, no se fuerza).
            var sourceNodeIds = await _db.WorkflowEdges
                .Where(e => e.TargetNodeId == row.Node.Id)
                .Select(e => e.SourceNodeId)
                .ToListAsync(cancellationToken);
            var decision = await _db.WorkflowStepHistories
                .Where(s => s.InstanceId == row.Step.InstanceId
                    && sourceNodeIds.Contains(s.NodeId)
                    && s.Status == WorkflowStepStatus.Completed
                    && s.ApprovalResult != null)
                .OrderByDescending(s => s.CompletedAt)
                .Select(s => s.ApprovalResult)
                .FirstOrDefaultAsync(cancellationToken);

            var result = await engine.CompleteStepAsync(
                row.Step.InstanceId, row.Step.Id, null, decision, "Resuelto automaticamente (ADR-0037)", cancellationToken);
            if (result.IsOk || result.Status == WorkflowEngineStatus.StuckDetected)
            {
                resolved++;
            }
        }

        _logger.LogInformation("Compuertas varadas resueltas automaticamente (ADR-0037): {Count}.", resolved);
        return resolved;
    }

    // ---- Indice de flujos demo (editor canvas del prototipo, ADR-0022) ----

    public const string DemoDraftFlowName = "Mantenimiento y soporte";
    public const string DemoPausedFlowProcessCode = "VIS-TEC";
    public const string DemoPausedFlowName = "Visita tecnica de instalacion";

    /// <summary>
    /// Enriquece el indice /flujos del tenant demo (ADR-0022), idempotente:
    /// 1) Backfill de layout: definiciones anteriores a AddWorkflowEditorFields (todos los
    ///    nodos en 0,0) reciben auto-layout + XML regenerado con DI; COT-COM ademas recibe
    ///    la categoria "Comercial".
    /// 2) Un BORRADOR simple ("Mantenimiento y soporte", Operaciones) creado con el propio
    ///    IWorkflowDesignService (CreateDraft + AddNode + Connect + DeleteEdge).
    /// 3) Una definicion PAUSADA ("Visita tecnica de instalacion", VIS-TEC) publicada y
    ///    pausada. Sin instancias: las metricas reales de las nuevas pueden ser 0.
    /// REQUIERE ambient del tenant demo (igual que EnsureWorkflowDemoAsync). Solo Development.
    /// </summary>
    public async Task EnsureWorkflowIndexDemoAsync(
        IWorkflowEngine engine, IWorkflowDesignService design, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // 1) Backfill de layout para definiciones sembradas antes del editor.
        var definitions = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            if (definition.ProcessCode == DemoWorkflowProcessCode)
            {
                definition.Category ??= "Comercial";
            }
            var nodes = await _db.WorkflowNodes.IgnoreQueryFilters()
                .Where(n => n.DefinitionId == definition.Id)
                .OrderBy(n => n.StepNumber)
                .ToListAsync(cancellationToken);
            if (nodes.Count == 0 || nodes.Any(n => n.X != 0 || n.Y != 0))
            {
                continue;
            }
            var edges = await _db.WorkflowEdges.IgnoreQueryFilters()
                .Where(e => e.DefinitionId == definition.Id)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(cancellationToken);
            var nodesById = nodes.ToDictionary(n => n.Id);
            var layout = WorkflowAutoLayout.Compute(
                nodes.Select(n => (n.BpmnElementId, n.NodeType, n.StepNumber ?? 0)).ToList(),
                edges.Select(e => (nodesById[e.SourceNodeId].BpmnElementId, nodesById[e.TargetNodeId].BpmnElementId)).ToList());
            foreach (var node in nodes)
            {
                if (layout.TryGetValue(node.BpmnElementId, out var slot))
                {
                    node.X = slot.X;
                    node.Y = slot.Y;
                    node.W = slot.W;
                    node.H = slot.H;
                }
            }
            // XML regenerado con DI (mismo grafo + coordenadas; portabilidad bpmn.io).
            definition.BpmnXml = WorkflowDesignService.WriteXml(definition.ProcessCode, nodes, edges);
        }
        await _db.SaveChangesAsync(cancellationToken);

        // 2) Borrador simple creado con el propio servicio de diseno (dogfooding).
        var hasDraftDemo = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == tenant.Id && d.Name == DemoDraftFlowName, cancellationToken);
        if (!hasDraftDemo)
        {
            var created = await design.CreateDraftAsync(DemoDraftFlowName, "Operaciones", cancellationToken);
            if (created.IsOk && created.Value is not null)
            {
                var canvas = created.Value;
                var start = canvas.Nodes.First(n => n.NodeType == WorkflowNodeType.StartEvent);
                var end = canvas.Nodes.First(n => n.NodeType == WorkflowNodeType.EndEvent);
                var task = await design.AddNodeAsync(canvas.DefinitionId, WorkflowNodeType.Task, 240, 141, cancellationToken);
                if (task.IsOk && task.Value is not null)
                {
                    await design.RenameNodeAsync(task.Value.Id, "Atender solicitud de soporte", cancellationToken);
                    await design.ConnectAsync(start.Id, task.Value.Id, cancellationToken);
                    await design.ConnectAsync(task.Value.Id, end.Id, cancellationToken);
                    var direct = canvas.Edges.FirstOrDefault(e => e.SourceNodeId == start.Id && e.TargetNodeId == end.Id);
                    if (direct is not null)
                    {
                        await design.DeleteEdgeAsync(direct.Id, cancellationToken);
                    }
                    await design.UpdateDefinitionPropsAsync(canvas.DefinitionId, DemoDraftFlowName, "Operaciones",
                        "Borrador demo del editor de flujos: atencion de incidencias y mantenimiento.", cancellationToken);
                }
            }
            else
            {
                _logger.LogWarning("No se pudo sembrar el flujo borrador demo: {Error}", created.Error);
            }
        }

        // 3) Definicion publicada y PAUSADA (estado "Pausado" del indice).
        var hasPausedDemo = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == tenant.Id && d.ProcessCode == DemoPausedFlowProcessCode, cancellationToken);
        if (!hasPausedDemo)
        {
            var (evW, evH) = BpmnXmlWriter.DefaultSize(WorkflowNodeType.StartEvent);
            var (taskW, taskH) = BpmnXmlWriter.DefaultSize(WorkflowNodeType.Task);
            var xml = BpmnXmlWriter.Write(DemoPausedFlowProcessCode,
                [
                    new BpmnWriterNode("Start_1", "Inicio", WorkflowNodeType.StartEvent, 60, 150, evW, evH),
                    new BpmnWriterNode("Task_Agendar", "Agendar visita", WorkflowNodeType.Task, 190, 141, taskW, taskH),
                    new BpmnWriterNode("Task_Ejecutar", "Ejecutar visita en sitio", WorkflowNodeType.Task, 400, 141, taskW, taskH),
                    new BpmnWriterNode("End_1", "Fin", WorkflowNodeType.EndEvent, 620, 150, evW, evH)
                ],
                [
                    new BpmnWriterEdge("Flow_1", "Start_1", "Task_Agendar", null, null),
                    new BpmnWriterEdge("Flow_2", "Task_Agendar", "Task_Ejecutar", null, null),
                    new BpmnWriterEdge("Flow_3", "Task_Ejecutar", "End_1", null, null)
                ]);
            var imported = await engine.ImportBpmnAsync(new ImportBpmnRequest(
                DemoPausedFlowProcessCode, DemoPausedFlowName, xml,
                "Flujo demo pausado: visitas tecnicas de instalacion."), cancellationToken);
            if (imported.IsOk && imported.Value is not null)
            {
                await engine.PublishAsync(imported.Value.Id, cancellationToken);
                await design.UpdateDefinitionPropsAsync(imported.Value.Id, DemoPausedFlowName, "Operaciones",
                    "Flujo demo pausado: visitas tecnicas de instalacion.", cancellationToken);
                await design.PauseAsync(imported.Value.Id, cancellationToken);
            }
            else
            {
                _logger.LogWarning("No se pudo sembrar el flujo pausado demo: {Error}", imported.Error);
            }
        }
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

    // ---- Conceptos de actividad del CRM + sus formularios (000125, Ola C) ----

    /// <summary>
    /// Siembra los 5 conceptos de actividad del CRM y sus formularios asociados para un tenant de
    /// negocio: Anotacion, PQR, Solicitud, Oportunidad (maneja valor) y Cotizacion (maneja valor).
    /// Estos conceptos aparecen como botones en la pestana "Contacto Cliente" del modal de Tercero.
    /// Idempotente por Code (tanto del formulario como del concepto), estampa TenantId explicito.
    /// Corre para tenants reales via ECOREX_SEED_CRM_CONCEPTOS=true (ver Program.cs).
    /// </summary>
    public async Task EnsureCrmConceptosAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Crea un formulario (definicion + contenedor + preguntas) si su Code no existe para el tenant.
        // Devuelve el Id del formulario (existente o recien creado).
        async Task<Guid> EnsureFormAsync(
            string code, string title, string description,
            (string Field, string Label, FormControlType Type, bool Required, string Grid,
             string? Options, string? Validation)[] fields)
        {
            var existing = await _db.FormDefinitions.IgnoreQueryFilters()
                .Where(d => d.TenantId == tenantId && d.Code == code)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is Guid id) { return id; }

            var def = new FormDefinition
            {
                TenantId = tenantId,
                Code = code,
                Title = title,
                Description = description,
                Status = FormStatus.Active,
                Revision = 1
            };
            _db.FormDefinitions.Add(def);

            var container = new FormContainer
            {
                TenantId = tenantId,
                DefinitionId = def.Id,
                Name = title,
                ContainerType = FormContainerType.Segment,
                SortOrder = 0
            };
            _db.FormContainers.Add(container);

            var order = 0;
            foreach (var f in fields)
            {
                _db.FormQuestions.Add(new FormQuestion
                {
                    TenantId = tenantId,
                    DefinitionId = def.Id,
                    ContainerId = container.Id,
                    FieldCode = f.Field,
                    Label = f.Label,
                    ControlType = f.Type,
                    Required = f.Required,
                    SortOrder = order,
                    GridCol = f.Grid,
                    OptionsJson = f.Options,
                    ValidationJson = f.Validation
                });
                order++;
            }
            return def.Id;
        }

        // Crea el concepto si su Code no existe para el tenant. Estampa TenantId explicito.
        async Task EnsureConceptoAsync(
            string code, string name, string description, Guid formId,
            bool handlesValues, ConceptoActividadMode mode, int sortOrder)
        {
            if (await _db.ConceptosActividad.IgnoreQueryFilters()
                    .AnyAsync(c => c.TenantId == tenantId && c.Code == code, cancellationToken))
            {
                return;
            }
            _db.ConceptosActividad.Add(new ConceptoActividad
            {
                TenantId = tenantId,
                Code = code,
                Name = name,
                Description = description,
                FormDefinitionId = formId,
                HandlesValues = handlesValues,
                Mode = mode,
                SortOrder = sortOrder,
                IsArchived = false
            });
        }

        const string tipoPqr = """[{"id":"peticion","label":"Peticion"},{"id":"queja","label":"Queja"},{"id":"reclamo","label":"Reclamo"},{"id":"sugerencia","label":"Sugerencia"}]""";
        const string prioridad = """[{"id":"alta","label":"Alta"},{"id":"media","label":"Media"},{"id":"baja","label":"Baja"}]""";

        var anotacionForm = await EnsureFormAsync("FRM-CRM-ANOT", "Anotacion",
            "Nota libre de seguimiento del cliente.",
            new (string, string, FormControlType, bool, string, string?, string?)[]
            {
                ("texto", "Anotacion", FormControlType.TextArea, true, "col-12", null, """{"minLength":3,"maxLength":2000}"""),
                ("fecha", "Fecha", FormControlType.Date, false, "col-md-4", null, null),
            });

        var pqrForm = await EnsureFormAsync("FRM-CRM-PQR", "PQR",
            "Peticion, queja, reclamo o sugerencia del cliente.",
            new (string, string, FormControlType, bool, string, string?, string?)[]
            {
                ("tipo", "Tipo", FormControlType.Select, true, "col-md-6", tipoPqr, null),
                ("prioridad", "Prioridad", FormControlType.Select, false, "col-md-6", prioridad, null),
                ("asunto", "Asunto", FormControlType.Text, true, "col-12", null, """{"minLength":3,"maxLength":160}"""),
                ("detalle", "Detalle", FormControlType.TextArea, true, "col-12", null, """{"minLength":10,"maxLength":2000}"""),
            });

        var solicitudForm = await EnsureFormAsync("FRM-CRM-SOL", "Solicitud",
            "Solicitud o requerimiento del cliente.",
            new (string, string, FormControlType, bool, string, string?, string?)[]
            {
                ("asunto", "Asunto", FormControlType.Text, true, "col-md-8", null, """{"minLength":3,"maxLength":160}"""),
                ("fecha_requerida", "Fecha requerida", FormControlType.Date, false, "col-md-4", null, null),
                ("descripcion", "Descripcion", FormControlType.TextArea, true, "col-12", null, """{"minLength":10,"maxLength":2000}"""),
            });

        var oportunidadForm = await EnsureFormAsync("FRM-CRM-OPP", "Oportunidad",
            "Oportunidad comercial con valor estimado.",
            new (string, string, FormControlType, bool, string, string?, string?)[]
            {
                // NOTA: el VALOR ya no vive aqui. Lo pide el bloque de "datos del proceso" fuera del
                // formulario (concepto que maneja valor) y de ahi sale la Oportunidad del modulo.
                ("descripcion", "Descripcion", FormControlType.Text, true, "col-12", null, """{"minLength":3,"maxLength":200}"""),
                ("probabilidad", "Probabilidad (%)", FormControlType.Number, false, "col-md-4", null, """{"minValue":0,"maxValue":100}"""),
                ("fecha_cierre", "Fecha estimada de cierre", FormControlType.Date, false, "col-md-4", null, null),
                ("producto", "Producto / Servicio", FormControlType.Text, false, "col-12", null, null),
            });

        var cotizacionForm = await EnsureFormAsync("FRM-CRM-COT", "Cotizacion",
            "Cotizacion al cliente con valor total.",
            new (string, string, FormControlType, bool, string, string?, string?)[]
            {
                ("descripcion", "Descripcion", FormControlType.Text, true, "col-12", null, """{"minLength":3,"maxLength":200}"""),
                ("items", "Items", FormControlType.GridDetail, false, "col-12",
                    """[{"id":"detalle","label":"Detalle"},{"id":"cantidad","label":"Cantidad"},{"id":"valor_unitario","label":"Valor unitario"}]""", null),
                // El valor total lo pide el bloque de "datos del proceso", no el formulario.
                ("validez_dias", "Validez (dias)", FormControlType.Number, false, "col-md-6", null, """{"minValue":1,"maxValue":365}"""),
            });

        await EnsureConceptoAsync("CRM-ANOT", "Anotacion", "Nota libre de seguimiento.",
            anotacionForm, handlesValues: false, ConceptoActividadMode.None, 0);
        await EnsureConceptoAsync("CRM-PQR", "PQR", "Peticion, queja, reclamo o sugerencia.",
            pqrForm, handlesValues: false, ConceptoActividadMode.AttentionProcess, 1);
        await EnsureConceptoAsync("CRM-SOL", "Solicitud", "Solicitud o requerimiento del cliente.",
            solicitudForm, handlesValues: false, ConceptoActividadMode.AttentionProcess, 2);
        await EnsureConceptoAsync("CRM-OPP", "Oportunidad", "Oportunidad comercial con valor.",
            oportunidadForm, handlesValues: true, ConceptoActividadMode.None, 3);
        await EnsureConceptoAsync("CRM-COT", "Cotizacion", "Cotizacion con valor total.",
            cotizacionForm, handlesValues: true, ConceptoActividadMode.None, 4);

        await _db.SaveChangesAsync(cancellationToken);

        // Limpieza idempotente: en los tenants sembrados ANTES de mover el valor fuera del
        // formulario, FRM-CRM-OPP tenia "valor" y FRM-CRM-COT "valor_total" como campos
        // OBLIGATORIOS, que ahora duplican el campo de proceso (habia que teclearlo dos veces). No se
        // borran (conservan las respuestas historicas): se ocultan y se dejan de exigir.
        var formsCrm = await _db.FormDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && (d.Code == "FRM-CRM-OPP" || d.Code == "FRM-CRM-COT"))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        if (formsCrm.Count > 0)
        {
            var duplicadas = await _db.FormQuestions.IgnoreQueryFilters()
                .Where(q => q.TenantId == tenantId
                            && formsCrm.Contains(q.DefinitionId)
                            && (q.FieldCode == "valor" || q.FieldCode == "valor_total")
                            && (q.Required || !q.IsHidden))
                .ToListAsync(cancellationToken);
            foreach (var q in duplicadas)
            {
                q.Required = false;
                q.IsHidden = true;
            }
            if (duplicadas.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(
                    "[crm-conceptos] {N} campo(s) de valor duplicados ocultados en los formularios CRM del tenant {TenantId}.",
                    duplicadas.Count, tenantId);
            }
        }

        _logger.LogWarning("[crm-conceptos] 5 conceptos + formularios sembrados para tenant {TenantId}.", tenantId);
    }

    // ---- Formularios demo del constructor (ADR-0021) ----

    public const string DemoFormDraftCode = "FRM-002";
    public const string DemoFormBuilderCode = "FRM-003";

    /// <summary>
    /// Siembra 2 formularios extra para el indice del constructor (ADR-0021):
    /// FRM-002 "Inventario fisico bodega" en BORRADOR (KPI Borrador) y FRM-003
    /// "Visita tecnica de instalacion" ACTIVO con contenedores Row/Col, anchos
    /// parciales (Width) y una TABLA funcional (GridDetail). Idempotente por Code.
    /// Solo Development.
    /// </summary>
    public async Task EnsureFormBuilderDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        FormQuestion Q(Guid definitionId, Guid? containerId, int order, string fieldCode,
            string label, FormControlType type, bool required, int width,
            string? optionsJson = null, string? placeholder = null, string? defaultValue = null)
            => new()
            {
                TenantId = tenant.Id,
                DefinitionId = definitionId,
                ContainerId = containerId,
                FieldCode = fieldCode,
                Label = label,
                ControlType = type,
                Required = required,
                SortOrder = order,
                Width = width,
                GridCol = width >= 12 ? "col-12" : $"col-md-{width}",
                OptionsJson = optionsJson,
                PlaceholderText = placeholder,
                DefaultValue = defaultValue
            };

        // FRM-002: borrador simple (KPI "Borrador" del indice).
        if (!await _db.FormDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == tenant.Id && d.Code == DemoFormDraftCode, cancellationToken))
        {
            var draft = new FormDefinition
            {
                TenantId = tenant.Id,
                Code = DemoFormDraftCode,
                Title = "Inventario fisico bodega",
                Description = "Conteo fisico por bodega (borrador del constructor).",
                Status = FormStatus.Draft,
                Revision = 1
            };
            _db.FormDefinitions.Add(draft);
            _db.FormQuestions.AddRange(
                Q(draft.Id, null, 0, "bodega", "Bodega", FormControlType.Text, true, 6,
                    placeholder: "Nombre o codigo de la bodega"),
                Q(draft.Id, null, 1, "fecha_conteo", "Fecha del conteo", FormControlType.Date, true, 6),
                Q(draft.Id, null, 2, "observaciones", "Observaciones", FormControlType.TextArea, false, 12,
                    placeholder: "Novedades del conteo..."));
        }

        // FRM-003: activo con Row/Col, anchos parciales y tabla funcional.
        if (!await _db.FormDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == tenant.Id && d.Code == DemoFormBuilderCode, cancellationToken))
        {
            var visita = new FormDefinition
            {
                TenantId = tenant.Id,
                Code = DemoFormBuilderCode,
                Title = "Visita tecnica de instalacion",
                Description = "Formulario demo del constructor (Row/Col + tabla).",
                Status = FormStatus.Active,
                Revision = 1
            };
            _db.FormDefinitions.Add(visita);

            var datos = new FormContainer
            {
                TenantId = tenant.Id,
                DefinitionId = visita.Id,
                Name = "Datos del cliente",
                ContainerType = FormContainerType.Row,
                SortOrder = 0,
                Width = 12
            };
            var observaciones = new FormContainer
            {
                TenantId = tenant.Id,
                DefinitionId = visita.Id,
                Name = "Observaciones",
                ContainerType = FormContainerType.Col,
                SortOrder = 1,
                Width = 12
            };
            var equipos = new FormContainer
            {
                TenantId = tenant.Id,
                DefinitionId = visita.Id,
                Name = "Equipos instalados",
                ContainerType = FormContainerType.Section,
                SortOrder = 2,
                Width = 12
            };
            _db.FormContainers.AddRange(datos, observaciones, equipos);

            _db.FormQuestions.AddRange(
                Q(visita.Id, datos.Id, 0, "cc_cliente", "CC", FormControlType.Text, true, 3,
                    placeholder: "Ingrese numero de documento"),
                Q(visita.Id, datos.Id, 1, "nombres_cliente", "Nombres y apellidos", FormControlType.Text, true, 5,
                    placeholder: "Ingrese nombres completos"),
                Q(visita.Id, datos.Id, 2, "fecha_visita", "Fecha de la visita", FormControlType.Date, true, 4),
                Q(visita.Id, observaciones.Id, 0, "observacion", "Observacion", FormControlType.TextArea, false, 12,
                    placeholder: "Ingrese observaciones..."),
                Q(visita.Id, equipos.Id, 0, "equipos", "Equipos", FormControlType.GridDetail, true, 12,
                    optionsJson: """[{"id":"equipo","label":"Equipo"},{"id":"serial","label":"Serial"},{"id":"cantidad","label":"Cantidad"}]"""),
                Q(visita.Id, equipos.Id, 1, "firma_cliente", "Firma del cliente", FormControlType.Signature, false, 12));
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Formularios demo del constructor sembrados para {Tenant}: {Draft} (borrador) y {Builder} (Row/Col + tabla).",
            tenant.Name, DemoFormDraftCode, DemoFormBuilderCode);
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

    // ========== Tableros de actividades unificados (ADR-0020) - demo del prototipo ==========

    public const string DemoActivityBoardCode = "PRY-0042";

    /// <summary>
    /// Siembra los tableros de ACTIVIDADES del prototipo (ECOREX.dc.html, modulo 000636)
    /// para el tenant demo SKY SYSTEM: el tablero "Comercial - Requerimiento Infraestructura"
    /// (PRY-0042) con columnas default y 10 tareas TaskItem (checklists, tags, encargados y
    /// asignados repartidos entre owner/admin/operator/viewer, 1 sin asignar y 3 del owner
    /// para los contadores de alcance 10/3/1), mas 2 tableros simples para el KPI
    /// "3 Tableros". Idempotente por Kind=Activities del tenant. Solo Development.
    /// </summary>
    public async Task EnsureActivityBoardsDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.TaskBoards.IgnoreQueryFilters()
                .AnyAsync(b => b.TenantId == tenant.Id && b.Kind == TaskBoardKind.Activities, cancellationToken))
        {
            return;
        }

        var members = await _db.TenantUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        var owner = members.FirstOrDefault(u => u.Email == TenantOwnerEmail)
            ?? members.OrderBy(u => u.TenantRole == TenantRole.Owner ? 0 : 1).ThenBy(u => u.CreatedAt).FirstOrDefault();
        var admin = members.FirstOrDefault(u => u.Email == TenantAdminEmail) ?? owner;
        var operatorUser = members.FirstOrDefault(u => u.Email == TenantOperatorEmail) ?? owner;
        var viewer = members.FirstOrDefault(u => u.Email == TenantViewerEmail) ?? owner;
        if (owner is null) { return; }

        var activityType = await _db.ActivityTypes.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenant.Id && !t.IsArchived)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (activityType is null)
        {
            _logger.LogWarning("Seed de tableros de actividades omitido: el tenant demo no tiene tipos de actividad.");
            return;
        }

        // ---- Tags del tablero del prototipo (catalogo TaskItemTag por tenant) ----
        var tagDefs = new (string Name, string Color)[]
        {
            ("Infraestructura", "#3b82f6"), // azul
            ("Comercial", "#ec4899"),       // rosa
            ("Proyecto medio", "#22c55e")   // verde
        };
        var tags = new Dictionary<string, TaskItemTag>();
        foreach (var (name, color) in tagDefs)
        {
            var tag = await _db.TaskItemTags.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Name == name, cancellationToken);
            if (tag is null)
            {
                tag = new TaskItemTag { TenantId = tenant.Id, Name = name, Color = color };
                _db.TaskItemTags.Add(tag);
            }
            tags[name] = tag;
        }

        // ---- Columnas default del prototipo (mismo set de TaskBoardService) ----
        var defaultColumns = new (string Name, string Color, bool IsDone)[]
        {
            ("Por hacer",   "#e2e8f0", false),
            ("En progreso", "#bfdbfe", false),
            ("En revision", "#fed7aa", false),
            ("Completado",  "#bbf7d0", true)
        };
        var nextBoardOrder = (await _db.TaskBoards.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenant.Id)
            .Select(b => (int?)b.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        TaskBoard NewBoard(string code, string name, string? description, TaskBoardStatus status, DateTimeOffset? dueDate)
            => new()
            {
                TenantId = tenant.Id,
                Kind = TaskBoardKind.Activities,
                Code = code,
                Name = name,
                Description = description,
                Status = status,
                DueDate = dueDate,
                SortOrder = nextBoardOrder++
            };

        var mainBoard = NewBoard(DemoActivityBoardCode, "Comercial - Requerimiento Infraestructura",
            "Flujo de aprobacion comercial, cotizacion y compra del cliente.",
            TaskBoardStatus.InProgress, new DateTimeOffset(2026, 7, 12, 23, 59, 0, TimeSpan.Zero));
        var board2 = NewBoard("PRY-0040", "Marketing - Lanzamiento Q3",
            "Campana de lanzamiento del tercer trimestre.",
            TaskBoardStatus.OnTime, new DateTimeOffset(2026, 8, 15, 23, 59, 0, TimeSpan.Zero));
        var board3 = NewBoard("PRY-0041", "Soporte - Mesa de ayuda",
            "Atencion de tickets internos de soporte.",
            TaskBoardStatus.AtRisk, new DateTimeOffset(2026, 7, 5, 23, 59, 0, TimeSpan.Zero));
        _db.TaskBoards.AddRange(mainBoard, board2, board3);

        var mainColumns = new Dictionary<string, TaskBoardColumn>();
        foreach (var board in new[] { mainBoard, board2, board3 })
        {
            for (int i = 0; i < defaultColumns.Length; i++)
            {
                var (cname, ccolor, isDone) = defaultColumns[i];
                var column = new TaskBoardColumn
                {
                    TenantId = tenant.Id,
                    BoardId = board.Id,
                    Name = cname,
                    Color = ccolor,
                    SortOrder = i,
                    IsDone = isDone
                };
                _db.TaskBoardColumns.Add(column);
                if (board == mainBoard) { mainColumns[cname] = column; }
            }
        }

        // Secuencia PRY coherente con los codigos sembrados (proximo: PRY-0043).
        if (!await _db.TenantSequences.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenant.Id && s.Code == "PRY", cancellationToken))
        {
            _db.TenantSequences.Add(new TenantSequence { TenantId = tenant.Id, Code = "PRY", NextValue = 43 });
        }

        // ---- 10 tareas del tablero principal (numeros T continuando la secuencia T05) ----
        var sequence = await _db.TenantSequences.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Code == "T05", cancellationToken);
        if (sequence is null)
        {
            sequence = new TenantSequence { TenantId = tenant.Id, Code = "T05", NextValue = 1 };
            _db.TenantSequences.Add(sequence);
        }

        var today = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(17), TimeSpan.Zero);
        DateTimeOffset July(int day) => new(2026, 7, day, 12, 0, 0, TimeSpan.Zero);

        // (Titulo, Columna, Estado, Encargado, Asignados, Due, Tag, checklist done/total)
        // Contadores de alcance esperados para el OWNER: team 10 / mine 3 (tareas con * ) /
        // unassigned 1 (tarea sin encargado ni asignados).
        (string Title, string Column, TaskItemStatus Status, TenantUser? Assignee, TenantUser[] Team,
         DateTimeOffset? Due, string? Tag, int ChecksDone, int ChecksTotal)[] taskDefs =
        {
            ("Cotizar equipos de red", "Por hacer", TaskItemStatus.Active,
                admin, [operatorUser!], July(1), "Infraestructura", 0, 4),
            ("Migrar formulario a EAV", "En progreso", TaskItemStatus.InProgress, // * owner encargado
                owner, [], today, "Proyecto medio", 3, 4),
            ("Aprobar cotizacion de proveedor", "En revision", TaskItemStatus.InProgress, // * owner asignado
                admin, [owner], today, "Comercial", 4, 4),
            ("Configurar consecutivo 0D7", "Completado", TaskItemStatus.Done,
                operatorUser, [], July(6), null, 0, 0),
            ("Levantar inventario de sedes", "Por hacer", TaskItemStatus.Pending, // sin asignar
                null, [], July(8), "Infraestructura", 0, 3),
            ("Contactar proveedores de fibra", "Por hacer", TaskItemStatus.Active,
                viewer, [], July(9), "Comercial", 0, 0),
            ("Redactar requerimiento tecnico", "En progreso", TaskItemStatus.InProgress, // * owner encargado
                owner, [], July(10), null, 1, 2),
            ("Validar presupuesto con gerencia", "En revision", TaskItemStatus.InProgress,
                admin, [viewer!], July(11), "Comercial", 0, 0),
            ("Configurar VLAN de pruebas", "Completado", TaskItemStatus.Done,
                operatorUser, [], July(2), "Infraestructura", 0, 0),
            ("Comprar licencias de firewall", "Por hacer", TaskItemStatus.Active,
                admin, [], July(15), "Infraestructura", 0, 0)
        };

        var sortPerColumn = new Dictionary<string, int>();
        foreach (var def in taskDefs)
        {
            var column = mainColumns[def.Column];
            sortPerColumn.TryGetValue(def.Column, out var sortOrder);
            sortPerColumn[def.Column] = sortOrder + 1;

            var number = "T" + sequence.NextValue.ToString().PadLeft(5, '0');
            sequence.NextValue++;

            var task = new TaskItem
            {
                TenantId = tenant.Id,
                Number = number,
                Title = def.Title,
                ActivityTypeId = activityType.Id,
                Priority = def.Tag == "Comercial" ? TaskPriority.High : TaskPriority.Medium,
                Status = def.Status,
                AssigneeTenantUserId = def.Assignee?.Id,
                DueDate = def.Due,
                StartDate = def.Due?.AddDays(-7),
                BoardId = mainBoard.Id,
                ColumnId = column.Id,
                BoardSortOrder = sortOrder
            };
            _db.TaskItems.Add(task);
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = tenant.Id,
                TaskItemId = task.Id,
                Type = TaskActivityType.Action,
                ActorUserId = owner.PlatformUserId,
                ActorName = "Owner SKY SYSTEM",
                Text = $"creo la tarea {number}"
            });

            if (def.Tag is not null)
            {
                _db.TaskItemTagAssignments.Add(new TaskItemTagAssignment
                {
                    TenantId = tenant.Id,
                    TaskItemId = task.Id,
                    TagId = tags[def.Tag].Id
                });
            }
            foreach (var teammate in def.Team)
            {
                _db.TaskItemAssignments.Add(new TaskItemAssignment
                {
                    TenantId = tenant.Id,
                    TaskItemId = task.Id,
                    TenantUserId = teammate.Id
                });
            }
            for (int i = 0; i < def.ChecksTotal; i++)
            {
                var done = i < def.ChecksDone;
                _db.TaskItemChecklistItems.Add(new TaskItemChecklistItem
                {
                    TenantId = tenant.Id,
                    TaskItemId = task.Id,
                    Text = $"Paso {i + 1} de {def.Title.ToLowerInvariant()}",
                    IsCompleted = done,
                    CompletedAt = done ? DateTimeOffset.UtcNow.AddHours(-i) : null,
                    CompletedByTenantUserId = done ? (def.Assignee ?? owner).Id : null,
                    SortOrder = i
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Tableros de actividades demo sembrados para {Tenant}: {Board} con {Tasks} tareas + 2 tableros simples.",
            tenant.Name, DemoActivityBoardCode, taskDefs.Length);
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
    /// Asignacion por nodo demo (ADR-0035, ola F1): mini organigrama con clasificador
    /// (Dependencia -&gt; Cargo -&gt; Funcionario) y policies WorkflowNodePolicy sobre los nodos
    /// Task del flujo publicado COT-COM (Cotizacion Comercial), para que la ola F2 (bandeja)
    /// tenga datos reales que resolver:
    ///   Dependencia "Comercial" -&gt; Cargo "Asesor Comercial" -&gt; Funcionario (owner/operator).
    ///   Dependencia "Finanzas"  -&gt; Cargo "Aprobador"        -&gt; Funcionario (admin).
    /// Task_Requerimiento -&gt; Cargo "Asesor Comercial"; Gateway/aprobacion via Task previo -&gt;
    /// "Aprobador". Idempotente: si ya existen las unidades/policies no duplica. Solo Development.
    /// </summary>
    public async Task EnsureOrgAssignmentDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        var members = await _db.TenantUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        var owner = members.FirstOrDefault(u => u.Email == TenantOwnerEmail);
        var operatorUser = members.FirstOrDefault(u => u.Email == TenantOperatorEmail);
        var admin = members.FirstOrDefault(u => u.Email == TenantAdminEmail);
        var asesorOccupant = operatorUser ?? owner;
        if (asesorOccupant is null || admin is null) { return; }

        var existing = await _db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);

        // Helper idempotente: crea la unidad si no existe (por Name + Classifier + ParentId).
        async Task<OrgUnit> EnsureUnitAsync(
            string name, OrgUnitClassifier classifier, Guid? parentId, Guid? occupantUserId, int sortOrder)
        {
            var found = existing.FirstOrDefault(u =>
                u.Name == name && u.Classifier == classifier && u.ParentId == parentId);
            if (found is not null) { return found; }
            found = new OrgUnit
            {
                TenantId = tenant.Id,
                Name = name,
                Kind = classifier == OrgUnitClassifier.Dependencia ? OrgUnitKind.Area : OrgUnitKind.Team,
                Classifier = classifier,
                ParentId = parentId,
                TenantUserId = classifier == OrgUnitClassifier.Funcionario ? occupantUserId : null,
                Description = $"{classifier} demo de asignacion por nodo (COT-COM).",
                SortOrder = sortOrder
            };
            _db.OrgUnits.Add(found);
            existing.Add(found);
            await _db.SaveChangesAsync(cancellationToken);
            return found;
        }

        // Comercial -> Asesor Comercial -> Funcionario ocupante.
        var comercial = await EnsureUnitAsync("Comercial (asignacion)", OrgUnitClassifier.Dependencia, null, null, 10);
        var asesorCargo = await EnsureUnitAsync("Asesor Comercial", OrgUnitClassifier.Cargo, comercial.Id, null, 0);
        await EnsureUnitAsync(
            asesorOccupant.Email, OrgUnitClassifier.Funcionario, asesorCargo.Id, asesorOccupant.Id, 0);

        // Finanzas -> Aprobador -> Funcionario ocupante (admin).
        var finanzas = await EnsureUnitAsync("Finanzas (asignacion)", OrgUnitClassifier.Dependencia, null, null, 11);
        var aprobadorCargo = await EnsureUnitAsync("Aprobador", OrgUnitClassifier.Cargo, finanzas.Id, null, 0);
        await EnsureUnitAsync(admin.Email, OrgUnitClassifier.Funcionario, aprobadorCargo.Id, admin.Id, 0);

        // Policies sobre los nodos Task del flujo publicado COT-COM.
        var definitionId = await _db.WorkflowDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id && d.ProcessCode == DemoWorkflowProcessCode && d.IsPublished)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (definitionId is not Guid wfId)
        {
            _logger.LogInformation("Asignacion por nodo demo: COT-COM aun no publicado; se sembraron unidades sin policies.");
            return;
        }

        var nodes = await _db.WorkflowNodes.IgnoreQueryFilters()
            .Where(n => n.DefinitionId == wfId)
            .ToListAsync(cancellationToken);
        var requerimiento = nodes.FirstOrDefault(n => n.BpmnElementId == "Task_Requerimiento");
        var cotizacion = nodes.FirstOrDefault(n => n.BpmnElementId == "Task_Cotizacion");

        async Task EnsurePolicyAsync(WorkflowNode? node, Guid orgUnitId)
        {
            if (node is null) { return; }
            var already = await _db.WorkflowNodePolicies.IgnoreQueryFilters()
                .AnyAsync(p => p.WorkflowNodeId == node.Id && p.OrgUnitId == orgUnitId, cancellationToken);
            if (already) { return; }
            _db.WorkflowNodePolicies.Add(new WorkflowNodePolicy
            {
                TenantId = tenant.Id,
                WorkflowNodeId = node.Id,
                OrgUnitId = orgUnitId,
                SortOrder = 0
            });
        }

        // Tarea inicial -> Asesor Comercial; paso de cotizacion/aprobacion -> Aprobador.
        await EnsurePolicyAsync(requerimiento, asesorCargo.Id);
        await EnsurePolicyAsync(cotizacion, aprobadorCargo.Id);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Asignacion por nodo demo sembrada para {Tenant}: 2 dependencias/cargos/funcionarios + policies COT-COM.",
            tenant.Name);
    }

    /// <summary>
    /// Runtime operativo de flujos demo (bandeja "mis pasos", ola F2, ADR-0036): crea una TAREA
    /// del ActivityType vinculado a COT-COM ("Direccion Comercial/Cotizacion") via el flujo normal
    /// (ITaskItemService.CreateAsync), lo que arranca una WorkflowInstance Running con el primer
    /// paso (Requerimiento) Pending y SIN reclamar. Como el nodo Requerimiento tiene la policy del
    /// cargo "Asesor Comercial" (ocupado por operator@), al entrar a /mis-pasos como operator@ hay
    /// un paso listo para atender. Idempotente por titulo de la tarea. REQUIERE ambient del tenant
    /// demo (el servicio consulta via el filtro global). Solo Development.
    /// </summary>
    public async Task EnsureWorkflowRuntimeDemoAsync(
        ITaskItemService tasks, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // ActivityType demo vinculado a COT-COM publicado (lo prepara EnsureWorkflowDemoAsync).
        var activityType = await _db.ActivityTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenant.Id
                && t.Category == "Direccion Comercial" && t.Name == "Cotizacion"
                && t.WorkflowDefinitionId != null, cancellationToken);
        if (activityType is null)
        {
            _logger.LogInformation("Runtime de flujos demo: ActivityType COT-COM sin vincular; se omite la tarea demo.");
            return;
        }

        const string demoTaskTitle = "Cotizacion de infraestructura para cliente demo";
        if (await _db.TaskItems.IgnoreQueryFilters()
                .AnyAsync(t => t.TenantId == tenant.Id && t.Title == demoTaskTitle, cancellationToken))
        {
            return;
        }

        // El owner del tenant crea la tarea; el flujo arranca en la MISMA transaccion (el paso
        // Requerimiento queda Pending y sin reclamar, listo para el candidato Asesor Comercial).
        var owner = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == TenantOwnerEmail, cancellationToken);
        var actorUserId = owner?.PlatformUserId ?? Guid.Empty;

        var created = await tasks.CreateAsync(
            new CreateTaskItemRequest(
                demoTaskTitle, activityType.Id,
                Description: "Tarea demo del runtime de flujos: arranca COT-COM en el paso Requerimiento.",
                RequesterName: "Cliente Demo", RequesterEmail: "cliente.demo@ejemplo.com"),
            actorUserId, "Seeder", cancellationToken);
        if (!created.IsOk)
        {
            _logger.LogWarning("Runtime de flujos demo: no se pudo crear la tarea demo: {Error}", created.Error);
            return;
        }

        _logger.LogInformation(
            "Runtime de flujos demo sembrado para {Tenant}: tarea '{Title}' con COT-COM Running en Requerimiento.",
            tenant.Name, demoTaskTitle);
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
            ("000889", "Programar actividad", "Programacion de actividades recurrentes o futuras.", "/programar-actividad", ModuleArea.Operaciones, false),
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

    /// <summary>
    /// Fuente demo del modulo EXTRACCION DE DATOS (000730, ADR-0025) para el tenant demo:
    /// una fuente Json apuntando al endpoint PROPIO de la consola /api/demo/scrape-sample
    /// (JSON estatico de items), para probar Ejecutar sin depender de internet. El guard
    /// SSRF permite loopback SOLO en Development (excepcion explicita documentada en el ADR).
    /// Idempotente por nombre; si la app arranca en otro puerto (ej. suite E2E), la URL se
    /// actualiza para seguir apuntando a la instancia viva.
    /// </summary>
    public async Task EnsureScrapingDemoAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        const string name = "Items demo ECOREX (JSON)";
        var url = $"{baseUrl.TrimEnd('/')}/api/demo/scrape-sample";

        var source = await _db.ScrapeSources.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Name == name, cancellationToken);
        if (source is null)
        {
            _db.ScrapeSources.Add(new ScrapeSource
            {
                TenantId = tenant.Id,
                Name = name,
                Url = url,
                Kind = ScrapeSourceKind.Json,
                Status = ScrapeSourceStatus.Active
            });
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Fuente de extraccion demo sembrada para {Tenant}: {Url}", tenant.Name, url);
        }
        else if (source.Url != url)
        {
            source.Url = url;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Fuente de extraccion demo re-apuntada a {Url}.", url);
        }
    }

    /// <summary>
    /// Siembra el inventario demo del tenant SKY SYSTEM (grupo Sistema - Inventarios, ADR-0027):
    /// 2 bodegas, 3 marcas, 2 grupos con 2 subgrupos c/u, 3 tipos y ~8 items con stock repartido
    /// (algunos en 0 para probar el filtro de disponibles) e imagenes placeholder. Idempotente
    /// por tabla vacia (guard por tenant en cada bloque). Solo Development.
    /// </summary>
    /// <summary>
    /// Siembra los terceros de ejemplo del Directorio General (modulo 000232) para el tenant
    /// indicado: 3 empresas (ANDINA, Produvarios, INGETEL) con contactos y 2 personas (una cliente
    /// individual, un empleado). Idempotente: si el tenant ya tiene terceros, no hace nada. Estampa
    /// TenantId explicito (no depende del ambient). Rellena FichasJson con un ejemplo por perfil.
    /// </summary>
    public async Task EnsureDirectorioGeneralDemoAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Los campos configurables por ficha se siembran siempre (idempotente por su cuenta),
        // aunque el tenant ya tenga terceros de ejemplo.
        await EnsureDirectorioFieldDefaultsAsync(tenantId, cancellationToken);

        if (await _db.Terceros.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        var andina = new Tercero
        {
            TenantId = tenantId,
            Nombre = "ANDINA S.A.S",
            Tipo = TerceroTipo.Empresa,
            Perfiles = TerceroPerfil.Cliente,
            Estado = TerceroEstado.Activo,
            Vendedor = "Julian R.",
            Ciudad = "Bogota",
            IdTipo = TerceroIdTipo.Nit,
            IdValor = "901.111.222",
            Sector = "Manufactura",
            FichasJson = "{\"fiscal\":{\"regimen\":\"Comun\",\"responsableIva\":\"Si\"},"
                + "\"comercial\":{\"vendedor\":\"Julian R.\",\"segmento\":\"Corporativo\"},"
                + "\"cliente\":{\"cupo\":\"50000000\",\"condicionPago\":\"30 dias\"}}"
        };
        var produvarios = new Tercero
        {
            TenantId = tenantId,
            Nombre = "Produvarios",
            Tipo = TerceroTipo.Empresa,
            Perfiles = TerceroPerfil.Cliente | TerceroPerfil.Proveedor,
            Estado = TerceroEstado.Activo,
            Ciudad = "Medellin",
            IdTipo = TerceroIdTipo.Nit,
            IdValor = "900.222.333",
            Sector = "Distribucion",
            FichasJson = "{\"fiscal\":{\"regimen\":\"Comun\",\"responsableIva\":\"Si\"},"
                + "\"cliente\":{\"cupo\":\"20000000\",\"condicionPago\":\"Contado\"},"
                + "\"proveedor\":{\"categoria\":\"Insumos\",\"plazoEntrega\":\"5 dias\"}}"
        };
        var ingetel = new Tercero
        {
            TenantId = tenantId,
            Nombre = "INGETEL",
            Tipo = TerceroTipo.Empresa,
            Perfiles = TerceroPerfil.Proveedor,
            Estado = TerceroEstado.Activo,
            Ciudad = "Bogota",
            IdTipo = TerceroIdTipo.Nit,
            IdValor = "830.444.555",
            Sector = "Telecomunicaciones",
            FichasJson = "{\"fiscal\":{\"regimen\":\"Comun\",\"responsableIva\":\"Si\"},"
                + "\"proveedor\":{\"categoria\":\"Servicios\",\"plazoEntrega\":\"15 dias\"}}"
        };
        var maria = new Tercero
        {
            TenantId = tenantId,
            Nombre = "Maria Fernanda Lopez",
            Tipo = TerceroTipo.Persona,
            Perfiles = TerceroPerfil.Cliente,
            Estado = TerceroEstado.Activo,
            Ciudad = "Cali",
            IdTipo = TerceroIdTipo.Identificacion,
            IdValor = "1.020.334.556",
            Cargo = "Independiente",
            Email = "mfernanda@ejemplo.local",
            FichasJson = "{\"comercial\":{\"segmento\":\"Personal\"},"
                + "\"cliente\":{\"cupo\":\"3000000\",\"condicionPago\":\"Contado\"}}"
        };
        var roberto = new Tercero
        {
            TenantId = tenantId,
            Nombre = "Roberto Salcedo",
            Tipo = TerceroTipo.Persona,
            Perfiles = TerceroPerfil.Empleado,
            Estado = TerceroEstado.Activo,
            Ciudad = "Bogota",
            IdTipo = TerceroIdTipo.Correo,
            IdValor = "rsalcedo@ejemplo.local",
            Cargo = "Consultor",
            Email = "rsalcedo@ejemplo.local",
            FichasJson = "{\"empleado\":{\"cargo\":\"Consultor\",\"area\":\"Comercial\"}}"
        };
        _db.Terceros.AddRange(andina, produvarios, ingetel, maria, roberto);

        _db.TerceroContactos.AddRange(
            new TerceroContacto
            {
                TenantId = tenantId,
                TerceroId = andina.Id,
                Nombre = "Carlos Mesa",
                Cargo = "Gerente de Compras",
                Email = "cmesa@andina.local",
                Telefono = "3101112233"
            },
            new TerceroContacto
            {
                TenantId = tenantId,
                TerceroId = andina.Id,
                Nombre = "Laura Prieto",
                Cargo = "Asistente Administrativa",
                Email = "lprieto@andina.local",
                Telefono = "3104445566"
            },
            new TerceroContacto
            {
                TenantId = tenantId,
                TerceroId = produvarios.Id,
                Nombre = "Andres Gil",
                Cargo = "Jefe de Logistica",
                Email = "agil@produvarios.local",
                Telefono = "3117778899"
            });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Directorio General demo sembrado para tenant {Tenant}: 3 empresas, 2 personas, 3 contactos.", tenantId);
    }

    /// <summary>
    /// Siembra los datos demo del Gestor de Clientes (000740) para el tenant indicado, si aun no
    /// tiene columnas de Bolsa. Idempotente. Estampa TenantId explicito. Debe correr DESPUES de
    /// <see cref="EnsureDirectorioGeneralDemoAsync"/> (reutiliza los terceros ya sembrados). Siembra:
    /// 5 columnas de Bolsa, asignacion de terceros a columnas, oportunidades, citas del mes actual,
    /// filtros dinamicos con snapshot distinto al conteo actual y prospectos scrapeados.
    /// </summary>
    public async Task EnsureGestorContactosDemoAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await _db.BolsaColumnas.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        // 1) Columnas de la Bolsa (mismo set que usa el servicio como default).
        var columnas = Ecorex.Application.Gestor.GestorContactosService.BuildDefaultColumnas(tenantId).ToList();
        _db.BolsaColumnas.AddRange(columnas);
        await _db.SaveChangesAsync(cancellationToken);

        // 2) Asignar algunos terceros ya sembrados del Directorio a columnas de la Bolsa.
        // Indices de columnas: 0 Sospechoso, 1 Incubadora, 2 Clientes, 3 Seguimiento, 4 Cierre.
        var terceros = await _db.Terceros.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.EmpresaId == null)
            .OrderBy(t => t.Nombre)
            .ToListAsync(cancellationToken);
        var asignaciones = new[] { 2, 1, 0, 3, 2, 4 };
        for (var i = 0; i < terceros.Count && i < asignaciones.Length; i++)
        {
            terceros[i].BolsaColumnaId = columnas[asignaciones[i]].Id;
        }

        // 3) Oportunidades sobre esos terceros (varias etapas, valores COP).
        var oportunidades = new List<Oportunidad>();
        void AddOp(int idx, string nombre, OportunidadEtapa etapa, decimal valor, string resp, int prob, string fuente)
        {
            if (idx >= terceros.Count) { return; }
            oportunidades.Add(new Oportunidad
            {
                TenantId = tenantId,
                TerceroId = terceros[idx].Id,
                Nombre = nombre,
                Etapa = etapa,
                Valor = valor,
                Responsable = resp,
                Probabilidad = prob,
                Fuente = fuente,
                SortOrder = oportunidades.Count
            });
        }
        AddOp(0, "Renovacion enlace dedicado", OportunidadEtapa.Negociacion, 45000000m, "Julian R.", 70, "Referido");
        AddOp(1, "Suministro insumos Q3", OportunidadEtapa.Propuesta, 28500000m, "Ana M.", 50, "Campana");
        AddOp(2, "Plan hogar fibra", OportunidadEtapa.Calificada, 3200000m, "Carlos T.", 40, "LinkedIn");
        AddOp(3, "Contrato soporte anual", OportunidadEtapa.Nueva, 18000000m, "Julian R.", 25, "Web");
        AddOp(0, "Ampliacion datacenter", OportunidadEtapa.Ganada, 62000000m, "Julian R.", 100, "Referido");
        _db.Oportunidades.AddRange(oportunidades);

        // 4) Citas del mes actual (fechas relativas a UtcNow; dias <= 28 seguros en cualquier mes).
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset Dia(int d, int h, int m) => new(now.Year, now.Month, d, h, m, 0, TimeSpan.Zero);
        Guid? PrimeraOpId() => oportunidades.Count > 0 ? oportunidades[0].Id : null;
        Guid? TerceroId(int idx) => idx < terceros.Count ? terceros[idx].Id : null;
        var citas = new List<Cita>
        {
            new() { TenantId = tenantId, TerceroId = TerceroId(0), OportunidadId = PrimeraOpId(), Titulo = "Cotizacion enlace dedicado", Tipo = CitaTipo.Cotizacion, Inicio = Dia(3, 10, 0), DuracionMinutos = 60, Nota = "Enviar propuesta comercial.", Completada = false },
            new() { TenantId = tenantId, TerceroId = TerceroId(1), Titulo = "Llamada seguimiento", Tipo = CitaTipo.Llamada, Inicio = Dia(6, 15, 30), DuracionMinutos = 20, Nota = "Confirmar interes.", Completada = true },
            new() { TenantId = tenantId, TerceroId = TerceroId(2), Titulo = "Reunion de arranque", Tipo = CitaTipo.Reunion, Inicio = Dia(9, 9, 0), DuracionMinutos = 45, Nota = "Alcance del plan.", Completada = false },
            new() { TenantId = tenantId, TerceroId = TerceroId(3), Titulo = "Visita tecnica en sitio", Tipo = CitaTipo.Visita, Inicio = Dia(14, 14, 0), DuracionMinutos = 120, Nota = "Levantamiento de requerimientos.", Completada = false },
            new() { TenantId = tenantId, TerceroId = TerceroId(0), Titulo = "Atencion PQR", Tipo = CitaTipo.Pqr, Inicio = Dia(20, 11, 0), DuracionMinutos = 30, Nota = "Seguimiento a caso abierto.", Completada = false },
            new() { TenantId = tenantId, TerceroId = TerceroId(4), Titulo = "Cierre de negociacion", Tipo = CitaTipo.Reunion, Inicio = Dia(25, 16, 0), DuracionMinutos = 60, Nota = "Firma de contrato.", Completada = false }
        };
        _db.Citas.AddRange(citas);

        // 5) Filtros dinamicos: criterios que aciertan sobre los terceros del Directorio, con
        // ConteoAnterior distinto al conteo actual para que el % de crecimiento se vea.
        var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        string Crit(string campo, string op, string valor)
            => System.Text.Json.JsonSerializer.Serialize(
                new[] { new Ecorex.Application.Gestor.FiltroCriterio(campo, op, valor) }, jsonOpts);
        var filtros = new List<TerceroFiltro>
        {
            new() { TenantId = tenantId, Nombre = "Clientes activos", Descripcion = "Terceros con perfil cliente.", Fuente = "Todos", CriteriosJson = Crit("perfil", "=", "Cliente"), ConteoAnterior = 2, FechaSnapshot = now, SortOrder = 0 },
            new() { TenantId = tenantId, Nombre = "Proveedores", Descripcion = "Terceros con perfil proveedor.", Fuente = "Todos", CriteriosJson = Crit("perfil", "=", "Proveedor"), ConteoAnterior = 1, FechaSnapshot = now, SortOrder = 1 },
            new() { TenantId = tenantId, Nombre = "Contactos Bogota", Descripcion = "Terceros de la ciudad de Bogota.", Fuente = "Maps", CriteriosJson = Crit("ciudad", "=", "Bogota"), ConteoAnterior = 4, FechaSnapshot = now, SortOrder = 2 },
            new() { TenantId = tenantId, Nombre = "Empleados", Descripcion = "Terceros con perfil empleado.", Fuente = "Todos", CriteriosJson = Crit("perfil", "=", "Empleado"), ConteoAnterior = 0, FechaSnapshot = now, SortOrder = 3 },
            new() { TenantId = tenantId, Nombre = "Sector Manufactura", Descripcion = "Empresas del sector manufactura.", Fuente = "LinkedIn", CriteriosJson = Crit("sector", "LIKE", "Manufactura"), ConteoAnterior = 1, FechaSnapshot = now, SortOrder = 4 }
        };
        _db.TerceroFiltros.AddRange(filtros);

        // 6) Prospectos scrapeados (LinkedIn / Maps) sin promover.
        (string Fuente, string Nombre, string Cargo, string Empresa, string Ciudad, string Metrica, string Badge, string Tel, string Correo)[] prospectos =
        {
            ("LinkedIn", "Andrea Cardona", "Gerente de Compras", "Nutresa", "Medellin", "2.340 conexiones", "Hot", "3001112233", "acardona@nutresa.local"),
            ("LinkedIn", "Julian Restrepo", "Director de TI", "Bancolombia", "Medellin", "3.120 conexiones", "Calificado", "3002223344", "jrestrepo@banco.local"),
            ("Maps", "Ferreteria El Tornillo", "Propietario", "Ferreteria El Tornillo", "Bogota", "4.9 estrellas - 89 resenas", "Nuevo", "6013334455", "ventas@eltornillo.local"),
            ("Maps", "Cafe de la Montana", "Administrador", "Cafe de la Montana", "Manizales", "4.7 estrellas - 210 resenas", "Nuevo", "6068889900", "hola@cafemontana.local"),
            ("LinkedIn", "Paola Nunez", "CEO", "Innovatek", "Bogota", "5.400 conexiones", "Hot", "3005556677", "pnunez@innovatek.local"),
            ("LinkedIn", "Carlos Mejia", "Jefe de Logistica", "Coordinadora", "Cali", "1.980 conexiones", "Calificado", "3007778899", "cmejia@coord.local"),
            ("Maps", "TecnoServicios SAS", "Gerente", "TecnoServicios SAS", "Barranquilla", "4.5 estrellas - 54 resenas", "Nuevo", "6053332211", "info@tecnoservicios.local"),
            ("LinkedIn", "Mariana Vega", "Directora Comercial", "Alkosto", "Bogota", "2.760 conexiones", "Hot", "3009990011", "mvega@alkosto.local"),
            ("Maps", "Distribuciones Norte", "Propietario", "Distribuciones Norte", "Cucuta", "4.3 estrellas - 32 resenas", "Nuevo", "6075554433", "contacto@distnorte.local"),
            ("LinkedIn", "Sergio Ospina", "Coordinador de Proyectos", "Sura", "Medellin", "1.450 conexiones", "Calificado", "3002224466", "sospina@sura.local")
        };
        var i2 = 0;
        foreach (var p in prospectos)
        {
            _db.ProspectosScrapeados.Add(new ProspectoScrapeado
            {
                TenantId = tenantId,
                Fuente = p.Fuente,
                NombreCompleto = p.Nombre,
                Cargo = p.Cargo,
                Empresa = p.Empresa,
                Ciudad = p.Ciudad,
                Metrica = p.Metrica,
                Badge = p.Badge,
                Telefono = p.Tel,
                Correo = p.Correo,
                FechaCaptura = now.AddDays(-i2)
            });
            i2++;
        }

        // Un solo SaveChanges para la asignacion de terceros + oportunidades + citas + filtros + prospectos.
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Gestor de Clientes demo sembrado para tenant {Tenant}: 5 columnas, {Ops} oportunidades, {Citas} citas, 5 filtros, 10 prospectos.",
            tenantId, oportunidades.Count, citas.Count);
    }

    /// <summary>
    /// Siembra los campos configurables por ficha por defecto (IsSystem=true) del Directorio
    /// General (000232) para el tenant indicado, si aun no tiene ninguno. Idempotente. Estampa
    /// TenantId explicito. Reusa el catalogo de defaults del servicio (fuente unica).
    /// </summary>
    public async Task EnsureDirectorioFieldDefaultsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await _db.TerceroFieldDefinitions.IgnoreQueryFilters().AnyAsync(f => f.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        _db.TerceroFieldDefinitions.AddRange(
            Ecorex.Application.Directorio.TerceroFieldService.BuildDefaultFields(tenantId));
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Directorio General: campos por defecto sembrados para tenant {Tenant}.", tenantId);
    }

    public async Task EnsureInventoryDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // ---- Bodegas ----
        List<Warehouse> warehouses;
        if (!await _db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.TenantId == tenant.Id, cancellationToken))
        {
            warehouses =
            [
                new Warehouse { TenantId = tenant.Id, Name = "Bodega Central", City = "Bogota", Address = "Calle 100 #15-20", Phone = "6013001000", SortOrder = 0 },
                new Warehouse { TenantId = tenant.Id, Name = "Bodega Norte", City = "Medellin", Address = "Cra 43A #1-50", Phone = "6044004000", SortOrder = 1 }
            ];
            _db.Warehouses.AddRange(warehouses);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            warehouses = await _db.Warehouses.IgnoreQueryFilters()
                .Where(w => w.TenantId == tenant.Id).OrderBy(w => w.SortOrder).ToListAsync(cancellationToken);
        }

        // ---- Marcas ----
        List<Brand> brands;
        if (!await _db.Brands.IgnoreQueryFilters().AnyAsync(b => b.TenantId == tenant.Id, cancellationToken))
        {
            brands =
            [
                new Brand { TenantId = tenant.Id, Name = "Acme", SortOrder = 0 },
                new Brand { TenantId = tenant.Id, Name = "Globex", SortOrder = 1 },
                new Brand { TenantId = tenant.Id, Name = "Umbrella", SortOrder = 2 }
            ];
            _db.Brands.AddRange(brands);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            brands = await _db.Brands.IgnoreQueryFilters()
                .Where(b => b.TenantId == tenant.Id).OrderBy(b => b.SortOrder).ToListAsync(cancellationToken);
        }

        // ---- Grupos + subgrupos ----
        List<ItemGroup> groups;
        List<ItemSubgroup> subgroups;
        if (!await _db.ItemGroups.IgnoreQueryFilters().AnyAsync(g => g.TenantId == tenant.Id, cancellationToken))
        {
            groups =
            [
                new ItemGroup { TenantId = tenant.Id, Name = "Tecnologia", SortOrder = 0 },
                new ItemGroup { TenantId = tenant.Id, Name = "Oficina", SortOrder = 1 }
            ];
            _db.ItemGroups.AddRange(groups);
            await _db.SaveChangesAsync(cancellationToken);

            subgroups =
            [
                new ItemSubgroup { TenantId = tenant.Id, Name = "Computo", GroupId = groups[0].Id, SortOrder = 0 },
                new ItemSubgroup { TenantId = tenant.Id, Name = "Perifericos", GroupId = groups[0].Id, SortOrder = 1 },
                new ItemSubgroup { TenantId = tenant.Id, Name = "Papeleria", GroupId = groups[1].Id, SortOrder = 0 },
                new ItemSubgroup { TenantId = tenant.Id, Name = "Mobiliario", GroupId = groups[1].Id, SortOrder = 1 }
            ];
            _db.ItemSubgroups.AddRange(subgroups);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            groups = await _db.ItemGroups.IgnoreQueryFilters()
                .Where(g => g.TenantId == tenant.Id).OrderBy(g => g.SortOrder).ToListAsync(cancellationToken);
            subgroups = await _db.ItemSubgroups.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenant.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        }

        // ---- Tipos ----
        List<ItemType> types;
        if (!await _db.ItemTypes.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id, cancellationToken))
        {
            types =
            [
                new ItemType { TenantId = tenant.Id, Name = "Producto", SortOrder = 0 },
                new ItemType { TenantId = tenant.Id, Name = "Insumo", SortOrder = 1 },
                new ItemType { TenantId = tenant.Id, Name = "Servicio", SortOrder = 2 }
            ];
            _db.ItemTypes.AddRange(types);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            types = await _db.ItemTypes.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenant.Id).OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);
        }

        // ---- Campos configurables por tipo (ItemFieldDefinition, 000066) ----
        // Se siembran aparte de los items (guard propio), para que la ficha muestre campos segun
        // el tipo. Producto/Insumo/Servicio son los tipos sembrados arriba (por SortOrder 0/1/2).
        if (!await _db.ItemFieldDefinitions.IgnoreQueryFilters().AnyAsync(f => f.TenantId == tenant.Id, cancellationToken))
        {
            ItemType? TypeByName(string name) => types.FirstOrDefault(t => t.Name == name);
            // (tipo, [ (clave, label, tipoCampo, opciones, columna) ])
            var fieldDefs = new (string TypeName, (string Key, string Label, TerceroFieldType Type, string? Options, int Column)[] Fields)[]
            {
                ("Producto",
                [
                    ("material", "Material", TerceroFieldType.Text, null, 1),
                    ("garantia_meses", "Garantia (meses)", TerceroFieldType.Number, null, 1),
                    ("color", "Color", TerceroFieldType.Text, null, 1)
                ]),
                ("Insumo",
                [
                    ("unidad_de_medida", "Unidad de medida", TerceroFieldType.Select, "Unidad\nCaja\nKilogramo\nLitro", 1),
                    ("presentacion", "Presentacion", TerceroFieldType.Text, null, 1)
                ]),
                ("Servicio",
                [
                    ("modalidad", "Modalidad", TerceroFieldType.Select, "Presencial\nRemoto\nMixto", 1),
                    ("duracion_estimada", "Duracion estimada", TerceroFieldType.Text, null, 1)
                ])
            };
            foreach (var (typeName, fields) in fieldDefs)
            {
                var type = TypeByName(typeName);
                if (type is null) { continue; }
                var order = 0;
                foreach (var (key, label, ftype, options, column) in fields)
                {
                    _db.ItemFieldDefinitions.Add(new ItemFieldDefinition
                    {
                        TenantId = tenant.Id,
                        ItemTypeId = type.Id,
                        FieldKey = key,
                        Label = label,
                        FieldType = ftype,
                        Options = options,
                        Column = column,
                        SortOrder = order++,
                        IsSystem = true
                    });
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        // ---- Items (con stock e imagenes) ----
        if (await _db.Items.IgnoreQueryFilters().AnyAsync(i => i.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        ItemSubgroup? SubgroupOf(ItemGroup g, int idx) =>
            subgroups.Where(s => s.GroupId == g.Id).Skip(idx).FirstOrDefault();

        // (Nombre, Sku, Precio, marcaIdx, grupoIdx, subIdx, tipoIdx, stockCentral, stockNorte)
        (string Name, string Sku, decimal Price, int Brand, int Group, int Sub, int Type, int StockA, int StockB)[] defs =
        {
            ("Laptop Pro 14", "ITM000001", 4200000m, 0, 0, 0, 0, 12, 5),
            ("Mouse Inalambrico", "ITM000002", 85000m, 1, 0, 1, 0, 40, 0),
            ("Teclado Mecanico", "ITM000003", 220000m, 1, 0, 1, 0, 0, 0),
            ("Monitor 27\"", "ITM000004", 950000m, 2, 0, 1, 0, 7, 3),
            ("Resma Papel Carta", "ITM000005", 18000m, 0, 1, 2, 1, 120, 60),
            ("Silla Ergonomica", "ITM000006", 680000m, 2, 1, 3, 0, 4, 0),
            ("Escritorio Modular", "ITM000007", 540000m, 0, 1, 3, 0, 0, 2),
            ("Soporte Tecnico Mensual", "ITM000008", 300000m, 1, 0, 0, 2, 0, 0)
        };

        var placeholderImages = new[]
        {
            "https://placehold.co/400x400?text=Item",
            "https://placehold.co/400x400?text=Foto+2"
        };

        var items = new List<Item>();
        foreach (var d in defs)
        {
            var group = groups[d.Group % groups.Count];
            var sub = SubgroupOf(group, d.Sub % 2);
            var item = new Item
            {
                TenantId = tenant.Id,
                Sku = d.Sku,
                Name = d.Name,
                Price = d.Price,
                BrandId = brands[d.Brand % brands.Count].Id,
                GroupId = group.Id,
                SubgroupId = sub?.Id,
                ItemTypeId = types[d.Type % types.Count].Id,
                Description = $"Item demo {d.Name}."
            };
            items.Add(item);
        }
        _db.Items.AddRange(items);
        await _db.SaveChangesAsync(cancellationToken);

        // Avanza el consecutivo "ITM" para que un SKU generado desde la UI no colisione con los
        // SKUs demo (ITM000001..ITM000008): el siguiente emitido sera ITM000009.
        if (!await _db.TenantSequences.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenant.Id && s.Code == "ITM", cancellationToken))
        {
            _db.TenantSequences.Add(new TenantSequence
            {
                TenantId = tenant.Id,
                Code = "ITM",
                NextValue = items.Count + 1
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var central = warehouses[0];
        var norte = warehouses.Count > 1 ? warehouses[1] : warehouses[0];
        for (var idx = 0; idx < items.Count; idx++)
        {
            var (a, b) = (defs[idx].StockA, defs[idx].StockB);
            if (a > 0)
            {
                _db.ItemStocks.Add(new ItemStock { TenantId = tenant.Id, ItemId = items[idx].Id, WarehouseId = central.Id, Stock = a });
            }
            if (b > 0 && norte.Id != central.Id)
            {
                _db.ItemStocks.Add(new ItemStock { TenantId = tenant.Id, ItemId = items[idx].Id, WarehouseId = norte.Id, Stock = b });
            }
            // 1-2 imagenes placeholder por item; la primera queda como principal (portada).
            _db.ItemImages.Add(new ItemImage { TenantId = tenant.Id, ItemId = items[idx].Id, Url = placeholderImages[0], FileName = "principal.png", SortOrder = 0, EsPrincipal = true, Texto = "Oferta" });
            if (idx % 2 == 0)
            {
                _db.ItemImages.Add(new ItemImage { TenantId = tenant.Id, ItemId = items[idx].Id, Url = placeholderImages[1], FileName = "secundaria.png", SortOrder = 1 });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seed de inventario creado para {Tenant}: {Warehouses} bodegas, {Brands} marcas, {Groups} grupos, {Subgroups} subgrupos, {Types} tipos, {Items} items.",
            tenant.Name, warehouses.Count, brands.Count, groups.Count, subgroups.Count, types.Count, items.Count);
    }

    /// <summary>
    /// Plantillas HSM de WhatsApp demo (ADR-0029) para el tenant demo SKY SYSTEM. Idempotente
    /// (guard por tenant). Requiere una linea de WhatsApp: si no hay ninguna, siembra una linea
    /// Cloud demo (sin credenciales reales) para el FK. Crea 3 plantillas en categorias y estados
    /// distintos. Ningun envio real: Submit es un stub (ver WhatsAppTemplateService).
    /// </summary>
    public async Task EnsureWhatsAppTemplatesDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.WhatsAppTemplates.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        // Linea para el FK: reusa una existente del tenant demo o siembra una Cloud demo (sin
        // credenciales reales; solo un WABA id de ejemplo para referencia).
        var line = await _db.WhatsAppLines.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenant.Id)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            line = new WhatsAppLine
            {
                TenantId = tenant.Id,
                InstanceName = "Linea demo SKY",
                PhoneNumber = "573001112233",
                Status = WhatsAppLineStatus.Created,
                Provider = WhatsAppProvider.Cloud,
                CloudBusinessAccountId = "000000000000000"
            };
            _db.WhatsAppLines.Add(line);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var templates = new[]
        {
            new WhatsAppTemplate
            {
                TenantId = tenant.Id,
                Name = "bienvenida_cliente",
                Language = "es",
                Category = WhatsAppTemplateCategory.Utility,
                BodyText = "Hola {{cliente}}, gracias por contactar a {{empresa}}. Un asesor te atendera en breve.",
                FooterText = "Equipo SKY SYSTEM",
                VariablesJson = "[{\"Token\":\"cliente\",\"Example\":\"Juan Perez\"},{\"Token\":\"empresa\",\"Example\":\"SKY SYSTEM\"}]",
                Provider = line.Provider,
                WhatsAppLineId = line.Id,
                WabaId = line.CloudBusinessAccountId,
                Status = WhatsAppTemplateStatus.Draft
            },
            new WhatsAppTemplate
            {
                TenantId = tenant.Id,
                Name = "recordatorio_actividad",
                Language = "es",
                Category = WhatsAppTemplateCategory.Utility,
                HeaderType = WhatsAppTemplateHeaderType.Text,
                HeaderText = "Recordatorio",
                BodyText = "Hola {{cliente}}, te recordamos la actividad {{codigo}} programada para el {{fecha}}.",
                VariablesJson = "[{\"Token\":\"cliente\",\"Example\":\"Juan Perez\"},{\"Token\":\"codigo\",\"Example\":\"T00042\"},{\"Token\":\"fecha\",\"Example\":\"15 de julio\"}]",
                Provider = line.Provider,
                WhatsAppLineId = line.Id,
                WabaId = line.CloudBusinessAccountId,
                Status = WhatsAppTemplateStatus.Submitted,
                SubmittedAt = now
            },
            new WhatsAppTemplate
            {
                TenantId = tenant.Id,
                Name = "promo_mensual",
                Language = "es",
                Category = WhatsAppTemplateCategory.Marketing,
                BodyText = "{{cliente}}, aprovecha nuestras novedades de este mes en {{empresa}}. Responde para conocer mas.",
                VariablesJson = "[{\"Token\":\"cliente\",\"Example\":\"Juan Perez\"},{\"Token\":\"empresa\",\"Example\":\"SKY SYSTEM\"}]",
                Provider = line.Provider,
                WhatsAppLineId = line.Id,
                WabaId = line.CloudBusinessAccountId,
                Status = WhatsAppTemplateStatus.Approved,
                SubmittedAt = now,
                ReviewedAt = now
            }
        };
        _db.WhatsAppTemplates.AddRange(templates);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seed de plantillas WhatsApp creado para {Tenant}: {Count} plantillas.",
            tenant.Name, templates.Length);
    }

    // ---- Menu configurable por perfil (Ola 1) ----

    public const string MenuCompletoUserEmail = "completo@sky-system.local";
    public const string MenuSimpleUserEmail = "simple@sky-system.local";
    public const string MenuViewCompletoName = "Completo";
    public const string MenuViewSimpleName = "Simple";

    /// <summary>
    /// Siembra el menu configurable del tenant demo (SKY SYSTEM): la vista "Completo" (IsDefault)
    /// que transcribe 1:1 el menu actual del bloque _showTenant de NavMenu.razor, una vista
    /// "Simple" reducida, y 2 usuarios demo asignados a cada vista (completo@ y simple@). Idempotente:
    /// solo corre si el tenant demo aun no tiene vistas. Solo Development.
    /// </summary>
    /// <summary>
    /// Estado por ruta: los stubs modulo/... se marcan InDevelopment (metadata; no cambia el render).
    /// La comparten el arbol canonico ("Completo") y la vista "Simple" del demo.
    /// </summary>
    private static MenuNodeState StateFor(string? route)
        => route is not null && route.StartsWith("modulo/", StringComparison.Ordinal)
            ? MenuNodeState.InDevelopment
            : MenuNodeState.Ready;

    /// <summary>
    /// Siembra la vista de menu "Completo" (por defecto, arbol canonico del workspace) para un tenant
    /// que aun NO tiene ninguna vista. Idempotente. La usan el seeder del demo Y el ALTA DE TENANTS
    /// (IMenuProvisioningService): asi ningun cliente nuevo nace sin menu.
    /// </summary>
    public async Task EnsureDefaultMenuAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await _db.MenuViews.IgnoreQueryFilters()
                .AnyAsync(v => v.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        var completo = new MenuView
        {
            TenantId = tenantId,
            Name = MenuViewCompletoName,
            Description = "Menu completo del workspace (todas las secciones).",
            IsDefault = true,
            SortOrder = 0
        };
        _db.MenuViews.Add(completo);

        var nodes = new List<MenuNode>();
        var order = 0;

        MenuNode Add(MenuNodeKind kind, string name, Guid? parentId, string? route,
            string? iconKey = null, string? legacyCode = null, MenuNodeState state = MenuNodeState.Ready)
        {
            var node = new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = completo.Id,
                ParentId = parentId,
                Kind = kind,
                Name = name,
                IconKey = iconKey,
                LegacyCode = legacyCode,
                Route = route,
                State = state,
                IsVisible = true,
                SortOrder = order++
            };
            nodes.Add(node);
            return node;
        }

        MenuNode Item(Guid parentId, string name, string route, string? legacyCode = null)
            => Add(MenuNodeKind.Item, name, parentId, route, legacyCode: legacyCode, state: StateFor(route));

        // ---- Quick links (antes de "Modulos") ----
        Add(MenuNodeKind.QuickLink, "Inicio", null, "inicio", iconKey: "home");
        Add(MenuNodeKind.QuickLink, "Anuncios", null, "anuncios", iconKey: "megaphone");

        // ---- Seccion: Mis Procesos (slug misproc) ----
        var misproc = Add(MenuNodeKind.Section, "Mis Procesos", null, "misproc", iconKey: "list");
        // ADR-0038: se retiro el item "Mis pasos" (000637). El runtime de flujos vive DENTRO de la
        // tarea (seccion Flujo del detalle) y los pasos pendientes se descubren en el TABLERO.
        // La creacion se unifico al wizard (tableros/conceptos): se retiro "Crear una actividad".
        // El grupo "Procesos" (categorias con flujo) lo despliega NavMenu por IsProcessGroup.
        Item(misproc.Id, "Proyectos", "proyectos", "000042");
        Item(misproc.Id, "Administrar actividades", "actividades", "000636");
        Item(misproc.Id, "Programar actividad", "programar-actividad", "000889");

        // ---- Seccion: Negocio (slug nego) ----
        var nego = Add(MenuNodeKind.Section, "Negocio", null, "nego", iconKey: "briefcase");
        Item(nego.Id, "Directorio General", "directorio-general", "000232");
        Item(nego.Id, "Cargador de contactos", "cargador-contactos", "000740");

        // ---- Seccion: Automatizacion (slug auto) ----
        var auto = Add(MenuNodeKind.Section, "Automatizacion", null, "auto", iconKey: "automation");
        Item(auto.Id, "Flujos del proceso", "flujos", "000291");
        Item(auto.Id, "Formularios", "formularios", "000131");
        Item(auto.Id, "Power BI Service", "modulo/power-bi-service", "000788");

        // ---- Seccion: Sistema - Inventarios (slug inv) ----
        var inv = Add(MenuNodeKind.Section, "Sistema \u00b7 Inventarios", null, "inv", iconKey: "cube");
        // Los cinco catalogos (bodegas 000556, grupos 000506, marcas 000502, subgrupos 000606
        // y tipos 000498) viven ahora en UN modulo con tarjetas + modal. Sus rutas individuales
        // siguen existiendo; lo que se retira es su entrada del menu.
        Item(inv.Id, "Configuracion", "inventario-configuracion", "000556");
        Item(inv.Id, "Items de inventarios", "inventario-items", "000066");

        // ---- Seccion: Sistema - Actividades (slug act) ----
        var act = Add(MenuNodeKind.Section, "Sistema \u00b7 Actividades", null, "act", iconKey: "check-square");
        Item(act.Id, "Prioridades", "modulo/prioridades", "000621");
        Item(act.Id, "Tipos de proyecto", "modulo/tipos-de-proyecto", "000690");
        Item(act.Id, "Conceptos", "conceptos", "000270");
        Item(act.Id, "Estados", "modulo/estados", "000653");

        // ---- Seccion: Sistema - CRM (slug syscrm) ----
        var syscrm = Add(MenuNodeKind.Section, "Sistema \u00b7 CRM", null, "syscrm", iconKey: "users");
        Item(syscrm.Id, "Conceptos actividades", "modulo/conceptos-actividades", "000125");
        Item(syscrm.Id, "Estados", "modulo/estados-crm", "000272");
        Item(syscrm.Id, "Perfiles cliente/prov", "modulo/perfiles-cliente-prov", "000166");
        Item(syscrm.Id, "Servicios o productos", "modulo/servicios-o-productos", "000249");
        Item(syscrm.Id, "Tipos de empresas", "modulo/tipos-de-empresas", "000231");
        Item(syscrm.Id, "Vendedores", "modulo/vendedores", "000124");
        Item(syscrm.Id, "Origen clientes", "modulo/origen-clientes", "000324");
        Item(syscrm.Id, "Grupos de actividades", "modulo/grupos-de-actividades", "000126");

        // ---- Seccion: Sistema - General (slug gen) ----
        var gen = Add(MenuNodeKind.Section, "Sistema \u00b7 General", null, "gen", iconKey: "gear");
        // "Configuracion de entidad" legacy = el plan/cuenta del tenant -> se renombra a "Mi cuenta".
        // La verdadera configuracion de la entidad (agencias/areas/sucursales) es el modulo nuevo.
        Item(gen.Id, "Mi cuenta", "mi-cuenta", "000615");
        Item(gen.Id, "Configuracion de la entidad", "configuracion-entidad", "000616");
        // "Actividades" (indice de tableros) retirado de Sistema.General: es redundante con
        // "Mis Procesos > Administrar actividades". Los tableros se referencian desde Conceptos.
        Item(gen.Id, "Extraccion de datos", "extraccion-datos", "000730");
        Item(gen.Id, "Plantillas", "plantillas", "000893");
        Item(gen.Id, "Dependencias", "dependencias", "000850");
        // Administrador de Menu (menu configurable por perfil, Ola 2, ADR-0030). Reutiliza el
        // code 000194 (antes "Roles y permisos", que era un stub modulo/...) apuntandolo a la
        // pagina real /configuracion-menu; queda como Ready por ser una pantalla implementada.
        Item(gen.Id, "Administrador de Menu", "configuracion-menu", "000194");
        // Administracion de usuarios del tenant (modulo 000073, ADR-0031): pagina real /admin-usuarios.
        Item(gen.Id, "Administracion de usuarios", "admin-usuarios", "000073");
        // Roles y permisos (Ola B1, ADR-0032): matriz de permisos por rol. LegacyCode libre 000198
        // (no colisiona con 000194 = Administrador de Menu). Pagina real /roles-permisos (Ready).
        Item(gen.Id, "Roles y permisos", "roles-permisos", "000198");
        // Contenedor de datos (modelos dinamicos + importacion): pagina real /contenedor-datos.
        Item(gen.Id, "Contenedor de datos", "contenedor-datos", "000920");

        // ---- Seccion: Sistema - Desarrollo (slug dev) ----
        var dev = Add(MenuNodeKind.Section, "Sistema \u00b7 Desarrollo", null, "dev", iconKey: "gear");
        Item(dev.Id, "Autocompletado formularios", "modulo/autocompletado-formularios", "000801");
        Item(dev.Id, "Modulos web", "modulos-web", "000109");
        Item(dev.Id, "Notificaciones", "modulo/notificaciones-config", "000288");
        Item(dev.Id, "Objetos del sistema", "modulo/objetos-del-sistema", "000137");
        Item(dev.Id, "Parametros XML", "modulo/parametros-xml", "000057");
        Item(dev.Id, "Reglas", "reglas", "000802");
        Item(dev.Id, "Reportes", "metricas", "000119");
        Item(dev.Id, "Servicios web", "modulo/servicios-web", "000053");
        // Consola SQL (000077): pagina real /sql-admin (antes stub modulo/...). Ready.
        Item(dev.Id, "SQL Admin", "sql-admin", "000077");
        Item(dev.Id, "Tipos de documentos \u00b7 Consecutivos", "modulo/consecutivos", "000136");

        // ---- Seccion: Infraestructura IA (slug ia) ----
        var ia = Add(MenuNodeKind.Section, "Infraestructura IA", null, "ia", iconKey: "robot");
        Item(ia.Id, "Agentes", "agentes", "000867");
        Item(ia.Id, "Agentes Colmena", "agentes-colmena", "000868");
        Item(ia.Id, "Lineas WhatsApp", "lineas");
        Item(ia.Id, "Conversaciones", "conversaciones");
        Item(ia.Id, "Bitacora del agente", "bitacora-agente");
        Item(ia.Id, "Plantillas WhatsApp", "plantillas-whatsapp");

        // ---- Seccion: CRM (heredado) (slug crm) ----
        var crm = Add(MenuNodeKind.Section, "CRM (heredado)", null, "crm", iconKey: "crm");
        Item(crm.Id, "Asesores", "asesores");
        Item(crm.Id, "Automatizaciones", "automatizaciones");
        Item(crm.Id, "Lista negra", "lista-negra");

        _db.MenuNodes.AddRange(nodes);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Backfill IDEMPOTENTE (ADR-0045): asegura el item "Agentes Colmena" bajo la seccion Infraestructura
    /// IA en los menus YA sembrados. Los menus NUEVOS ya lo traen via <see cref="EnsureDefaultMenuAsync"/>;
    /// esto cubre las BD existentes (que se sembraron antes del cambio). Corre en cada arranque; no duplica.
    /// </summary>
    public async Task EnsureAgentesColmenaMenuItemAsync(CancellationToken cancellationToken = default)
    {
        var iaSections = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.Kind == MenuNodeKind.Section && n.Route == "ia")
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var ia in iaSections)
        {
            var exists = await _db.MenuNodes.IgnoreQueryFilters()
                .AnyAsync(n => n.MenuViewId == ia.MenuViewId && n.Route == "agentes-colmena", cancellationToken);
            if (exists) { continue; }

            var maxSort = await _db.MenuNodes.IgnoreQueryFilters()
                .Where(n => n.ParentId == ia.Id)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync(cancellationToken) ?? ia.SortOrder;

            _db.MenuNodes.Add(new MenuNode
            {
                TenantId = ia.TenantId,
                MenuViewId = ia.MenuViewId,
                ParentId = ia.Id,
                Kind = MenuNodeKind.Item,
                Name = "Agentes Colmena",
                IconKey = null,
                LegacyCode = "000868",
                Route = "agentes-colmena",
                State = MenuNodeState.Ready,
                IsVisible = true,
                SortOrder = maxSort + 1
            });
            added = true;
        }

        if (added) { await _db.SaveChangesAsync(cancellationToken); }
    }

    public async Task EnsureMenuConfigDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.MenuViews.IgnoreQueryFilters().AnyAsync(v => v.TenantId == tenant.Id, cancellationToken))
        {
            // El seeder es idempotente por existencia de vistas: no re-siembra. Pero para que un
            // demo YA sembrado refleje las paginas reales que se fueron implementando, reconcilia
            // (sin recrear la vista) los nodos cuyo route/state/name cambiaron. Ver ADR-0031.
            await ReconcileMenuNodesAsync(tenant.Id, cancellationToken);
            return;
        }

        // 1) Vista "Completo" (por defecto): arbol canonico COMPARTIDO con el alta de tenants
        //    (EnsureDefaultMenuAsync / IMenuProvisioningService), para no duplicarlo ni que derive.
        await EnsureDefaultMenuAsync(tenant.Id, cancellationToken);
        var completo = await _db.MenuViews.IgnoreQueryFilters()
            .FirstAsync(v => v.TenantId == tenant.Id && v.Name == MenuViewCompletoName, cancellationToken);

        // 2) Vista "Simple": subconjunto pequeno (Inicio + Anuncios; Mis Procesos con 3 items;
        //    Inventarios con 1 item; Automatizacion con 1 item). Se construye directa.
        var simple = new MenuView
        {
            TenantId = tenant.Id,
            Name = MenuViewSimpleName,
            Description = "Menu reducido para perfiles operativos.",
            IsDefault = false,
            SortOrder = 1
        };
        _db.MenuViews.Add(simple);

        var simpleNodes = new List<MenuNode>();
        var simpleOrder = 0;

        MenuNode AddS(MenuNodeKind kind, string name, Guid? parentId, string? route,
            string? iconKey = null, string? legacyCode = null)
        {
            var node = new MenuNode
            {
                TenantId = tenant.Id,
                MenuViewId = simple.Id,
                ParentId = parentId,
                Kind = kind,
                Name = name,
                IconKey = iconKey,
                LegacyCode = legacyCode,
                Route = route,
                State = StateFor(route),
                IsVisible = true,
                SortOrder = simpleOrder++
            };
            simpleNodes.Add(node);
            return node;
        }

        AddS(MenuNodeKind.QuickLink, "Inicio", null, "inicio", iconKey: "home");
        AddS(MenuNodeKind.QuickLink, "Anuncios", null, "anuncios", iconKey: "megaphone");

        var sMisproc = AddS(MenuNodeKind.Section, "Mis Procesos", null, "misproc", iconKey: "list");
        // ADR-0038: item "Mis pasos" (000637) retirado; el runtime va en la tarea, descubrimiento en el tablero.
        // "Crear una actividad" retirado: la creacion se unifico al wizard (tableros/conceptos).
        AddS(MenuNodeKind.Item, "Administrar actividades", sMisproc.Id, "actividades", legacyCode: "000636");
        AddS(MenuNodeKind.Item, "Proyectos", sMisproc.Id, "proyectos", legacyCode: "000042");

        var sInv = AddS(MenuNodeKind.Section, "Sistema \u00b7 Inventarios", null, "inv", iconKey: "cube");
        AddS(MenuNodeKind.Item, "Items de inventarios", sInv.Id, "inventario-items", legacyCode: "000066");

        var sAuto = AddS(MenuNodeKind.Section, "Automatizacion", null, "auto", iconKey: "automation");
        AddS(MenuNodeKind.Item, "Flujos del proceso", sAuto.Id, "flujos", legacyCode: "000291");

        _db.MenuNodes.AddRange(simpleNodes);
        await _db.SaveChangesAsync(cancellationToken);

        // 3) 2 usuarios demo asignados a cada vista (patron del seeder: PlatformUser + TenantUser).
        await EnsureMenuDemoUserAsync(tenant.Id, MenuCompletoUserEmail, "Perfil Completo", completo.Id, cancellationToken);
        await EnsureMenuDemoUserAsync(tenant.Id, MenuSimpleUserEmail, "Perfil Simple", simple.Id, cancellationToken);

        var completoNodes = await _db.MenuNodes.IgnoreQueryFilters()
            .CountAsync(n => n.MenuViewId == completo.Id, cancellationToken);
        _logger.LogInformation(
            "Seed del menu configurable creado para {Tenant}: vista Completo ({CompletoNodes} nodos) + vista Simple ({SimpleNodes} nodos), usuarios {UserA}/{UserB}.",
            tenant.Name, completoNodes, simpleNodes.Count, MenuCompletoUserEmail, MenuSimpleUserEmail);
    }

    /// <summary>
    /// Reconciliacion idempotente de nodos del menu para un demo YA sembrado (ADR-0031). No recrea
    /// vistas: solo actualiza (si difieren) el Route/State/Name de los nodos por LegacyCode que
    /// pasaron de stub a pagina real. Tenant-scoped (usa IgnoreQueryFilters + filtro por TenantId).
    /// </summary>
    private async Task ReconcileMenuNodesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // (LegacyCode, Route esperado, Name esperado o null=no tocar el nombre).
        var expected = new (string LegacyCode, string Route, string? Name)[]
        {
            ("000073", "admin-usuarios", "Administracion de usuarios"),
            ("000194", "configuracion-menu", "Administrador de Menu"),
            // Motor de programaciones (P1): el nodo existia como stub "modulo/programar-actividad".
            // Los tenants YA sembrados (prod) se corrigen aqui al arrancar -> pagina real + Ready.
            ("000889", "programar-actividad", "Programar actividad"),
            // Consola SQL (000077): antes stub "modulo/sql-admin" -> pagina real /sql-admin + Ready.
            ("000077", "sql-admin", "SQL Admin"),
        };

        var codes = expected.Select(e => e.LegacyCode).ToArray();
        var nodes = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.LegacyCode != null && codes.Contains(n.LegacyCode))
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var node in nodes)
        {
            var target = expected.First(e => e.LegacyCode == node.LegacyCode);
            if (node.Route != target.Route)
            {
                node.Route = target.Route;
                changed++;
            }
            if (target.Name is not null && node.Name != target.Name)
            {
                node.Name = target.Name;
                changed++;
            }
            if (node.State != MenuNodeState.Ready)
            {
                node.State = MenuNodeState.Ready;
                changed++;
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Reconciliacion del menu para el tenant {Tenant}: {Changed} campos ajustados en nodos 000073/000194.",
                tenantId, changed);
        }

        // Alta idempotente del item "Roles y permisos" (Ola B1, ADR-0032) en las vistas ya
        // sembradas que aun no lo tienen. Se cuelga de la seccion "Sistema . General" (slug gen)
        // de cada vista; si la vista no tiene esa seccion, se omite (vistas reducidas como Simple).
        await EnsureMenuItemInSectionAsync(
            tenantId, sectionSlug: "gen", route: "roles-permisos",
            name: "Roles y permisos", legacyCode: "000198", cancellationToken);

        // ADR-0038: RETIRO idempotente del item "Mis pasos" (000637) de los tenants ya sembrados.
        // El runtime de flujos vive DENTRO de la tarea (seccion Flujo) y los pasos pendientes se
        // descubren en el TABLERO ("mis pendientes"); no hay bandeja/pagina aparte.
        await RemoveMenuItemByRouteAsync(tenantId, route: "mis-pasos", cancellationToken);

        // Reorg del menu "Mis Procesos": la creacion se unifico al wizard -> se retira "Crear una
        // actividad"; y el subgrupo estatico "Comercial" (sg-comercial) tambien. Idempotente, todos
        // los tenants. El grupo "Procesos" (categorias con flujo) lo despliega NavMenu por IsProcessGroup.
        await RemoveMenuItemByRouteAsync(tenantId, route: "crear-actividad", cancellationToken);
        await RemoveMenuSubtreeByRouteAsync(tenantId, route: "sg-comercial", cancellationToken);
        // "Actividades" (route=actividades) redundante en Sistema.General (gen); se conserva la de
        // "Mis Procesos" (misproc). Scopeado por seccion para no borrar la buena.
        await RemoveMenuItemFromSectionAsync(tenantId, sectionSlug: "gen", route: "actividades", cancellationToken);

        // Alta idempotente del item "Contenedor de datos" (modelos dinamicos + importacion) en la
        // seccion "Sistema . General" (slug gen) de cada vista ya sembrada que aun no lo tenga.
        await EnsureMenuItemInSectionAsync(
            tenantId, sectionSlug: "gen", route: "contenedor-datos",
            name: "Contenedor de datos", legacyCode: "000920", cancellationToken);

        // Alta idempotente del item "Directorio General" (000232, CRM de terceros: crear/editar
        // empresas, personas y contactos) en la seccion "Negocio" (slug nego). El feature 83100d9 solo
        // lo dejo en el seed inicial, asi que los tenants ya sembrados nunca lo recibieron -> se repone.
        await EnsureMenuItemInSectionAsync(
            tenantId, sectionSlug: "nego", route: "directorio-general",
            name: "Directorio General", legacyCode: "000232", cancellationToken);

        // Reorg "Negocio": se retiran "Creacion de clientes" y "Seguimiento de clientes" (modulos
        // legacy stub); la gestion de terceros queda unificada en "Directorio General" + "Cargador
        // de contactos". Scopeado por seccion (nego) para no tocar rutas homonimas de otras secciones.
        await RemoveMenuItemFromSectionAsync(tenantId, sectionSlug: "nego", route: "modulo/creacion-de-clientes", cancellationToken);
        await RemoveMenuItemFromSectionAsync(tenantId, sectionSlug: "nego", route: "modulo/seguimiento-de-clientes", cancellationToken);

        await EnsureInventarioConfigMenuAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// Reorg "Sistema - Inventarios": los cinco catalogos (bodegas, marcas, grupos, subgrupos y
    /// tipos) ocupaban cinco entradas del menu para configuraciones que se tocan de vez en cuando.
    /// Ahora viven en UN modulo con tarjetas + modal (/inventario-configuracion) que reusa los
    /// mismos componentes; las rutas individuales siguen existiendo.
    /// Publico para poder dispararlo puntualmente contra un tenant ya sembrado, donde la
    /// reconciliacion completa no corre (dev con Ecorex:SkipDemoSeed). Idempotente.
    /// </summary>
    public async Task EnsureInventarioConfigMenuAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Primero el alta y despues las bajas: si esto se interrumpe a medias, la seccion nunca
        // queda sin acceso a los catalogos.
        await EnsureMenuItemInSectionAsync(
            tenantId, sectionSlug: "inv", route: "inventario-configuracion",
            name: "Configuracion", legacyCode: "000556", cancellationToken);

        foreach (var ruta in new[]
        {
            "inventario-bodegas", "inventario-marcas",
            "inventario-grupos", "inventario-subgrupos", "inventario-tipos"
        })
        {
            await RemoveMenuItemFromSectionAsync(tenantId, sectionSlug: "inv", route: ruta, cancellationToken);
        }
    }

    /// <summary>
    /// Alta idempotente de un Item hoja dentro de la seccion (por slug/route) de cada vista del
    /// tenant que aun no tenga ese Route. Tenant-scoped (IgnoreQueryFilters + filtro por TenantId).
    /// Lo usa la reconciliacion para propagar items nuevos a demos ya sembrados.
    /// </summary>
    private async Task EnsureMenuItemInSectionAsync(
        Guid tenantId, string sectionSlug, string route, string name, string? legacyCode,
        CancellationToken cancellationToken)
    {
        var views = await _db.MenuViews.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var viewId in views)
        {
            var alreadyThere = await _db.MenuNodes.IgnoreQueryFilters()
                .AnyAsync(n => n.MenuViewId == viewId && n.Route == route, cancellationToken);
            if (alreadyThere) { continue; }

            var section = await _db.MenuNodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.MenuViewId == viewId
                    && n.Kind == MenuNodeKind.Section && n.Route == sectionSlug, cancellationToken);
            if (section is null) { continue; }

            var nextOrder = await _db.MenuNodes.IgnoreQueryFilters()
                .Where(n => n.MenuViewId == viewId && n.ParentId == section.Id)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync(cancellationToken);

            _db.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = viewId,
                ParentId = section.Id,
                Kind = MenuNodeKind.Item,
                Name = name,
                Route = route,
                LegacyCode = legacyCode,
                State = MenuNodeState.Ready,
                IsVisible = true,
                SortOrder = (nextOrder ?? -1) + 1
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Reconciliacion del menu para el tenant {Tenant}: item '{Name}' ({Route}) agregado a {Count} vista(s).",
                tenantId, name, route, added);
        }
    }

    /// <summary>
    /// Retiro idempotente de un item de menu por su ruta en TODAS las vistas del tenant (ADR-0038,
    /// caso "Mis pasos"). Los MenuNode son configuracion regenerable, no un agregado de negocio: el
    /// borrado es simetrico al alta de EnsureMenuItemInSectionAsync.
    /// </summary>
    private async Task RemoveMenuItemByRouteAsync(
        Guid tenantId, string route, CancellationToken cancellationToken)
    {
        var stale = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Route == route && n.Kind == MenuNodeKind.Item)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) { return; }

        _db.MenuNodes.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Reconciliacion del menu para el tenant {Tenant}: item '{Route}' RETIRADO de {Count} vista(s) (ADR-0038).",
            tenantId, route, stale.Count);
    }

    /// <summary>
    /// Retiro idempotente de un nodo de menu (cualquier Kind) y TODA su descendencia, por ruta, en
    /// todas las vistas del tenant. Para quitar subgrupos con hijos (p.ej. "Comercial"/sg-comercial).
    /// </summary>
    private async Task RemoveMenuSubtreeByRouteAsync(
        Guid tenantId, string route, CancellationToken cancellationToken)
    {
        var toDelete = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Route == route)
            .Select(n => n.Id).ToListAsync(cancellationToken);
        if (toDelete.Count == 0) { return; }

        // Recolectar descendientes por niveles (el arbol de menu es superficial).
        var frontier = new List<Guid>(toDelete);
        while (frontier.Count > 0)
        {
            var kids = await _db.MenuNodes.IgnoreQueryFilters()
                .Where(n => n.TenantId == tenantId && n.ParentId != null && frontier.Contains(n.ParentId.Value))
                .Select(n => n.Id).ToListAsync(cancellationToken);
            kids = kids.Where(k => !toDelete.Contains(k)).ToList();
            if (kids.Count == 0) { break; }
            toDelete.AddRange(kids);
            frontier = kids;
        }

        var nodes = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => toDelete.Contains(n.Id)).ToListAsync(cancellationToken);
        _db.MenuNodes.RemoveRange(nodes);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Reconciliacion del menu para el tenant {Tenant}: subarbol '{Route}' RETIRADO ({Count} nodos).",
            tenantId, route, nodes.Count);
    }

    /// <summary>
    /// Retiro idempotente de un item por ruta PERO solo bajo una seccion concreta (por su slug), para
    /// no borrar otros items con la misma ruta en otras secciones (p.ej. "actividades" existe en
    /// Sistema.General y en Mis Procesos; aqui solo se quita el de Sistema.General).
    /// </summary>
    private async Task RemoveMenuItemFromSectionAsync(
        Guid tenantId, string sectionSlug, string route, CancellationToken cancellationToken)
    {
        var stale = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Route == route && n.Kind == MenuNodeKind.Item
                && n.ParentId != null
                && _db.MenuNodes.Any(s => s.Id == n.ParentId && s.Route == sectionSlug))
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) { return; }

        _db.MenuNodes.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Reconciliacion del menu para el tenant {Tenant}: item '{Route}' de la seccion '{Sec}' RETIRADO ({Count}).",
            tenantId, route, sectionSlug, stale.Count);
    }

    private async Task EnsureMenuDemoUserAsync(
        Guid tenantId, string email, string displayName, Guid menuViewId, CancellationToken cancellationToken)
    {
        var existing = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken);
        if (existing is not null)
        {
            // Idempotente: asegura la asignacion de vista aunque el usuario ya existiera.
            if (existing.MenuViewId != menuViewId)
            {
                existing.MenuViewId = menuViewId;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var platformUser = new PlatformUser
        {
            Email = email,
            EmailVerified = true,
            DisplayName = displayName,
            Status = PlatformUserStatus.Active,
            PasswordHash = _hasher.Hash(TenantUsersPassword)
        };
        _db.PlatformUsers.Add(platformUser);
        _db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = platformUser.Id,
            Email = email,
            TenantRole = TenantRole.Advisor,
            Status = PlatformUserStatus.Active,
            MenuViewId = menuViewId
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public const string RolAdministradorName = "Administrador";
    public const string RolAsesorLimitadoName = "Asesor limitado";

    /// <summary>
    /// Siembra los roles de permisos del tenant demo (Ola B1, ADR-0032): el rol de sistema
    /// "Administrador" (IsSystem, todos los modulos con Ver/Crear/Editar/Eliminar) y un rol demo
    /// "Asesor limitado" (solo Ver en la mayoria + Crear en tareas/inventario, sin Eliminar), y
    /// asigna "Asesor limitado" al usuario simple@sky-system.local. Idempotente por existencia
    /// del rol Administrador. El catalogo de modulos sale de los Item Ready de la vista IsDefault
    /// del tenant (misma fuente que RolService.GetModuleCatalogAsync). Solo Development.
    /// </summary>
    public async Task EnsureRolesDemoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        var rolesExist = await _db.Roles.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenant.Id && r.IsSystem, cancellationToken);

        // Catalogo = Route de los Item Ready de la vista IsDefault del tenant.
        var defaultView = await _db.MenuViews.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenant.Id && v.IsDefault)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaultView is null) { return; }

        // Nodos de la vista por defecto: los Item Ready dan el catalogo (Route) y, subiendo por
        // ParentId hasta la Section ancestro, el slug de su seccion. Con eso el rol demo puede negar
        // Ver por seccion (Ola B2, ADR-0033) sin listas hardcodeadas.
        var viewNodes = await _db.MenuNodes.IgnoreQueryFilters()
            .Where(n => n.MenuViewId == defaultView.Id)
            .Select(n => new { n.Id, n.ParentId, n.Kind, n.Route, n.State })
            .ToListAsync(cancellationToken);
        var nodeById = viewNodes.ToDictionary(n => n.Id);

        string? SectionSlugFor(Guid? parentId)
        {
            var guard = 0;
            var current = parentId;
            while (current is Guid pid && nodeById.TryGetValue(pid, out var node) && guard++ < 100)
            {
                if (node.Kind == MenuNodeKind.Section) { return node.Route; }
                current = node.ParentId;
            }
            return null;
        }

        // (Route, SectionSlug) de cada modulo real. Un mismo Route puede repetirse en varias
        // secciones (gana el primero por orden de aparicion) -> lo usamos solo para el catalogo.
        var moduleRows = viewNodes
            .Where(n => n.Kind == MenuNodeKind.Item
                && n.State == MenuNodeState.Ready
                && !string.IsNullOrWhiteSpace(n.Route))
            .Select(n => new { Route = n.Route!, Section = SectionSlugFor(n.ParentId) })
            .ToList();

        var moduleKeys = moduleRows.Select(m => m.Route).Distinct().ToList();
        if (moduleKeys.Count == 0) { return; }

        // Rol demo "Asesor limitado" (Ola B2, ADR-0033): recorte DEMOSTRABLE del menu y de botones.
        // - SIN Ver en las secciones de gobierno/tecnicas: Sistema . Desarrollo (dev), Sistema . CRM
        //   (syscrm) y CRM heredado (crm) -> esas secciones desaparecen del sidebar de simple@.
        // - CON Ver en el resto (Mis Procesos, Inventarios, Automatizacion, etc.).
        // - Crea solo tareas/proyectos (no inventario, para que /inventario-items NO muestre "Nuevo
        //   item" al Asesor aunque SI pueda verlo). Nunca Editar ni Eliminar.
        var seccionesSinVer = new HashSet<string>(StringComparer.Ordinal) { "dev", "syscrm", "crm" };
        var creaEn = new HashSet<string>(StringComparer.Ordinal) { "actividades", "crear-actividad", "proyectos" };
        // Route -> puede verlo (true salvo que TODAS sus apariciones esten en secciones sin Ver).
        var puedeVer = moduleKeys.ToDictionary(
            route => route,
            route => moduleRows.Where(m => m.Route == route)
                .Any(m => m.Section is null || !seccionesSinVer.Contains(m.Section)),
            StringComparer.Ordinal);

        // Filas de permiso del Asesor limitado (mismo calculo para alta y reconciliacion).
        List<RolPermiso> BuildAsesorPermisos(Guid tenantId, Guid rolId) => moduleKeys.Select(key =>
        {
            var canView = puedeVer[key];
            return new RolPermiso
            {
                TenantId = tenantId,
                RolId = rolId,
                ModuleKey = key,
                CanView = canView,
                CanCreate = canView && creaEn.Contains(key), // Crear solo donde ademas puede Ver.
                CanEdit = false,
                CanDelete = false
            };
        }).ToList();

        if (rolesExist)
        {
            // Idempotente: los roles ya existen. Reconcilia SOLO la matriz del "Asesor limitado" para
            // aplicar el recorte de Ver por seccion (borra e reinserta sus filas). No toca el rol de
            // sistema "Administrador" ni renombra nada.
            var asesorExistente = await _db.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == RolAsesorLimitadoName, cancellationToken);
            if (asesorExistente is not null)
            {
                var prev = await _db.RolPermisos.IgnoreQueryFilters()
                    .Where(p => p.RolId == asesorExistente.Id).ToListAsync(cancellationToken);
                _db.RolPermisos.RemoveRange(prev);
                _db.RolPermisos.AddRange(BuildAsesorPermisos(tenant.Id, asesorExistente.Id));
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Reconciliado el rol '{Asesor}' de {Tenant}: matriz reescrita con recorte de Ver por seccion.",
                    RolAsesorLimitadoName, tenant.Name);
            }
            return;
        }

        // Rol de sistema "Administrador": TODO en true para todos los modulos.
        var admin = new Rol
        {
            TenantId = tenant.Id,
            Name = RolAdministradorName,
            Description = "Rol de sistema con acceso total a todos los modulos.",
            IsActive = true,
            IsSystem = true
        };
        _db.Roles.Add(admin);
        foreach (var key in moduleKeys)
        {
            _db.RolPermisos.Add(new RolPermiso
            {
                TenantId = tenant.Id,
                RolId = admin.Id,
                ModuleKey = key,
                CanView = true,
                CanCreate = true,
                CanEdit = true,
                CanDelete = true
            });
        }

        var asesor = new Rol
        {
            TenantId = tenant.Id,
            Name = RolAsesorLimitadoName,
            Description = "Perfil operativo: consulta general, crea tareas, sin Desarrollo/CRM, sin editar ni eliminar.",
            IsActive = true,
            IsSystem = false
        };
        _db.Roles.Add(asesor);
        _db.RolPermisos.AddRange(BuildAsesorPermisos(tenant.Id, asesor.Id));

        await _db.SaveChangesAsync(cancellationToken);

        // Asigna "Asesor limitado" al usuario simple@ para tener un caso visible.
        var simpleUser = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == MenuSimpleUserEmail, cancellationToken);
        if (simpleUser is not null && simpleUser.RolId != asesor.Id)
        {
            simpleUser.RolId = asesor.Id;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Seed de roles creado para {Tenant}: '{Admin}' (sistema, {Count} modulos) + '{Asesor}' asignado a {User}.",
            tenant.Name, RolAdministradorName, moduleKeys.Count, RolAsesorLimitadoName, MenuSimpleUserEmail);
    }
}
