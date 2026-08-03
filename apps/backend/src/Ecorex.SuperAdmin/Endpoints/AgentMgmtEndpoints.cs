using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// API REST de GOBIERNO para gestionar la estructura de los agentes de IA de CUALQUIER tenant y
/// leer sus bitacoras, pensada para un operador externo (via WebFetch). No usa la cookie del panel.
///
/// Endurecimiento (ADR-0057):
/// - Gate 1: la env var ECOREX_MGMT_API_KEY. Si no esta seteada/vacia -> 404 en todo (API deshabilitada,
///   no revela que existe). Se activa SOLO cuando el operador setea la clave, y se desactiva al quitarla.
/// - Gate 2 (opcional): ECOREX_MGMT_API_ALLOW_IPS (lista separada por coma). Si esta seteada, solo esas
///   IPs de origen pasan; el resto recibe 403. Sin ella, no hay filtro de IP.
/// - Header X-Ecorex-Mgmt-Key validado en tiempo constante; si falta o no coincide -> 401.
/// - Tenant objetivo obligatorio por query ?tenant={guid}; se fija con AmbientTenantContext.Begin en un
///   scope de DI aislado (igual que el webhook entrante).
/// - AUDITORIA: TODA mutacion (crear/editar/prompt) escribe una entrada inmutable en
///   super_admin_audit_logs via IAuditWriter, con actorType=System y reason "mgmt-api" (rule #5 de CLAUDE.md).
///   Al ser una clave compartida no hay identidad de usuario: la traza registra QUE/CUANDO/tenant/agente,
///   no QUIEN. Por eso la clave debe rotarse y su uso restringirse.
/// </summary>
public static class AgentMgmtEndpoints
{
    private const string KeyHeader = "X-Ecorex-Mgmt-Key";
    private const string KeyEnvVar = "ECOREX_MGMT_API_KEY";
    private const string AllowIpsEnvVar = "ECOREX_MGMT_API_ALLOW_IPS";
    private const string AuditReason = "mgmt-api";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static WebApplication MapAgentMgmtEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mgmt");

        // 1. Listado resumido de agentes del tenant.
        group.MapGet("/agents", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, (svc, _, c) => svc.Agents.ListAsync(c),
                result => Results.Json(result, Json)));

        // 2. Detalle: agente + prompts enrutados + recursos + definicion de datos cache.
        group.MapGet("/agents/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, _, c) =>
            {
                var detail = await svc.Agents.GetAsync(id, c);
                if (detail is null) { return (object?)null; }
                var cacheFields = await svc.Cache.ListFieldsAsync(id, c);
                return new { detail.Agent, detail.Resources, detail.Prompts, CacheFields = cacheFields };
            },
            result => result is null ? Results.NotFound() : Results.Json(result, Json)));

        // 3. Crear agente.
        group.MapPost("/agents", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<CreateAiAgentRequest>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null) { return null; }
                var created = await svc.Agents.CreateAsync(body, SystemActor, c);
                if (created is not null) { await AuditAsync(svc, tenant, "mgmt-api.agent.create", nameof(Ecorex.Domain.Entities.AiAgent), created.Id, new { created.Name, created.Provider }, c); }
                return (object?)created;
            },
            result => result is null ? Results.BadRequest(new { error = "no se pudo crear el agente" }) : Results.Json(result, Json, statusCode: 201)))
            .DisableAntiforgery();

        // 4. Actualizar agente completo.
        group.MapPut("/agents/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<UpdateAiAgentRequest>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null) { return null; }
                var updated = await svc.Agents.UpdateAsync(id, body, SystemActor, c);
                if (updated is not null) { await AuditAsync(svc, tenant, "mgmt-api.agent.update", nameof(Ecorex.Domain.Entities.AiAgent), id, new { body.Name, body.Provider, body.Model }, c); }
                return (object?)updated;
            },
            result => result is null ? Results.NotFound() : Results.Json(result, Json)))
            .DisableAntiforgery();

        // 5. Actualizar SOLO el system prompt (carga el agente, conserva los demas campos, reusa UpdateAsync).
        group.MapPut("/agents/{id:guid}/prompt", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<SetSystemPromptRequest>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null || body.SystemPrompt is null) { return null; }
                var current = await svc.Agents.GetAsync(id, c);
                if (current is null) { return null; }
                var a = current.Agent;
                var upd = new UpdateAiAgentRequest(a.Name, a.Role, a.Provider, a.Model, body.SystemPrompt,
                    a.DisabledTools, a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis);
                var updated = await svc.Agents.UpdateAsync(id, upd, SystemActor, c);
                if (updated is not null) { await AuditAsync(svc, tenant, "mgmt-api.agent.prompt-set", nameof(Ecorex.Domain.Entities.AiAgent), id, new { promptLength = body.SystemPrompt.Length }, c); }
                return (object?)updated;
            },
            result => result is null ? Results.NotFound() : Results.Json(result, Json)))
            .DisableAntiforgery();

        // 6a. Agregar prompt enrutado.
        group.MapPost("/agents/{id:guid}/prompts", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<PromptUpsertBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null) { return null; }
                var request = new CreateAgentPromptRequest(id, body.Name ?? string.Empty, body.Rule, body.Body ?? string.Empty);
                var created = await svc.Agents.AddPromptAsync(request, SystemActor, c);
                if (created is not null) { await AuditAsync(svc, tenant, "mgmt-api.prompt.add", nameof(Ecorex.Domain.Entities.AiAgentPrompt), created.Id, new { agentId = id, created.Name }, c); }
                return (object?)created;
            },
            result => result is null ? Results.BadRequest(new { error = "no se pudo crear el prompt" }) : Results.Json(result, Json, statusCode: 201)))
            .DisableAntiforgery();

        // 6b. Actualizar prompt enrutado.
        group.MapPut("/prompts/{promptId:guid}", (Guid promptId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<PromptUpsertBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null) { return null; }
                var request = new UpdateAgentPromptRequest(body.Name ?? string.Empty, body.Rule, body.Body ?? string.Empty);
                var updated = await svc.Agents.UpdatePromptAsync(promptId, request, SystemActor, c);
                if (updated is not null) { await AuditAsync(svc, tenant, "mgmt-api.prompt.update", nameof(Ecorex.Domain.Entities.AiAgentPrompt), promptId, new { updated.Name }, c); }
                return (object?)updated;
            },
            result => result is null ? Results.NotFound() : Results.Json(result, Json)))
            .DisableAntiforgery();

        // 6c. Eliminar prompt enrutado.
        group.MapDelete("/prompts/{promptId:guid}", (Guid promptId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, tenant, c) =>
            {
                var ok = await svc.Agents.DeletePromptAsync(promptId, SystemActor, c);
                if (ok) { await AuditAsync(svc, tenant, "mgmt-api.prompt.delete", nameof(Ecorex.Domain.Entities.AiAgentPrompt), promptId, null, c); }
                return ok;
            },
            ok => ok ? Results.Json(new { deleted = true }, Json) : Results.NotFound()))
            .DisableAntiforgery();

        // 7. Bitacora del agente (ai_agent_run_logs). Solo lectura, no audita.
        group.MapGet("/agents/{id:guid}/bitacora", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            AiAgentRunLogKind? kind = null;
            var kindRaw = req.Query["kind"].ToString();
            if (!string.IsNullOrWhiteSpace(kindRaw))
            {
                if (!Enum.TryParse<AiAgentRunLogKind>(kindRaw, ignoreCase: true, out var k))
                {
                    return Task.FromResult(Results.BadRequest(new { error = $"kind invalido: {kindRaw}" }));
                }
                kind = k;
            }
            var limit = 50;
            var limitRaw = req.Query["limit"].ToString();
            if (!string.IsNullOrWhiteSpace(limitRaw) && int.TryParse(limitRaw, out var l)) { limit = Math.Clamp(l, 1, 500); }

            return Run(req, scopes, ct, async (svc, _, c) =>
            {
                var query = svc.Db.AiAgentRunLogs.AsNoTracking().Where(x => x.AgentId == id);
                if (kind is { } kk) { query = query.Where(x => x.Kind == kk); }
                var rows = await query.OrderByDescending(x => x.OccurredAt).Take(limit)
                    .Select(x => new BitacoraEntryDto(x.OccurredAt, x.Kind, x.ConversationId, x.Title, x.Content, x.Response))
                    .ToListAsync(c);
                return (object)rows;
            },
            result => Results.Json(result, Json));
        });

        return app;
    }

    // Sin identidad de usuario detras de la clave: el actor de la auditoria es el sistema.
    private static readonly Guid SystemActor = Guid.Empty;

    // --- Cuerpos de entrada propios de esta API ---
    private sealed record SetSystemPromptRequest(string? SystemPrompt);
    private sealed record PromptUpsertBody(string? Name, string? Rule, string? Body);
    private sealed record BitacoraEntryDto(DateTimeOffset OccurredAt, AiAgentRunLogKind Kind, Guid ConversationId, string Title, string? Content, string? Response);

    /// <summary>Servicios tenant-scoped resueltos dentro del scope con el tenant ya fijado.</summary>
    private readonly record struct MgmtServices(IAiAgentService Agents, IAiAgentCacheService Cache, IAuditWriter Audit, IApplicationDbContext Db);

    /// <summary>Registra una entrada inmutable de auditoria (actorType=System, reason "mgmt-api") y la persiste.</summary>
    private static async Task AuditAsync(MgmtServices svc, Guid tenantId, string action, string entityName, Guid? entityId, object? newValue, CancellationToken ct)
    {
        svc.Audit.Write(SystemActor, action, entityName, entityId, previousValue: null, newValue: newValue,
            tenantId: tenantId, reason: AuditReason, actorType: AuditActorType.System);
        await svc.Db.SaveChangesAsync(ct);
    }

    // ---- Nucleo de auth + tenant-scoping ----

    private static async Task<IResult> Run<T>(
        HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct,
        Func<MgmtServices, Guid, CancellationToken, Task<T>> work,
        Func<T, IResult> shape)
    {
        var gate = CheckGate(req, out var tenantId);
        if (gate is not null) { return gate; }

        using var _ = AmbientTenantContext.Begin(tenantId);
        using var scope = scopes.CreateScope();
        var svc = Resolve(scope.ServiceProvider);
        var result = await work(svc, tenantId, ct);
        return shape(result);
    }

    private static async Task<IResult> RunBody<TBody>(
        HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct,
        Func<MgmtServices, TBody?, Guid, CancellationToken, Task<object?>> work,
        Func<object?, IResult> shape) where TBody : class
    {
        var gate = CheckGate(req, out var tenantId);
        if (gate is not null) { return gate; }

        TBody? body;
        try { body = await req.ReadFromJsonAsync<TBody>(Json, ct); }
        catch (JsonException) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }

        using var _ = AmbientTenantContext.Begin(tenantId);
        using var scope = scopes.CreateScope();
        var svc = Resolve(scope.ServiceProvider);
        var result = await work(svc, body, tenantId, ct);
        return shape(result);
    }

    private static MgmtServices Resolve(IServiceProvider sp) => new(
        sp.GetRequiredService<IAiAgentService>(),
        sp.GetRequiredService<IAiAgentCacheService>(),
        sp.GetRequiredService<IAuditWriter>(),
        sp.GetRequiredService<IApplicationDbContext>());

    /// <summary>
    /// Aplica los gates de seguridad y resuelve el tenant. Devuelve un IResult si hay que cortar
    /// (404 API deshabilitada, 403 IP no permitida, 401 key mala, 400 tenant ausente/invalido); null si OK.
    /// </summary>
    private static IResult? CheckGate(HttpRequest req, out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var configured = Environment.GetEnvironmentVariable(KeyEnvVar);
        // API deshabilitada si no hay clave: 404 (no revelar que el endpoint existe).
        if (string.IsNullOrWhiteSpace(configured)) { return Results.NotFound(); }

        // Allowlist de IP opcional (defensa en profundidad). Con UseForwardedHeaders la IP ya es la real del cliente.
        var allowIps = Environment.GetEnvironmentVariable(AllowIpsEnvVar);
        if (!string.IsNullOrWhiteSpace(allowIps))
        {
            var remote = req.HttpContext.Connection.RemoteIpAddress?.ToString();
            var allowed = allowIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (remote is null || !allowed.Contains(remote, StringComparer.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        var provided = req.Headers[KeyHeader].ToString();
        if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(provided, configured))
        {
            return Results.Unauthorized();
        }

        var tenantRaw = req.Query["tenant"].ToString();
        if (string.IsNullOrWhiteSpace(tenantRaw) || !Guid.TryParse(tenantRaw, out tenantId) || tenantId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "query 'tenant' (guid) es obligatoria" });
        }

        return null;
    }

    /// <summary>Comparacion en tiempo constante sobre los bytes UTF8 (evita timing attacks sobre la key).</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
