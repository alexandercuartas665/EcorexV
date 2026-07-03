using Ecorex.Application.Common.Auth;
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

}
