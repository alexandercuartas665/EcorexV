using System.Globalization;
using System.Security.Claims;
using CubotNails.Application;
using CubotNails.Application.Common;
using CubotNails.Application.Common.Auth;
using CubotNails.Domain.Enums;
using CubotNails.Infrastructure;
using CubotNails.Infrastructure.Persistence;
using CubotNails.SuperAdmin.Auth;
using CubotNails.SuperAdmin.Components;
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
builder.Services.AddScoped<ITenantContext, CubotNails.SuperAdmin.Auth.AmbientTenantContext>();

// Chat en tiempo real (SignalR): reemplaza el broadcaster no-op por el real.
builder.Services.AddSignalR();
builder.Services.AddScoped<CubotNails.Application.Tenancy.IChatBroadcaster, CubotNails.SuperAdmin.RealTime.SignalRChatBroadcaster>();

// Atencion automatica del agente de IA por lineas de WhatsApp: lector de recursos (wwwroot) +
// despachador en background con debounce (reemplaza la cola no-op de Application).
builder.Services.AddSingleton<CubotNails.Application.Tenancy.IAgentAssetReader, CubotNails.SuperAdmin.RealTime.WebRootAgentAssetReader>();
builder.Services.AddSingleton<CubotNails.SuperAdmin.RealTime.AgentReplyDispatcher>();
builder.Services.AddSingleton<CubotNails.Application.Tenancy.IAgentReplyQueue>(sp => sp.GetRequiredService<CubotNails.SuperAdmin.RealTime.AgentReplyDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CubotNails.SuperAdmin.RealTime.AgentReplyDispatcher>());
// Tunel de desarrollo real (cloudflared); reemplaza el no-op de Application.
builder.Services.AddSingleton<CubotNails.Application.Tenancy.IDevTunnel, CubotNails.SuperAdmin.RealTime.CloudflaredTunnel>();
// Sembrador one-shot del agente TravelFans (ver /admin/seed-travelfans).
builder.Services.AddScoped<CubotNails.SuperAdmin.Seeders.TravelFansAgentSeeder>();

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

    // En produccion las migraciones NO se aplican solas. Si CUBOT_RUN_MIGRATIONS=true
    // (variable de Railway), aplicar las migraciones pendientes al arrancar. Es seguro
    // con una sola instancia web; el seed de demo no corre en produccion.
    if (string.Equals(Environment.GetEnvironmentVariable("CUBOT_RUN_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CubotNailsDbContext>();
        await db.Database.MigrateAsync();
        // Asegura que el Super Admin tambien sea Owner del tenant interno "Plataforma CUBOT" para
        // que pueda usar Pipeline, Tableros y los modulos comerciales como una agencia mas. Es
        // idempotente: si el tenant interno o la membresia ya existen no hace nada. No crea datos
        // demo. Esto es lo unico del seeder que tiene sentido correr en produccion.
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.EnsurePlatformAdminTenantAsync();
        // Clave fuerte del Super Admin definida como secreto en la plataforma (Railway), no versionada.
        await seeder.EnsureSuperAdminPasswordAsync(Environment.GetEnvironmentVariable("CUBOT_SEED_ADMIN_PASSWORD"));
    }
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CubotNailsDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
    await seeder.EnsurePlatformAdminTenantAsync();
    await seeder.EnsureDemoTemplateAssetsAsync();
    await seeder.EnsureDemoProductsAsync();
    await seeder.EnsureDemoCoursesAsync();
    await seeder.EnsureDemoAgentCommercialFlowAsync();
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

app.MapHub<CubotNails.SuperAdmin.RealTime.ChatHub>("/hubs/chat");

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
    // interno "Plataforma CUBOT") recibe los dos claims y puede usar tanto la consola de gobierno
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

    redirect = isOperator ? "/" : "/mi-cuenta";

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
    CubotNails.Application.Auth.ISelfSignupService signup) =>
{
    var result = await signup.SignUpAsync(
        new CubotNails.Application.Auth.SelfSignupRequest(agencyName, displayName, email, password));

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
    CubotNails.Application.Auth.IAccountActivationService activation) =>
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
    CubotNails.Application.Auth.IAccountActivationService activation,
    CubotNails.Application.Common.IEmailSender emailSender,
    CubotNails.Application.Admin.IPlatformBrandingService branding) =>
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
    CubotNails.Application.Auth.IPasswordResetService reset) =>
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
    CubotNails.Application.Auth.IPasswordResetService reset) =>
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
    CubotNails.Application.Auth.IGoogleSignInService google) =>
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
    CubotNails.Application.Auth.IGoogleSignInService google,
    CubotNailsDbContext db) =>
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
    // el usuario es miembro de algun tenant (caso Super Admin con tenant interno "Plataforma CUBOT").
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

    redirect = isOperator ? "/" : "/mi-cuenta";

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
    CubotNails.Application.Tenancy.ITenantApiService api,
    CubotNails.Application.Tenancy.ApiCreateLeadRequest body,
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
    CubotNails.Application.Tenancy.IQuoteRenderService render,
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
    CubotNails.Application.Common.IQuotePdfRenderer pdf,
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
    CubotNails.Application.Admin.IPaymentReceiptService receipts) =>
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
    CubotNails.Application.Tenancy.IChatIngestService ingest,
    CancellationToken ct) =>
{
    var master = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
    var expected = master?.WebhookToken
        ?? Environment.GetEnvironmentVariable("CUBOT_EVOLUTION_WEBHOOK_TOKEN");
    if (string.IsNullOrEmpty(expected)) { return Results.StatusCode(503); }

    var provided = request.Headers["x-webhook-token"].ToString();
    if (string.IsNullOrEmpty(provided)) { provided = request.Query["token"].ToString(); }
    if (!string.Equals(provided, expected, StringComparison.Ordinal)) { return Results.Unauthorized(); }

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var parsed = CubotNails.SuperAdmin.RealTime.EvolutionWebhookParser.Parse(doc.RootElement);
    if (parsed is null) { return Results.Ok(new { status = "ignored" }); }

    var result = await ingest.IngestTrustedAsync(parsed.TenantId, parsed.Payload, ct);
    return result == CubotNails.Application.Tenancy.ChatIngestResult.Duplicate
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
    CubotNails.Application.Tenancy.IChatIngestService ingest,
    CancellationToken ct) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var messages = CubotNails.SuperAdmin.RealTime.MetaWebhookParser.Parse(doc.RootElement);
    if (messages.Count == 0) { return Results.Ok(new { status = "ignored" }); }

    foreach (var m in messages)
    {
        // Sin contexto de tenant aun: la linea Cloud se identifica por su phone_number_id.
        var line = await db.WhatsAppLines.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.CloudPhoneNumberId == m.PhoneNumberId
                && l.Provider == CubotNails.Domain.Enums.WhatsAppProvider.Cloud, ct);
        if (line is null) { continue; } // numero no registrado en ninguna linea

        var payload = new CubotNails.Application.Tenancy.IngestMessageRequest(
            m.Phone, m.Name, m.ExternalId, m.Body, "text", m.SentAt, line.Id);
        await ingest.IngestTrustedAsync(line.TenantId, payload, ct);
    }
    return Results.Ok(new { status = "ok" });
}).AllowAnonymous().DisableAntiforgery();

app.Run();
