using System.Security.Claims;
using CubotTravels.Application;
using CubotTravels.Application.Common;
using CubotTravels.Application.Common.Auth;
using CubotTravels.Domain.Enums;
using CubotTravels.Infrastructure;
using CubotTravels.Infrastructure.Persistence;
using CubotTravels.SuperAdmin.Auth;
using CubotTravels.SuperAdmin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
    // Miembro de una agencia: tiene claim tenant_id.
    .AddPolicy("TenantMember", p => p.RequireClaim("tenant_id"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<ITenantContext, CookieUserContext>();

// Chat en tiempo real (SignalR): reemplaza el broadcaster no-op por el real.
builder.Services.AddSignalR();
builder.Services.AddScoped<CubotTravels.Application.Tenancy.IChatBroadcaster, CubotTravels.SuperAdmin.RealTime.SignalRChatBroadcaster>();
// Tunel de desarrollo real (cloudflared); reemplaza el no-op de Application.
builder.Services.AddSingleton<CubotTravels.Application.Tenancy.IDevTunnel, CubotTravels.SuperAdmin.RealTime.CloudflaredTunnel>();

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
        var db = scope.ServiceProvider.GetRequiredService<CubotTravelsDbContext>();
        await db.Database.MigrateAsync();
    }
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CubotTravelsDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
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

app.MapHub<CubotTravels.SuperAdmin.RealTime.ChatHub>("/hubs/chat");

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
        || user.Status != PlatformUserStatus.Active
        || string.IsNullOrEmpty(user.PasswordHash)
        || !hasher.Verify(user.PasswordHash, password ?? string.Empty))
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
    if (user.PlatformRole is PlatformRole role)
    {
        // Operador de plataforma (Super Admin / roles internos).
        claims.Add(new Claim("platform_role", role.ToString()));
        redirect = "/";
    }
    else
    {
        // Usuario de agencia: resolver su membresia activa. Sin contexto de tenant aun,
        // se ignora el filtro global para localizar la membresia por su PlatformUserId.
        var membership = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
            .OrderBy(tu => tu.CreatedAt)
            .FirstOrDefaultAsync();

        if (membership is null)
        {
            // Identidad valida pero sin rol de plataforma ni membresia activa: sin acceso.
            return Results.Redirect("/login?error=1");
        }

        claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
        claims.Add(new Claim("tenant_role", membership.TenantRole.ToString()));
        redirect = "/mi-cuenta";
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(redirect);
}).DisableAntiforgery();

// Auto-registro (autogestion): un visitante crea su propia agencia + usuario Owner y queda
// con sesion iniciada. La agencia nace activa sin plan; elige plan luego en "Mi cuenta".
app.MapPost("/auth/register", async (
    HttpContext http,
    [FromForm] string agencyName,
    [FromForm] string displayName,
    [FromForm] string email,
    [FromForm] string password,
    CubotTravels.Application.Auth.ISelfSignupService signup) =>
{
    var result = await signup.SignUpAsync(
        new CubotTravels.Application.Auth.SelfSignupRequest(agencyName, displayName, email, password));

    if (!result.Success)
    {
        var msg = Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta.");
        return Results.Redirect($"/login?mode=signup&regerror={msg}");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.AdminUserId.ToString()),
        new(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? result.Email : displayName.Trim()),
        new(ClaimTypes.Email, result.Email),
        new("tenant_id", result.TenantId.ToString()),
        new("tenant_role", TenantRole.Owner.ToString())
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/mi-cuenta");
}).DisableAntiforgery();

// Recuperar contrasena (autogestion): envia un enlace de reseteo por correo. Nunca revela si el
// correo existe. El enlace usa el host de la peticion (sirve en dev y en prod tras forwarded headers).
app.MapPost("/auth/forgot", async (
    HttpContext http,
    [FromForm] string email,
    CubotTravels.Application.Auth.IPasswordResetService reset) =>
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
    CubotTravels.Application.Auth.IPasswordResetService reset) =>
{
    var result = await reset.ResetAsync(token, password);
    if (!result.Success)
    {
        return Results.Redirect($"/restablecer?token={Uri.EscapeDataString(token)}&error={Uri.EscapeDataString(result.Error ?? "No se pudo restablecer la contrasena.")}");
    }
    return Results.Redirect("/login?reset=1");
}).DisableAntiforgery();

// Inicia el flujo OIDC con Google: arma la URL de challenge y guarda un state (proteccion CSRF).
app.MapGet("/connect/google", async (
    HttpContext http,
    CubotTravels.Application.Auth.IGoogleSignInService google) =>
{
    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var state = Guid.NewGuid().ToString("N");
    var url = await google.BuildAuthorizeUrlAsync(redirectUri, state);
    if (url is null) { return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("El ingreso con Google no esta habilitado.")); }

    http.Response.Cookies.Append("g_oauth_state", state, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/"
    });
    return Results.Redirect(url);
}).AllowAnonymous();

// Callback de Google: valida el state, intercambia el code y, si el usuario existe y esta activo,
// inicia sesion por cookie. No hay auto-registro: usuarios desconocidos reciben un mensaje claro.
app.MapGet("/signin-google", async (
    HttpContext http,
    [FromQuery] string? code,
    [FromQuery] string? state,
    [FromQuery] string? error,
    CubotTravels.Application.Auth.IGoogleSignInService google) =>
{
    if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("No se completo el ingreso con Google."));
    }

    var expectedState = http.Request.Cookies["g_oauth_state"];
    http.Response.Cookies.Delete("g_oauth_state");
    if (string.IsNullOrEmpty(state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("Sesion de ingreso invalida. Intenta de nuevo."));
    }

    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var result = await google.ResolveAsync(code, redirectUri);
    if (!result.Success)
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo iniciar sesion con Google."));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
        new(ClaimTypes.Name, result.DisplayName ?? result.Email ?? string.Empty),
        new(ClaimTypes.Email, result.Email ?? string.Empty)
    };

    string redirect;
    if (result.PlatformRole is not null)
    {
        claims.Add(new Claim("platform_role", result.PlatformRole));
        redirect = "/";
    }
    else
    {
        claims.Add(new Claim("tenant_id", result.TenantId!.Value.ToString()));
        claims.Add(new Claim("tenant_role", result.TenantRole ?? string.Empty));
        redirect = "/mi-cuenta";
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(redirect);
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// Descarga del comprobante de pago (PDF). Solo pagos aprobados; el usuario de agencia solo
// puede descargar comprobantes de su propio tenant; el operador de plataforma puede cualquiera.
app.MapGet("/comprobante/{paymentId:guid}", async (
    Guid paymentId,
    HttpContext http,
    CubotTravels.Application.Admin.IPaymentReceiptService receipts) =>
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
    CubotTravels.Application.Tenancy.IChatIngestService ingest,
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
    var parsed = CubotTravels.SuperAdmin.RealTime.EvolutionWebhookParser.Parse(doc.RootElement);
    if (parsed is null) { return Results.Ok(new { status = "ignored" }); }

    var result = await ingest.IngestTrustedAsync(parsed.TenantId, parsed.Payload, ct);
    return result == CubotTravels.Application.Tenancy.ChatIngestResult.Duplicate
        ? Results.Ok(new { status = "duplicate" })
        : Results.Accepted();
}).AllowAnonymous().DisableAntiforgery();

app.Run();
