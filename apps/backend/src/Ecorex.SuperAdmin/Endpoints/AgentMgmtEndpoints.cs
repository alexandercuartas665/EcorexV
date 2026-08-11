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

        // 0. Descubrimiento de tenants (NO requiere ?tenant=): lista TODOS con conteo de agentes/lineas.
        //    Es lo primero que llama el operador para saber sobre que tenant operar.
        group.MapGet("/tenants", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunNoTenant(req, scopes, ct, async (db, c) =>
            {
                var tenants = await db.Tenants.IgnoreQueryFilters()
                    .OrderBy(t => t.Name)
                    .Select(t => new { t.Id, t.Name, t.Status, t.Kind })
                    .ToListAsync(c);
                var agentCounts = await db.AiAgents.IgnoreQueryFilters()
                    .GroupBy(a => a.TenantId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(c);
                var lineCounts = await db.WhatsAppLines.IgnoreQueryFilters()
                    .GroupBy(l => l.TenantId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(c);
                var aMap = agentCounts.ToDictionary(x => x.Key, x => x.C);
                var lMap = lineCounts.ToDictionary(x => x.Key, x => x.C);
                return (object)tenants.Select(t => new
                {
                    t.Id, t.Name, t.Status, t.Kind,
                    agents = aMap.TryGetValue(t.Id, out var a) ? a : 0,
                    lines = lMap.TryGetValue(t.Id, out var l) ? l : 0
                }).ToList();
            },
            result => Results.Json(result, Json)));

        // 1. Listado resumido de agentes del tenant.
        group.MapGet("/agents", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, (svc, _, c) => svc.Agents.ListAsync(c),
                result => Results.Json(result, Json)));

        // 2. Detalle: agente + prompts enrutados + recursos + datos cache + catalogo de tools MCP
        //    (cada tool con enabled por-agente segun DisabledTools).
        group.MapGet("/agents/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, (svc, _, c) => BuildAgentDetailAsync(svc, id, c),
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

        // ===== Extension del spec (huecos): tools MCP, lineas, bindings linea<->agente, logs por conversacion, recursos =====

        // 8. Catalogo de tools MCP disponibles (definido en codigo via IAgentToolset). No audita (lectura).
        group.MapGet("/mcp-tools", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, (svc, _, c) => Task.FromResult((object)BuildCatalog(svc.Toolsets)),
                result => Results.Json(result, Json)));

        // 9. Fijar las tools MCP del agente. Body {toolKeys:[...]} = keys HABILITADAS (opt-in). Se validan
        //    contra el catalogo (400 si hay invalidas) y se persiste el complemento como DisabledTools.
        group.MapPut("/agents/{id:guid}/tools", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<ToolsBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body?.ToolKeys is null) { return new ApiOutcome(400, new { error = "toolKeys es obligatorio" }); }
                var all = AllToolNames(svc.Toolsets);
                var invalid = body.ToolKeys.Where(k => !all.Contains(k)).Distinct().ToList();
                if (invalid.Count > 0) { return new ApiOutcome(400, new { error = "tool keys invalidas", invalid, catalogo = all.OrderBy(x => x).ToList() }); }
                var current = await svc.Agents.GetAsync(id, c);
                if (current is null) { return new ApiOutcome(404, null); }
                var enabled = body.ToolKeys.ToHashSet(StringComparer.Ordinal);
                var disabled = all.Where(k => !enabled.Contains(k)).ToList();
                var a = current.Agent;
                var upd = new UpdateAiAgentRequest(a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt,
                    disabled, a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis);
                var updated = await svc.Agents.UpdateAsync(id, upd, SystemActor, c);
                if (updated is null) { return new ApiOutcome(404, null); }
                await AuditAsync(svc, tenant, "mgmt-api.agent.tools", nameof(Ecorex.Domain.Entities.AiAgent), id, new { enabled = body.ToolKeys, disabled }, c);
                return new ApiOutcome(200, await BuildAgentDetailAsync(svc, id, c));
            },
            ShapeOutcome))
            .DisableAntiforgery();

        // 10. Lineas WhatsApp del tenant + a que agente estan vinculadas (si alguna).
        group.MapGet("/lines", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, _, c) =>
            {
                var lines = await svc.Db.WhatsAppLines.AsNoTracking().OrderBy(l => l.InstanceName).ToListAsync(c);
                var bindings = await svc.Db.AiAgentLineBindings.AsNoTracking().ToListAsync(c);
                return (object)lines.Select(l =>
                {
                    var b = bindings.FirstOrDefault(x => x.WhatsAppLineId == l.Id && x.IsConnected);
                    return new AdminLineDto(l.Id, l.InstanceName, l.Provider, l.PhoneNumber, l.Status, b?.AgentId);
                }).ToList();
            },
            result => Results.Json(result, Json)));

        // 11. Vincular una linea a este agente. Body {whatsAppLineId, reassign?}. 409 si la linea ya la
        //     atiende OTRO agente y no se pasa reassign:true.
        group.MapPost("/agents/{id:guid}/line-binding", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<BindBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null || body.WhatsAppLineId == Guid.Empty) { return new ApiOutcome(400, new { ok = false, error = "whatsAppLineId es obligatorio" }); }
                if (await svc.Agents.GetAsync(id, c) is null) { return new ApiOutcome(404, new { ok = false, error = "agente no existe" }); }
                if (!await svc.Db.WhatsAppLines.AsNoTracking().AnyAsync(l => l.Id == body.WhatsAppLineId, c)) { return new ApiOutcome(404, new { ok = false, error = "linea no existe" }); }
                var existing = await svc.Db.AiAgentLineBindings.AsNoTracking().FirstOrDefaultAsync(x => x.WhatsAppLineId == body.WhatsAppLineId, c);
                if (existing is not null && existing.AgentId != id && existing.IsConnected && body.Reassign != true)
                {
                    return new ApiOutcome(409, new { ok = false, error = "la linea ya la atiende otro agente; envia reassign:true para reasignar" });
                }
                await svc.Lines.SetConnectedAsync(id, body.WhatsAppLineId, true, SystemActor, c);
                await AuditAsync(svc, tenant, "mgmt-api.line.bind", nameof(Ecorex.Domain.Entities.AiAgentLineBinding), body.WhatsAppLineId, new { agentId = id, reassign = body.Reassign == true }, c);
                return new ApiOutcome(200, new { ok = true });
            },
            ShapeOutcome))
            .DisableAntiforgery();

        // 12. Desvincular (desconectar) una linea de este agente.
        group.MapDelete("/agents/{id:guid}/line-binding/{lineId:guid}", (Guid id, Guid lineId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, tenant, c) =>
            {
                var binding = await svc.Db.AiAgentLineBindings.AsNoTracking().FirstOrDefaultAsync(x => x.WhatsAppLineId == lineId && x.AgentId == id, c);
                if (binding is null) { return new ApiOutcome(404, null); }
                await svc.Lines.SetConnectedAsync(id, lineId, false, SystemActor, c);
                await AuditAsync(svc, tenant, "mgmt-api.line.unbind", nameof(Ecorex.Domain.Entities.AiAgentLineBinding), lineId, new { agentId = id }, c);
                return new ApiOutcome(204, null);
            },
            ShapeOutcome))
            .DisableAntiforgery();

        // 13. Bitacora por CONVERSACION: listado de conversaciones atendidas (todas las del tenant).
        group.MapGet("/agent-logs", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var take = 50;
            var takeRaw = req.Query["take"].ToString();
            if (!string.IsNullOrWhiteSpace(takeRaw) && int.TryParse(takeRaw, out var t)) { take = Math.Clamp(t, 1, 500); }
            return Run(req, scopes, ct, async (svc, _, c) => (object)await svc.Lines.ListAttendedConversationsAsync(take, c),
                result => Results.Json(result, Json));
        });

        // 14. Entradas de UNA conversacion (orden cronologico). Lista vacia si no hay.
        group.MapGet("/agent-logs/{conversationId:guid}", (Guid conversationId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, _, c) => (object)await svc.Lines.GetConversationLogAsync(conversationId, c),
                result => Results.Json(result, Json)));

        // 15. Recursos del agente (contenido que entrega): crear / actualizar / eliminar.
        group.MapPost("/agents/{id:guid}/resources", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<ResourceBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Name)) { return new ApiOutcome(400, new { error = "name es obligatorio" }); }
                var created = await svc.Agents.AddResourceAsync(new CreateAgentResourceRequest(id, body.Name, body.ResourceType, body.Detail, body.FileUrl, body.FileName), SystemActor, c);
                if (created is null) { return new ApiOutcome(404, null); }
                await AuditAsync(svc, tenant, "mgmt-api.resource.add", nameof(Ecorex.Domain.Entities.AiAgentResource), created.Id, new { agentId = id, created.Name }, c);
                return new ApiOutcome(201, created);
            },
            ShapeOutcome))
            .DisableAntiforgery();

        group.MapPut("/resources/{resourceId:guid}", (Guid resourceId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            RunBody<ResourceBody>(req, scopes, ct, async (svc, body, tenant, c) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Name)) { return new ApiOutcome(400, new { error = "name es obligatorio" }); }
                var updated = await svc.Agents.UpdateResourceAsync(resourceId, new UpdateAgentResourceRequest(body.Name, body.ResourceType, body.Detail, body.FileUrl, body.FileName), SystemActor, c);
                if (updated is null) { return new ApiOutcome(404, null); }
                await AuditAsync(svc, tenant, "mgmt-api.resource.update", nameof(Ecorex.Domain.Entities.AiAgentResource), resourceId, new { updated.Name }, c);
                return new ApiOutcome(200, updated);
            },
            ShapeOutcome))
            .DisableAntiforgery();

        group.MapDelete("/resources/{resourceId:guid}", (Guid resourceId, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Run(req, scopes, ct, async (svc, tenant, c) =>
            {
                var ok = await svc.Agents.DeleteResourceAsync(resourceId, SystemActor, c);
                if (ok) { await AuditAsync(svc, tenant, "mgmt-api.resource.delete", nameof(Ecorex.Domain.Entities.AiAgentResource), resourceId, null, c); }
                return new ApiOutcome(ok ? 204 : 404, null);
            },
            ShapeOutcome))
            .DisableAntiforgery();

        return app;
    }

    // Sin identidad de usuario detras de la clave: el actor de la auditoria es el sistema.
    private static readonly Guid SystemActor = Guid.Empty;

    // --- Cuerpos de entrada propios de esta API ---
    private sealed record SetSystemPromptRequest(string? SystemPrompt);
    private sealed record PromptUpsertBody(string? Name, string? Rule, string? Body);
    private sealed record BitacoraEntryDto(DateTimeOffset OccurredAt, AiAgentRunLogKind Kind, Guid ConversationId, string Title, string? Content, string? Response);

    /// <summary>Servicios tenant-scoped resueltos dentro del scope con el tenant ya fijado.</summary>
    private readonly record struct MgmtServices(IAiAgentService Agents, IAiAgentCacheService Cache, IAuditWriter Audit,
        IApplicationDbContext Db, IAiAgentLineService Lines, IEnumerable<IAgentToolset> Toolsets);

    // --- Cuerpos y DTOs de la extension (tools/lineas/bindings/recursos) ---
    private sealed record ToolsBody(IReadOnlyList<string>? ToolKeys);
    private sealed record BindBody(Guid WhatsAppLineId, bool? Reassign);
    private sealed record ResourceBody(string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName);
    private sealed record AdminLineDto(Guid Id, string Label, WhatsAppProvider Provider, string? Phone, WhatsAppLineStatus Estado, Guid? BoundAgentId);
    private sealed record ApiOutcome(int Status, object? Payload);

    /// <summary>Catalogo de tools MCP (grupos + specs) definido en codigo via IAgentToolset.</summary>
    private static object BuildCatalog(IEnumerable<IAgentToolset> toolsets)
        => toolsets.Select(ts => new
        {
            groupKey = ts.GroupKey,
            groupLabel = ts.GroupLabel,
            tools = ts.GetSpecs().Select(s => new { name = s.Name, description = s.Description }).ToList()
        }).ToList();

    private static HashSet<string> AllToolNames(IEnumerable<IAgentToolset> toolsets)
        => toolsets.SelectMany(ts => ts.GetSpecs()).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Detalle del agente + recursos + prompts + cache + catalogo de tools con enabled por-agente.</summary>
    private static async Task<object?> BuildAgentDetailAsync(MgmtServices svc, Guid id, CancellationToken c)
    {
        var detail = await svc.Agents.GetAsync(id, c);
        if (detail is null) { return null; }
        var cacheFields = await svc.Cache.ListFieldsAsync(id, c);
        var disabled = (detail.Agent.DisabledTools ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var mcpTools = svc.Toolsets.Select(ts => new
        {
            groupKey = ts.GroupKey,
            groupLabel = ts.GroupLabel,
            tools = ts.GetSpecs().Select(s => new { name = s.Name, description = s.Description, enabled = !disabled.Contains(s.Name) }).ToList()
        }).ToList();
        return new { detail.Agent, detail.Resources, detail.Prompts, CacheFields = cacheFields, McpTools = mcpTools };
    }

    /// <summary>Mapea un ApiOutcome (status + payload) al IResult correspondiente.</summary>
    private static IResult ShapeOutcome(object? r) => r is ApiOutcome o
        ? o.Status switch
        {
            200 => Results.Json(o.Payload, Json),
            201 => Results.Json(o.Payload, Json, statusCode: 201),
            204 => Results.NoContent(),
            400 => Results.Json(o.Payload, Json, statusCode: 400),
            409 => Results.Json(o.Payload, Json, statusCode: 409),
            404 => Results.NotFound(),
            _ => Results.StatusCode(o.Status)
        }
        : Results.NotFound();

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
        sp.GetRequiredService<IApplicationDbContext>(),
        sp.GetRequiredService<IAiAgentLineService>(),
        sp.GetRequiredService<IEnumerable<IAgentToolset>>());

    /// <summary>
    /// Ejecuta un handler que NO necesita tenant (p.ej. descubrir tenants): aplica solo el gate de AUTH
    /// (sin ?tenant=), abre un scope y resuelve el DbContext. Las consultas deben usar IgnoreQueryFilters.
    /// </summary>
    private static async Task<IResult> RunNoTenant(
        HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct,
        Func<IApplicationDbContext, CancellationToken, Task<object?>> work,
        Func<object?, IResult> shape)
    {
        var gate = CheckAuthGate(req);
        if (gate is not null) { return gate; }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var result = await work(db, ct);
        return shape(result);
    }

    /// <summary>
    /// Gate de AUTH (sin tenant): 404 si la API esta deshabilitada, 403 IP no permitida, 401 key mala; null si OK.
    /// </summary>
    private static IResult? CheckAuthGate(HttpRequest req)
    {
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

        return null;
    }

    /// <summary>
    /// Gate completo: AUTH + tenant obligatorio por ?tenant=. Devuelve un IResult si hay que cortar
    /// (404/403/401/400); null si OK.
    /// </summary>
    private static IResult? CheckGate(HttpRequest req, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var auth = CheckAuthGate(req);
        if (auth is not null) { return auth; }

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
