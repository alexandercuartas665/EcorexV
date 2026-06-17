using CubotNails.Application.Common.Auth;
using CubotNails.Domain.Entities;
using CubotNails.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotNails.Infrastructure.Persistence;

/// <summary>
/// Siembra datos iniciales de desarrollo de forma idempotente: un Super Admin, un plan,
/// una agencia demo con su administrador y una suscripcion. Solo crea si la base esta vacia.
/// </summary>
public sealed class DatabaseSeeder
{
    public const string SuperAdminEmail = "admin@cubot.nails";
    public const string SuperAdminPassword = "Admin123*";
    public const string TenantAdminEmail = "demo-admin@cubot.nails";
    public const string TenantAdminPassword = "Demo123*";

    private readonly CubotNailsDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(CubotNailsDbContext db, IPasswordHasher hasher, ILogger<DatabaseSeeder> logger)
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
            Name = "Plan Inicial",
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

        var tenant = new Tenant
        {
            Name = "Agencia Demo",
            LegalName = "Agencia Demo SAS",
            TaxId = "900123456-7",
            Country = "CO",
            Currency = "COP",
            Status = TenantStatus.Active,
            Kind = TenantKind.Demo
        };

        var tenantAdmin = new PlatformUser
        {
            Email = TenantAdminEmail,
            EmailVerified = true,
            DisplayName = "Administrador Agencia Demo",
            Status = PlatformUserStatus.Active,
            PasswordHash = _hasher.Hash(TenantAdminPassword)
        };

        _db.PlatformUsers.AddRange(superAdmin, tenantAdmin);
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

        _db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            PlatformUserId = tenantAdmin.Id,
            Email = TenantAdminEmail,
            TenantRole = TenantRole.Owner,
            Status = PlatformUserStatus.Active
        });

        _db.TenantConfigurations.AddRange(
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "tono", ConfigValue = "cordial" },
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "horario", ConfigValue = "8-18" });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Seed inicial creado. Super Admin: {SuperAdmin} / {SuperPass}. Admin agencia: {TenantAdmin} / {TenantPass}",
            SuperAdminEmail, SuperAdminPassword, TenantAdminEmail, TenantAdminPassword);
    }

    /// <summary>
    /// Fija la clave del Super Admin a partir de un valor provisto por el entorno (CUBOT_SEED_ADMIN_PASSWORD
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
    /// Asegura que el Super Admin (admin@cubot.nails) tambien sea Owner de un tenant interno
    /// "Plataforma CUBOT". Asi el Super Admin puede usar Pipeline y los modulos comerciales como
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
                Name = "Plataforma CUBOT",
                LegalName = "CUBOT.nails SAS",
                Country = "CO",
                Currency = "COP",
                Status = TenantStatus.Active,
                Kind = TenantKind.Internal
            };
            _db.Tenants.Add(platformTenant);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Tenant interno 'Plataforma CUBOT' creado para el Super Admin (id={Id}).", platformTenant.Id);
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

    // Productos demo para probar el modulo de productos y la herramienta de IA "consultar_productos".
    // Idempotente: solo siembra si la agencia demo aun no tiene productos. Usa las sedes existentes
    // (creadas por el usuario); si no hay ninguna, crea tres (dos en Cali, una en Medellin).
    public async Task EnsureDemoProductsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.Products.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        var sedes = await _db.Sedes.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id)
            .OrderBy(s => s.City).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        if (sedes.Count == 0)
        {
            sedes = new List<Sede>
            {
                new() { TenantId = tenant.Id, Name = "Sede Norte", City = "Cali", Address = "Av 6N #28-10", Phone = "+57 602 4441122", IsActive = true },
                new() { TenantId = tenant.Id, Name = "Sede Sur", City = "Cali", Address = "Cra 100 #16-20", Phone = "+57 602 4443344", IsActive = true },
                new() { TenantId = tenant.Id, Name = "Sede Poblado", City = "Medellin", Address = "Cra 43A #7-50", Phone = "+57 604 4445566", IsActive = true }
            };
            _db.Sedes.AddRange(sedes);
            await _db.SaveChangesAsync(cancellationToken);
        }

        (string Name, string Sku, decimal Price, string Category, string Desc)[] products =
        {
            ("Esmalte Gel Rojo Pasion", "ESM-001", 35000m, "Esmaltes", "Esmalte en gel de larga duracion, secado UV/LED, color rojo intenso."),
            ("Esmalte Gel Nude Natural", "ESM-002", 35000m, "Esmaltes", "Esmalte en gel tono nude, ideal para un look natural y elegante."),
            ("Top Coat Brillo Espejo", "TOP-001", 30000m, "Esmaltes", "Sellador con acabado brillo espejo y proteccion anti-rayones."),
            ("Kit Manicure Premium", "KIT-001", 120000m, "Kits", "Kit completo: corta cuticula, lima, pulidor, empujador y estuche."),
            ("Crema Hidratante de Manos", "CRM-001", 28000m, "Cuidado", "Crema humectante con karite y vitamina E para manos suaves."),
            ("Aceite de Cuticula", "ACE-001", 18000m, "Cuidado", "Aceite nutritivo para cuticulas con jojoba y almendras."),
            ("Lima Profesional 100/180", "LIM-001", 12000m, "Herramientas", "Lima de doble grano para limado y acabado profesional.")
        };

        // Stock pseudo-variado por sede (incluye ceros para que la disponibilidad difiera entre sedes).
        int[] pattern = { 12, 8, 0, 20, 5, 15, 0, 10, 6, 9, 3, 18, 7, 0 };

        var created = new List<Product>();
        foreach (var p in products)
        {
            var product = new Product
            {
                TenantId = tenant.Id, Name = p.Name, Sku = p.Sku, Price = p.Price, Category = p.Category, Description = p.Desc, IsActive = true
            };
            _db.Products.Add(product);
            created.Add(product);
        }
        await _db.SaveChangesAsync(cancellationToken);

        var idx = 0;
        foreach (var product in created)
        {
            for (var si = 0; si < sedes.Count; si++)
            {
                var qty = pattern[(idx + si) % pattern.Length];
                _db.ProductStocks.Add(new ProductStock { TenantId = tenant.Id, ProductId = product.Id, SedeId = sedes[si].Id, Stock = qty });
            }
            idx++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Productos demo registrados ({Count}) en {Sedes} sedes.", products.Length, sedes.Count);
    }

    // Cursos demo (eventuales) para probar el modulo de cursos y las herramientas de IA consultar_cursos /
    // inscribir_curso. Idempotente: solo siembra si la agencia demo aun no tiene cursos.
    public async Task EnsureDemoCoursesAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.Courses.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var manicure = new Course
        {
            TenantId = tenant.Id, Name = "Curso de Manicure Profesional", IsActive = true,
            Description = "Curso practico de manicure profesional: tecnicas, higiene y atencion al cliente. Incluye kit basico.",
            Date = today.AddDays(15), StartTime = new TimeOnly(9, 0), Capacity = 12, Price = 250000m
        };
        var acrilicas = new Course
        {
            TenantId = tenant.Id, Name = "Taller de Unas Acrilicas", IsActive = true,
            Description = "Taller intensivo de unas acrilicas: aplicacion, esculpido y acabados.",
            Date = today.AddDays(25), StartTime = new TimeOnly(14, 0), Capacity = 8, Price = 180000m
        };
        var maquillaje = new Course
        {
            TenantId = tenant.Id, Name = "Maquillaje Profesional Basico", IsActive = true,
            Description = "Fundamentos del maquillaje profesional: preparacion de piel, correccion y look de dia/noche.",
            Date = today.AddDays(20), StartTime = new TimeOnly(10, 0), Capacity = 10, Price = 220000m
        };
        _db.Courses.AddRange(manicure, acrilicas, maquillaje);
        await _db.SaveChangesAsync(cancellationToken);

        // Un par de inscripciones de ejemplo en el primer curso (una pagada, una pendiente).
        var now = DateTimeOffset.UtcNow;
        _db.CourseRegistrations.AddRange(
            new CourseRegistration { TenantId = tenant.Id, CourseId = manicure.Id, PersonName = "Maria Lopez", Phone = "3019998877", IsPaid = true, RegisteredAt = now },
            new CourseRegistration { TenantId = tenant.Id, CourseId = manicure.Id, PersonName = "Juan Perez", Phone = "3024445566", IsPaid = false, RegisteredAt = now });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cursos demo registrados (3) con inscripciones de ejemplo.");
    }

    // Flujo comercial del agente demo: un dato cache que clasifica el canal del cliente + prompts enrutados
    // por canal (productos al detal, productos B2B, cursos, asesoria de imagen), cada uno con un GUION DE
    // CIERRE que termina creando un lead en el pipeline (herramienta crear_lead). Idempotente por nombre.
    public async Task EnsureDemoAgentCommercialFlowAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        var agents = await _db.AiAgents.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        if (agents.Count == 0) { return; }

        // Regla anexada a TODO prompt de canal: fuerza la invocacion REAL de la herramienta de cierre.
        const string closeRule =
            "\n\nREGLA DE CIERRE OBLIGATORIA: para cerrar DEBES invocar la herramienta de cierre que corresponde a este " +
            "prompt (por ejemplo crear_lead o inscribir_curso). NUNCA afirmes que registraste, inscribiste, dejaste el " +
            "pedido o que un asesor lo contactara si no has invocado la herramienta en este mismo turno. Primero invoca la " +
            "herramienta, luego confirma.";

        (string Name, string Rule, string Body)[] prompts =
        {
            ("Productos al detal",
             "cuando el cliente quiere comprar un producto para uso personal, pregunta por un esmalte, crema, kit, lima u otro articulo del catalogo, o por su precio o disponibilidad",
             "El cliente busca un PRODUCTO al detal (uso personal). Atiende asi:\n" +
             "1) Usa consultar_productos para darle precio real y en que sede esta disponible. NUNCA inventes productos ni precios.\n" +
             "2) Captura nombre del cliente, telefono y que producto(s) quiere.\n" +
             "GUION DE CIERRE: cuando tengas nombre, telefono y el producto de interes, confirma con un resumen corto y CIERRA " +
             "registrando el lead con la herramienta crear_lead usando tipo_cliente='productos' y un resumen de lo que quiere. " +
             "Luego avisa con calidez que un asesor lo contactara para coordinar el pago y la entrega."),

            ("Productos B2B (empresas)",
             "cuando el cliente quiere comprar al por mayor, suministrar productos para una empresa, salon o negocio, pide una cotizacion empresarial o menciona cantidades grandes",
             "El cliente es B2B: quiere SUMINISTRO o compra al por mayor para una empresa o negocio. Atiende asi:\n" +
             "1) Usa consultar_productos para precios y existencias reales; para volumen, aclara que un asesor confirma el precio mayorista.\n" +
             "2) Captura nombre del contacto, telefono, nombre de la empresa, que productos y cantidades aproximadas.\n" +
             "GUION DE CIERRE: cuando tengas nombre, telefono y el detalle del pedido, confirma con un resumen y CIERRA con " +
             "crear_lead usando tipo_cliente='b2b', el valor_estimado si lo puedes calcular y un resumen con empresa, productos y " +
             "cantidades. Avisa que un asesor comercial lo contactara con la cotizacion."),

            ("Cursos",
             "cuando el cliente pregunta por cursos, formacion, capacitacion, talleres o quiere aprender (manicure, unas, maquillaje, etc.)",
             "El cliente esta interesado en un CURSO / formacion. Atiende asi:\n" +
             "1) Usa consultar_cursos para mostrarle los cursos REALES disponibles (fecha, cupo y valor). NUNCA inventes cursos ni fechas.\n" +
             "2) Captura nombre y telefono del cliente y a que curso quiere inscribirse.\n" +
             "GUION DE CIERRE: cuando el cliente confirme el curso y te de su nombre, INSCRIBELO con inscribir_curso (curso, persona_nombre, persona_telefono). " +
             "Luego confirma la inscripcion e indica el valor y que un asesor coordinara el pago."),

            ("Asesoria de imagen (servicio estilista)",
             "cuando el cliente quiere un servicio del salon (manicure, unas, peinado, maquillaje, asesoria de imagen) o agendar con un estilista, y no es una cancelacion",
             "El cliente quiere un SERVICIO del salon / asesoria de imagen. Atiende asi:\n" +
             "1) Usa listar_asesores, consultar_servicios_precios y consultar_disponibilidad para orientarlo con datos reales; si confirma un cupo puedes reservar con reservar_cita.\n" +
             "2) Captura nombre del cliente y telefono.\n" +
             "GUION DE CIERRE: cuando tengas nombre, telefono y el servicio o intencion clara, ademas de atender la cita, CIERRA " +
             "registrando el lead con crear_lead usando tipo_cliente='estilista' y un resumen del servicio que busca, para que el " +
             "equipo comercial le de seguimiento.")
        };

        foreach (var agent in agents)
        {
            // Dato cache que clasifica el canal del cliente (idempotente por field_key).
            var hasTipo = await _db.AiAgentCacheFields.IgnoreQueryFilters()
                .AnyAsync(f => f.AgentId == agent.Id && f.FieldKey == "tipo_cliente", cancellationToken);
            if (!hasTipo)
            {
                var nextField = (await _db.AiAgentCacheFields.IgnoreQueryFilters().Where(f => f.AgentId == agent.Id)
                    .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
                _db.AiAgentCacheFields.Add(new AiAgentCacheField
                {
                    TenantId = tenant.Id,
                    AgentId = agent.Id,
                    FieldKey = "tipo_cliente",
                    Label = "Tipo de cliente",
                    Description = "Canal del cliente para clasificarlo y enrutar el cierre: 'b2b' (suministro para empresa o negocio), " +
                                  "'productos' (producto al detal / uso personal), 'cursos' (formacion) o 'estilista' (servicio del salon / asesoria de imagen). " +
                                  "Infierelo de lo que pide el cliente.",
                    SortOrder = nextField,
                    IsUpdatable = true
                });
            }

            // Prompts enrutados por canal (idempotente por nombre).
            var existingNames = await _db.AiAgentPrompts.IgnoreQueryFilters()
                .Where(p => p.AgentId == agent.Id)
                .Select(p => p.Name)
                .ToListAsync(cancellationToken);
            var nextOrder = (await _db.AiAgentPrompts.IgnoreQueryFilters().Where(p => p.AgentId == agent.Id)
                .Select(p => (int?)p.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
            foreach (var (name, rule, body) in prompts)
            {
                if (existingNames.Contains(name)) { continue; }
                _db.AiAgentPrompts.Add(new AiAgentPrompt
                {
                    TenantId = tenant.Id,
                    AgentId = agent.Id,
                    Name = name,
                    Rule = rule,
                    Body = body + closeRule,
                    SortOrder = nextOrder++
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Flujo comercial del agente demo asegurado (cache tipo_cliente + prompts por canal).");
    }
}
