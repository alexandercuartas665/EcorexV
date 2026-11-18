using System.Text.Json;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.Maqueta;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Integracion con el admin de anotaciones de la maqueta TRONOX (ADR-0176): POST /api/maqueta/solicitudes.
/// Contrato fijo del cliente (D:\DesarrolloIA\tronox.maqueta\docs\INTEGRACION-ECOREX-ANOTACIONES.md).
///
/// - AUTH: Authorization: Bearer &lt;token personal&gt; con alcance "integ:maqueta" (se emite en /conectar-ia).
///   El token resuelve tenant + USUARIO: las tarjetas caen en el tenant de ese usuario y quedan a su nombre
///   ("Nombre (via maqueta)"). No hay clave global en configuracion: cada token se revoca desde la app.
/// - CORS: lo llama un NAVEGADOR (pagina estatica en http://localhost:8096). Origenes permitidos en
///   MaquetaIntegration:AllowedOrigins (coma); por defecto localhost/127.0.0.1:8096. Preflight OPTIONS -> 204.
/// - AISLAMIENTO: la resolucion por hash es la unica lectura cross-tenant; luego AmbientTenantContext.Begin + scope
///   DI propio y el filtro global aisla todo lo demas.
/// - SESION (ADR-0178): solo con el hub /maqueta activo (Development/flag), si NO llega Bearer se acepta la cookie de
///   sesion de ECOREX del usuario logueado (el anotador corre bajo el mismo origen). Anti-CSRF: Content-Type JSON y,
///   si el navegador manda Origin / Sec-Fetch-Site, deben ser del mismo origen. Un Bearer invalido NO cae a sesion.
/// - Respuesta: 201 {id, url, estado:"creada"} o 200 {..., estado:"actualizada"} (reenvio del mismo idLocal);
///   error 4xx/5xx {error}.
/// </summary>
public static class MaquetaSolicitudesEndpoints
{
    public const string Route = "/api/maqueta/solicitudes";

    private const string Realm = "ecorex-maqueta";

    /// <summary>Tope del cuerpo JSON (captura PNG en base64 incluida).</summary>
    private const long MaxBodyBytes = 15 * 1024 * 1024;

    private static readonly string[] DefaultOrigins = ["http://localhost:8096", "http://127.0.0.1:8096"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static WebApplication MapMaquetaSolicitudesEndpoints(this WebApplication app)
    {
        var configured = app.Configuration["MaquetaIntegration:AllowedOrigins"];
        var origins = string.IsNullOrWhiteSpace(configured)
            ? DefaultOrigins
            : configured.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(o => o.TrimEnd('/')).ToArray();
        var allowed = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);

        app.MapMethods(Route, ["OPTIONS"], (HttpContext http) =>
            {
                if (!ApplyCors(http, allowed)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
                http.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
                http.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
                http.Response.Headers.AccessControlMaxAge = "600";
                return Results.NoContent();
            })
            .DisableAntiforgery().AllowAnonymous().WithTags("maqueta");

        app.MapPost(Route, (HttpContext http, IServiceScopeFactory scopes, ILoggerFactory logs, CancellationToken ct) =>
                HandleAsync(http, scopes, logs, allowed, ct))
            .DisableAntiforgery().AllowAnonymous().WithTags("maqueta");
        return app;
    }

    private static async Task<IResult> HandleAsync(HttpContext http, IServiceScopeFactory scopes, ILoggerFactory logs,
        HashSet<string> allowed, CancellationToken ct)
    {
        ApplyCors(http, allowed);

        // 1) Auth por token personal (antes de leer el cuerpo) o, sin Bearer, por la sesion del hub /maqueta.
        var bearer = ExtractBearer(http.Request);
        McpActor? actor;
        if (bearer is not null)
        {
            await using var authScope = scopes.CreateAsyncScope();
            var tokens = authScope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
            actor = await tokens.ResolveAsync(bearer, TenantApiToken.ScopeMaqueta, ct);
            if (actor is null) { return Unauthorized(http, "token invalido, revocado, de otra integracion o de usuario inactivo"); }
        }
        else
        {
            var hub = http.RequestServices.GetService<MaquetaHubRegistry>();
            if (hub is not { IsActive: true } || http.User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(http, "falta Authorization: Bearer <token>");
            }
            if (!IsSameOriginJson(http)) { return Error("La sesion solo se acepta desde el mismo origen y con JSON.", StatusCodes.Status403Forbidden); }
            if (!Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var sessionTenant)
                || !Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var sessionUser))
            {
                return Error("La sesion no pertenece a un usuario de empresa.", StatusCodes.Status403Forbidden);
            }
            using (AmbientTenantContext.Begin(sessionTenant, sessionUser))
            {
                await using var authScope = scopes.CreateAsyncScope();
                actor = await authScope.ServiceProvider.GetRequiredService<IMaquetaSolicitudService>().ResolveSessionActorAsync(sessionUser, ct);
            }
            if (actor is null) { return Unauthorized(http, "la sesion no tiene un usuario activo en la empresa"); }
        }

        // 2) Cuerpo JSON.
        if (http.Request.ContentLength is > MaxBodyBytes) { return Error("La solicitud es demasiado grande.", StatusCodes.Status413PayloadTooLarge); }
        MaquetaSolicitudRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<MaquetaSolicitudRequest>(http.Request.Body, Json, ct); }
        catch (JsonException) { return Error("JSON invalido.", StatusCodes.Status400BadRequest); }
        if (body is null) { return Error("Falta el cuerpo JSON.", StatusCodes.Status400BadRequest); }

        // 3) Ejecucion en el tenant y como el usuario del token.
        using var ambient = AmbientTenantContext.Begin(actor.TenantId, actor.PlatformUserId);
        await using var reqScope = scopes.CreateAsyncScope();
        var sp = reqScope.ServiceProvider;
        TaskCoreResult<MaquetaSolicitudResult> res;
        try
        {
            res = await sp.GetRequiredService<IMaquetaSolicitudService>().RecibirAsync(body, actor, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logs.CreateLogger("Ecorex.Maqueta").LogError(ex, "Fallo al registrar la solicitud de maqueta {IdLocal} del tenant {TenantId}",
                body.IdLocal, actor.TenantId);
            return Error("No se pudo registrar la solicitud.", StatusCodes.Status500InternalServerError);
        }
        if (!res.IsOk || res.Value is null)
        {
            var status = res.Status switch
            {
                TaskCoreStatus.NotFound => StatusCodes.Status404NotFound,
                TaskCoreStatus.Conflict => StatusCodes.Status409Conflict,
                TaskCoreStatus.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return Error(res.Error ?? "Solicitud invalida.", status);
        }

        var r = res.Value;
        var url = sp.GetRequiredService<INotifyLinkBuilder>().BuildTaskLink(r.TaskId)
            ?? $"{http.Request.Scheme}://{http.Request.Host}/actividades?task={r.TaskId}";
        if (r.BoardId is Guid boardId && !url.Contains("board=", StringComparison.Ordinal))
        {
            url = url.Replace("?task=", $"?board={boardId}&task=", StringComparison.Ordinal);
        }
        var payload = new { id = r.Number, url, estado = r.Created ? "creada" : "actualizada", captura = r.CaptureAttached };
        return Results.Json(payload, Json, statusCode: r.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    /// <summary>
    /// Anti-CSRF del canal por sesion: JSON obligatorio (un formulario de otro sitio no puede mandarlo sin preflight) y,
    /// si vienen, Origin = origen propio y Sec-Fetch-Site = same-origin.
    /// </summary>
    private static bool IsSameOriginJson(HttpContext http)
    {
        var contentType = http.Request.ContentType ?? string.Empty;
        if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) { return false; }
        var fetchSite = http.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0 && !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)) { return false; }
        var origin = http.Request.Headers.Origin.ToString();
        if (origin.Length == 0) { return true; }
        var self = $"{http.Request.Scheme}://{http.Request.Host}";
        return string.Equals(origin.TrimEnd('/'), self, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pone las cabeceras CORS si el Origin esta permitido. Sin Origin (curl) no aplica y se deja pasar.</summary>
    private static bool ApplyCors(HttpContext http, HashSet<string> allowed)
    {
        var origin = http.Request.Headers.Origin.ToString();
        http.Response.Headers.Vary = "Origin";
        if (string.IsNullOrEmpty(origin)) { return true; }
        if (!allowed.Contains(origin.TrimEnd('/'))) { return false; }
        http.Response.Headers.AccessControlAllowOrigin = origin;
        return true;
    }

    private static IResult Unauthorized(HttpContext http, string message)
    {
        http.Response.Headers.WWWAuthenticate = $"Bearer realm=\"{Realm}\"";
        return Error(message, StatusCodes.Status401Unauthorized);
    }

    private static IResult Error(string message, int status) => Results.Json(new { error = message }, Json, statusCode: status);

    private static string? ExtractBearer(HttpRequest req)
    {
        var header = req.Headers.Authorization.ToString();
        const string p = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { return null; }
        var t = header[p.Length..].Trim();
        return t.Length == 0 ? null : t;
    }
}
