using System.Globalization;
using System.Security.Claims;
using Ecorex.Application;
using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure;
using Ecorex.Infrastructure.Persistence;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    // Operador de plataforma (Super Admin / roles internos): tiene claim platform_role.
    .AddPolicy("PlatformOperator", p => p.RequireClaim("platform_role"))
    // Solo SuperAdmin (alta del equipo de plataforma).
    .AddPolicy("SuperAdminOnly", p => p.RequireClaim("platform_role", "SuperAdmin"))
    // Miembro de una agencia: tiene claim tenant_id.
    .AddPolicy("TenantMember", p => p.RequireClaim("tenant_id"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
// Contexto de tenant con soporte ambient: por cookie en peticiones, fijable en background (webhook -> agente).
builder.Services.AddScoped<ITenantContext, Ecorex.SuperAdmin.Auth.AmbientTenantContext>();

// Chat en tiempo real (SignalR): reemplaza el broadcaster no-op por el real.
builder.Services.AddSignalR();
builder.Services.AddScoped<Ecorex.Application.Tenancy.IChatBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRChatBroadcaster>();
// Nucleo de tareas en tiempo real (FASE 3): reemplaza el broadcaster no-op por el real.
builder.Services.AddScoped<Ecorex.Application.Tenancy.ITaskBroadcaster, Ecorex.SuperAdmin.RealTime.SignalRTaskBroadcaster>();

// Atencion automatica del agente de IA por lineas de WhatsApp: lector de recursos (wwwroot) +
// despachador en background con debounce (reemplaza la cola no-op de Application).
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IAgentAssetReader, Ecorex.SuperAdmin.RealTime.WebRootAgentAssetReader>();
builder.Services.AddSingleton<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>();
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IAgentReplyQueue>(sp => sp.GetRequiredService<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Ecorex.SuperAdmin.RealTime.AgentReplyDispatcher>());
// Tunel de desarrollo real (cloudflared); reemplaza el no-op de Application.
builder.Services.AddSingleton<Ecorex.Application.Tenancy.IDevTunnel, Ecorex.SuperAdmin.RealTime.CloudflaredTunnel>();
// Sembrador one-shot del agente TravelFans (ver /admin/seed-travelfans).
builder.Services.AddScoped<Ecorex.SuperAdmin.Seeders.TravelFansAgentSeeder>();

var app = builder.Build();

// Detras del proxy de Railway (TLS en el borde, HTTP al contenedor): leer
// X-Forwarded-Proto/For para que Request.Scheme sea "https". Asi las cookies
// seguras del login y UseHttpsRedirection funcionan sin bucles de redireccion.
// Debe ir lo antes posible en el pipeline.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Clear();
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
    await seeder.SeedAsync();
    await seeder.EnsurePlatformAdminTenantAsync();
    await seeder.EnsureDemoTemplateAssetsAsync();
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
        }
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

app.MapPost("/auth/login", async (
    HttpContext http,
    [FromForm] string email,
    [FromForm] string password,
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

    // Operador de plataforma -> Dashboard; usuario de tenant -> Pipeline comercial (no la pagina de cuenta).
    redirect = isOperator ? "/" : "/pipeline";

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
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

    // Operador de plataforma -> Dashboard; usuario de tenant -> Pipeline comercial (no la pagina de cuenta).
    redirect = isOperator ? "/" : "/pipeline";

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(redirect);
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

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
