using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Agents;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// API REST de CONFIGURACION tenant-scoped (FASE 1). Expone por HTTP TODA la configuracion de
/// Contenedores de datos, Conectores (con su modelo RestApi completo: TokenExchange de 2 pasos,
/// headers arbitrarios, paginacion, mapeo campo->columna, modo Append/Replace/Upsert) y Agentes
/// Colmena, para poder configurar de punta a punta (ej. el conector Siigo de AGROMETALICAS) SIN la
/// UI Blazor. Es una capa DELGADA: envuelve los servicios de aplicacion existentes; no reimplementa
/// logica.
///
/// AUTENTICACION (per-tenant, NUNCA cross-tenant):
/// - Los endpoints de datos usan un Bearer opaco (<see cref="TenantApiToken"/>). El header
///   Authorization: Bearer &lt;token&gt; se hashea (SHA-256) y se busca un token activo; su hash
///   resuelve el tenant y se fija con <see cref="AmbientTenantContext.Begin"/> en un scope de DI
///   aislado (igual que el webhook entrante). El filtro global de tenant aisla todo lo demas.
/// - La EMISION del token (POST /tokens) esta gateada por la COOKIE de admin del tenant (policy
///   TenantMember + rol Owner/Admin): un admin entra al panel, emite el token y lo copia UNA vez;
///   la sesion externa lo usa como Bearer.
/// - AUDITORIA: toda mutacion escribe en super_admin_audit_logs via IAuditWriter (actorType System,
///   reason "config-api", con tenantId).
/// Ver ADR-0058.
/// </summary>
public static class ConfigApiEndpoints
{
    private const string AuditReason = "config-api";
    private static readonly Guid SystemActor = Guid.Empty;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static WebApplication MapConfigApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config").WithTags("config-api");

        // ============ Gestion de tokens (gateada por COOKIE de admin del tenant) ============

        // Emite un token Bearer para el tenant del admin que inicio sesion. Devuelve el valor EN CLARO
        // UNA sola vez; el servidor solo guarda el hash SHA-256.
        group.MapPost("/tokens", async (HttpContext http, IApplicationDbContext db, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!TryTenantAdmin(http, out var tenantId, out var actor)) { return Results.Json(new { error = "requiere sesion de admin del tenant (Owner/Admin)" }, statusCode: 403); }

            TokenIssueBody? body;
            try { body = await http.Request.ReadFromJsonAsync<TokenIssueBody>(Json, ct); }
            catch (JsonException) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }
            var name = (body?.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) { return Results.BadRequest(new { error = "'name' es obligatorio" }); }

            var plain = ApiTokenHasher.Generate();
            var entity = new Domain.Entities.TenantApiToken
            {
                TenantId = tenantId,
                Name = name,
                TokenHash = ApiTokenHasher.Hash(plain),
                Scope = string.IsNullOrWhiteSpace(body?.Scope) ? "admin" : body!.Scope!.Trim()
            };
            db.TenantApiTokens.Add(entity);
            audit.Write(actor, "config-api.token.issue", nameof(Domain.Entities.TenantApiToken), entity.Id,
                previousValue: null, newValue: new { entity.Name, entity.Scope }, tenantId: tenantId,
                reason: AuditReason, actorType: AuditActorType.System);
            await db.SaveChangesAsync(ct);

            // El valor en claro SOLO viaja aqui, esta unica vez.
            return Results.Json(new { id = entity.Id, name = entity.Name, scope = entity.Scope, token = plain, createdAt = entity.CreatedAt }, Json, statusCode: 201);
        }).DisableAntiforgery();

        // Lista los tokens del tenant (sin el valor: solo metadatos).
        group.MapGet("/tokens", async (HttpContext http, IApplicationDbContext db, CancellationToken ct) =>
        {
            if (!TryTenantAdmin(http, out var tenantId, out _)) { return Results.Json(new { error = "requiere sesion de admin del tenant (Owner/Admin)" }, statusCode: 403); }
            var rows = await db.TenantApiTokens.AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Name, t.Scope, t.CreatedAt, t.RevokedAt, t.LastUsedAt, active = t.RevokedAt == null })
                .ToListAsync(ct);
            return Results.Json(rows, Json);
        });

        // Revoca un token (RevokedAt = now). Idempotente: revocar dos veces no cambia nada.
        group.MapPost("/tokens/{id:guid}/revoke", async (Guid id, HttpContext http, IApplicationDbContext db, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!TryTenantAdmin(http, out var tenantId, out var actor)) { return Results.Json(new { error = "requiere sesion de admin del tenant (Owner/Admin)" }, statusCode: 403); }
            var tok = await db.TenantApiTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tok is null) { return Results.NotFound(new { error = "token no encontrado" }); }
            if (tok.RevokedAt is null)
            {
                tok.RevokedAt = DateTimeOffset.UtcNow;
                audit.Write(actor, "config-api.token.revoke", nameof(Domain.Entities.TenantApiToken), tok.Id,
                    previousValue: null, newValue: new { tok.Name }, tenantId: tenantId, reason: AuditReason, actorType: AuditActorType.System);
                await db.SaveChangesAsync(ct);
            }
            return Results.Json(new { id = tok.Id, revoked = true }, Json);
        }).DisableAntiforgery();

        // ============ Contenedores (solo LECTURA en FASE 1) ============

        group.MapGet("/containers", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) => Results.Json(await s.Models.ListAsync(c), Json)));

        group.MapGet("/containers/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var detail = await s.Models.GetAsync(id, c);
                return detail is null ? Results.NotFound(new { error = "contenedor no encontrado" }) : Results.Json(detail, Json);
            }));

        // ============ Conectores (CRUD) ============

        // Lista los conectores. Opcional ?model={nombre|guid} para acotar a un contenedor.
        group.MapGet("/connectors", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var modelRef = req.Query["model"].ToString();
                if (!string.IsNullOrWhiteSpace(modelRef))
                {
                    var m = await ResolveModelAsync(s, modelRef, c);
                    if (m is null) { return Results.NotFound(new { error = $"contenedor '{modelRef}' no encontrado" }); }
                    return Results.Json(await s.Import.ListConnectorsAsync(m.Id, c), Json);
                }
                var all = new List<DataConnectorDto>();
                foreach (var model in await s.Models.ListAsync(c))
                {
                    all.AddRange(await s.Import.ListConnectorsAsync(model.Id, c));
                }
                return Results.Json(all, Json);
            }));

        group.MapGet("/connectors/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var dto = await FindConnectorAsync(s, id, c);
                return dto is null ? Results.NotFound(new { error = "conector no encontrado" }) : Results.Json(dto, Json);
            }));

        // Upsert idempotente por NOMBRE del conector dentro del contenedor (re-ejecutable por scripts).
        group.MapPost("/connectors", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<ConnectorBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                if (body is null) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }
                return await UpsertConnectorAsync(s, tenantId, body, existingId: null, c);
            })).DisableAntiforgery();

        group.MapPut("/connectors/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<ConnectorBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                if (body is null) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }
                var existing = await FindConnectorAsync(s, id, c);
                if (existing is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                return await UpsertConnectorAsync(s, tenantId, body, existingId: id, c, existing);
            })).DisableAntiforgery();

        group.MapDelete("/connectors/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, tenantId, c) =>
            {
                var ok = await s.Import.DeleteConnectorAsync(id, SystemActor, c);
                if (!ok) { return Results.NotFound(new { error = "conector no encontrado" }); }
                await AuditAsync(s, tenantId, "config-api.connector.delete", nameof(Domain.Entities.DataConnector), id, null, c);
                return Results.Json(new { id, deleted = true }, Json);
            })).DisableAntiforgery();

        // Setea SOLO el secreto de auth (sobre TLS; se cifra server-side, NUNCA se devuelve).
        group.MapPut("/connectors/{id:guid}/secret", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<SecretBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Secret)) { return Results.BadRequest(new { error = "'secret' es obligatorio" }); }
                var existing = await FindConnectorAsync(s, id, c);
                if (existing is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                var reqSave = ToSaveRequest(existing, credentials: body.Secret);
                var saved = await s.Import.SaveConnectorAsync(reqSave, SystemActor, c);
                if (saved is null) { return Results.BadRequest(new { error = "no se pudo guardar el secreto" }); }
                await AuditAsync(s, tenantId, "config-api.connector.secret-set", nameof(Domain.Entities.DataConnector), id, new { existing.Name }, c);
                return Results.Json(new { id, secretSet = true }, Json);
            })).DisableAntiforgery();

        // Probe: ejecuta auth (incluye TokenExchange) + headers + una pagina; devuelve campos + muestra.
        group.MapPost("/connectors/{id:guid}/probe", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var arrayPath = req.Query["arrayPath"].ToString();
                var result = await s.Api.ProbeAsync(id, string.IsNullOrWhiteSpace(arrayPath) ? null : arrayPath, c);
                return Results.Json(result, Json);
            })).DisableAntiforgery();

        // Preview: como el probe pero devuelve, para UNA fila de muestra, el mapeo YA aplicado
        // (columna -> valor) resolviendo las rutas ANIDADAS/INDEXADAS del conector con el mismo
        // resolver del run. Deja ver si alguna ruta queda vacia ANTES de disparar el run.
        group.MapPost("/connectors/{id:guid}/preview", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var connector = await s.Db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                if (connector.ContainerId is not Guid targetId) { return Results.BadRequest(new { error = "el conector no tiene tabla destino (targetTable)" }); }

                var cols = await s.Db.DataContainerColumns.AsNoTracking()
                    .Where(x => x.ContainerId == targetId)
                    .Select(x => new { x.Id, x.Name })
                    .ToListAsync(c);
                var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in cols) { byName[col.Name] = col.Id; }

                var arrayOverride = req.Query["arrayPath"].ToString();
                // Modo Append: el preview no necesita clave; solo el mapeo campo->columna del conector.
                var plan = ConnectorRunPlanner.Build(id, targetId, connector.MappingJson, byName,
                    ApiImportMode.Append, keyColumnName: null,
                    arrayPathOverride: string.IsNullOrWhiteSpace(arrayOverride) ? null : arrayOverride);
                if (!plan.Ok) { return Results.BadRequest(new { error = plan.Error }); }

                var idToName = cols.ToDictionary(x => x.Id, x => x.Name);
                var columnToPath = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in plan.Request!.ColumnToField)
                {
                    if (idToName.TryGetValue(kv.Key, out var name)) { columnToPath[name] = kv.Value; }
                }

                var result = await s.Api.PreviewAsync(id, columnToPath, plan.Request.ArrayPath, c);
                return Results.Json(result, Json);
            })).DisableAntiforgery();

        // Run: dispara la carga server-direct y devuelve runId. El modo/keyColumn son politica de la
        // corrida (van en el body); el mapeo/paginacion salen del conector persistido.
        group.MapPost("/connectors/{id:guid}/run", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<RunBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                var connector = await s.Db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                if (connector.ContainerId is not Guid targetId) { return Results.BadRequest(new { error = "el conector no tiene tabla destino (targetTable)" }); }

                var cols = await s.Db.DataContainerColumns.AsNoTracking()
                    .Where(x => x.ContainerId == targetId)
                    .Select(x => new { x.Id, x.Name })
                    .ToListAsync(c);
                var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in cols) { byName[col.Name] = col.Id; }

                var mode = body?.Mode ?? ApiImportMode.Append;
                var plan = ConnectorRunPlanner.Build(id, targetId, connector.MappingJson, byName, mode, body?.KeyColumn, body?.ArrayPath);
                if (!plan.Ok) { return Results.BadRequest(new { error = plan.Error }); }

                var runId = s.Runs.Start(tenantId, id);
                try
                {
                    var outcome = await s.Api.ImportAsync(plan.Request!, SystemActor, c);
                    s.Runs.Complete(runId, outcome);
                }
                catch (Exception ex)
                {
                    s.Runs.Fail(runId, ex.Message);
                }
                await AuditAsync(s, tenantId, "config-api.connector.run", nameof(Domain.Entities.DataConnector), id, new { runId, mode = mode.ToString() }, c);
                var state = s.Runs.Get(runId, tenantId);
                return Results.Json(new { runId, status = state?.Status ?? "running" }, Json, statusCode: 202);
            })).DisableAntiforgery();

        // ============ Programacion (scheduling) de un conector ============
        // Fase 2 (ADR-0060): expone el scheduling del conector via HTTP. El proceso programado corre
        // SERVER-DIRECT (mismo camino que /run): reconcilia por Mode/KeyColumn en vez de duplicar.

        // Crea/edita la programacion del conector (upsert por conector: un conector = una programacion).
        group.MapPut("/connectors/{id:guid}/schedule", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<ScheduleBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                if (body is null) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }
                var connector = await FindConnectorAsync(s, id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }

                var mode = body.Mode ?? ApiImportMode.Replace;
                if (mode == ApiImportMode.Upsert && string.IsNullOrWhiteSpace(body.KeyColumn))
                {
                    return Results.BadRequest(new { error = "para mode=Upsert indica 'keyColumn' (nombre de la columna clave)" });
                }

                // Un conector tiene a lo sumo una programacion: se busca la existente para editarla.
                var existing = (await s.Import.ListProcessesAsync(connector.ModelId, c))
                    .FirstOrDefault(p => p.ConnectorId == connector.Id);

                var name = $"Programacion {connector.Name}";
                if (name.Length > 150) { name = name[..150]; }

                var save = new SaveImportProcessRequest(
                    Id: existing?.Id,
                    ModelId: connector.ModelId,
                    ConnectorId: connector.Id,
                    ClientId: null,            // server-direct: no lo ejecuta un agente.
                    Name: name,
                    ScheduleKind: body.ScheduleKind,
                    IntervalMinutes: body.IntervalMinutes,
                    CronExpression: body.CronExpression,
                    IsActive: body.IsActive ?? true,
                    Mode: mode,
                    KeyColumn: body.KeyColumn);

                ImportProcessDto? saved;
                try
                {
                    // SaveProcessAsync valida el horario con el MISMO parser Cronos del scheduler y lanza
                    // si es invalido: se traduce a 400 con el motivo, sin activar una programacion rota.
                    saved = await s.Import.SaveProcessAsync(save, SystemActor, c);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                if (saved is null) { return Results.BadRequest(new { error = "no se pudo guardar la programacion (revisa el conector y su contenedor)" }); }

                var action = existing is null ? "config-api.schedule.create" : "config-api.schedule.update";
                await AuditAsync(s, tenantId, action, nameof(Domain.Entities.ImportProcess), saved.Id,
                    new { connectorId = connector.Id, saved.ScheduleKind, mode = mode.ToString(), saved.KeyColumn }, c);
                return Results.Json(ScheduleView(saved), Json, statusCode: existing is null ? 201 : 200);
            })).DisableAntiforgery();

        // Estado de la programacion del conector (nextRunAt, lastRunAt, isActive, disabledReason...).
        group.MapGet("/connectors/{id:guid}/schedule", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var connector = await FindConnectorAsync(s, id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                var process = (await s.Import.ListProcessesAsync(connector.ModelId, c))
                    .FirstOrDefault(p => p.ConnectorId == connector.Id);
                if (process is null) { return Results.NotFound(new { error = "el conector no tiene programacion" }); }
                return Results.Json(ScheduleView(process), Json);
            }));

        // Bitacora de corridas de la programacion del conector (mas reciente primero).
        group.MapGet("/connectors/{id:guid}/runs", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var connector = await FindConnectorAsync(s, id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                var process = (await s.Import.ListProcessesAsync(connector.ModelId, c))
                    .FirstOrDefault(p => p.ConnectorId == connector.Id);
                if (process is null) { return Results.NotFound(new { error = "el conector no tiene programacion" }); }
                var take = int.TryParse(req.Query["take"].ToString(), out var t) && t is > 0 and <= 200 ? t : 20;
                var runs = await s.Import.ListRunsAsync(process.Id, take, c);
                return Results.Json(runs, Json);
            }));

        // Quita la programacion del conector (su bitacora se borra con ella).
        group.MapDelete("/connectors/{id:guid}/schedule", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, tenantId, c) =>
            {
                var connector = await FindConnectorAsync(s, id, c);
                if (connector is null) { return Results.NotFound(new { error = "conector no encontrado" }); }
                var process = (await s.Import.ListProcessesAsync(connector.ModelId, c))
                    .FirstOrDefault(p => p.ConnectorId == connector.Id);
                if (process is null) { return Results.NotFound(new { error = "el conector no tiene programacion" }); }
                var ok = await s.Import.DeleteProcessAsync(process.Id, SystemActor, c);
                if (!ok) { return Results.NotFound(new { error = "programacion no encontrada" }); }
                await AuditAsync(s, tenantId, "config-api.schedule.delete", nameof(Domain.Entities.ImportProcess), process.Id,
                    new { connectorId = connector.Id }, c);
                return Results.Json(new { connectorId = connector.Id, scheduleId = process.Id, deleted = true }, Json);
            })).DisableAntiforgery();

        // Estado de una corrida (insertadas/actualizadas/borradas/errores).
        group.MapGet("/runs/{id:guid}", (Guid id, HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, (s, tenantId, _) =>
            {
                var state = s.Runs.Get(id, tenantId);
                if (state is null) { return Task.FromResult(Results.NotFound(new { error = "corrida no encontrada" })); }
                var o = state.Outcome;
                return Task.FromResult(Results.Json(new
                {
                    runId = state.RunId,
                    connectorId = state.ConnectorId,
                    status = state.Status,
                    startedAt = state.StartedAt,
                    finishedAt = state.FinishedAt,
                    inserted = o?.Inserted ?? 0,
                    updated = o?.Updated ?? 0,
                    deleted = o?.Deleted ?? 0,
                    failed = o?.Failed ?? 0,
                    errors = (object?)o?.Errors ?? state.Error,
                }, Json));
            }));

        // ============ Agentes Colmena (data_clients) ============

        group.MapGet("/agents", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            Bearer(req, scopes, ct, async (s, _, c) =>
            {
                var agents = await s.Agents.ListAsync(c);
                // Presencia/ultima conexion (best-effort): ultima actividad registrada por ClientId.
                var lastByClient = await s.Db.AgentActivityLogs.AsNoTracking()
                    .GroupBy(a => a.ClientId)
                    .Select(g => new { ClientId = g.Key, Last = g.Max(x => x.StartedAt) })
                    .ToListAsync(c);
                // AgentActivityLog.ClientId es el ClientId PUBLICO (string "cli_..."), no el Guid de la fila.
                var lastMap = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                foreach (var x in lastByClient) { if (!string.IsNullOrEmpty(x.ClientId)) { lastMap[x.ClientId] = x.Last; } }
                var shaped = agents.Select(a => new
                {
                    a.Id, a.Name, a.Description, a.ClientId, a.HasSecret, a.IsActive,
                    lastSeenAt = lastMap.TryGetValue(a.ClientId, out var l) ? (DateTimeOffset?)l : null
                });
                return Results.Json(shaped, Json);
            }));

        // Registra un agente colmena: devuelve clientId + secreto UNA sola vez.
        group.MapPost("/agents", (HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
            BearerBody<AgentBody>(req, scopes, ct, async (s, body, tenantId, c) =>
            {
                var name = (body?.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) { return Results.BadRequest(new { error = "'name' es obligatorio" }); }
                var (client, secret) = await s.Agents.SaveAsync(new SaveAgentClientRequest(null, name, body?.Description, true), SystemActor, c);
                await AuditAsync(s, tenantId, "config-api.agent.register", "DataClient", client.Id, new { client.Name, client.ClientId }, c);
                return Results.Json(new
                {
                    id = client.Id,
                    name = client.Name,
                    clientId = client.ClientId,
                    // El secreto en claro SOLO viaja aqui, esta unica vez.
                    clientSecret = secret?.ClientSecret
                }, Json, statusCode: 201);
            })).DisableAntiforgery();

        return app;
    }

    // ================= Nucleo de conectores (upsert + mapeo body<->servicio) =================

    private static async Task<IResult> UpsertConnectorAsync(ConfigServices s, Guid tenantId, ConnectorBody body, Guid? existingId, CancellationToken ct, DataConnectorDto? existing = null)
    {
        var name = (body.Name ?? "").Trim();
        if (existingId is null && string.IsNullOrWhiteSpace(name)) { return Results.BadRequest(new { error = "'name' es obligatorio" }); }

        // Resolver contenedor (modelo). En PUT, si no viene, se conserva el del conector existente.
        Guid modelId;
        if (!string.IsNullOrWhiteSpace(body.Model))
        {
            var m = await ResolveModelAsync(s, body.Model!, ct);
            if (m is null) { return Results.NotFound(new { error = $"contenedor '{body.Model}' no encontrado" }); }
            modelId = m.Id;
        }
        else if (existing is not null)
        {
            modelId = existing.ModelId;
        }
        else
        {
            return Results.BadRequest(new { error = "'model' (contenedor) es obligatorio" });
        }

        // Resolver tabla destino (opcional).
        Guid? targetId = null;
        if (!string.IsNullOrWhiteSpace(body.TargetTable))
        {
            targetId = await ResolveTableAsync(s, modelId, body.TargetTable, ct);
            if (targetId is null) { return Results.NotFound(new { error = $"tabla destino '{body.TargetTable}' no encontrada en el contenedor" }); }
        }
        else if (existingId is not null && existing is not null)
        {
            targetId = existing.ContainerId;
        }

        // Idempotencia por nombre en POST: si ya existe un conector con ese nombre en el contenedor, se ACTUALIZA.
        Guid? idToSave = existingId;
        if (idToSave is null)
        {
            var siblings = await s.Import.ListConnectorsAsync(modelId, ct);
            idToSave = siblings.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        if (existingId is not null) { name = string.IsNullOrWhiteSpace(name) && existing is not null ? existing.Name : name; }

        var authKind = body.AuthKind ?? ConnectorAuthKind.None;

        // Headers estaticos (ej. Partner-Id de Siigo). Reusa ConnectorRestConfig (no duplica el modelo).
        string? headersJson = null;
        if (body.Headers is { Count: > 0 })
        {
            var hs = body.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Name)).Select(h => new ConnectorHeader(h.Name!.Trim(), h.Value ?? "")).ToList();
            if (hs.Count > 0) { headersJson = ConnectorRestConfig.Serialize(hs); }
        }
        else if (existing is not null) { headersJson = existing.HeadersJson; }

        // TokenExchange (auth de 2 pasos). Reusa TokenExchangeConfig.
        string? tokenExchangeJson = null;
        if (authKind == ConnectorAuthKind.TokenExchange)
        {
            if (body.TokenExchange is { } tx)
            {
                var cfg = new TokenExchangeConfig(
                    TokenUrl: tx.TokenUrl,
                    Method: string.IsNullOrWhiteSpace(tx.Method) ? "POST" : tx.Method,
                    Username: tx.Username,
                    SecretParamName: tx.SecretParamName,
                    TokenJsonPath: tx.TokenJsonPath,
                    ApplyHeaderName: string.IsNullOrWhiteSpace(tx.ApplyHeaderName) ? "Authorization" : tx.ApplyHeaderName,
                    ApplyPrefix: tx.ApplyPrefix ?? "Bearer ",
                    BodyFormat: string.IsNullOrWhiteSpace(tx.BodyFormat) ? "json" : tx.BodyFormat);
                tokenExchangeJson = ConnectorRestConfig.Serialize(cfg);
            }
            else if (existing is not null) { tokenExchangeJson = existing.TokenExchangeJson; }
        }

        // MappingJson = RestFetchSpec (arrayPath + paginacion + mapeo campo->columna). Misma forma que
        // consume el agente y que lee ConnectorRunPlanner.
        string? mappingJson;
        if (body.Fields is not null || body.Paging is not null || body.ArrayPath is not null)
        {
            mappingJson = BuildMappingJson(body);
        }
        else { mappingJson = existing?.MappingJson; }

        var save = new SaveDataConnectorRequest(
            Id: idToSave,
            ModelId: modelId,
            Name: name,
            Kind: ConnectorKind.RestApi,
            EndpointUrl: body.EndpointUrl ?? existing?.EndpointUrl,
            HttpMethod: body.HttpMethod ?? existing?.HttpMethod ?? "GET",
            AuthKind: authKind,
            DbEngine: null, Host: null, Port: null, DatabaseName: null, Username: null,
            Credentials: body.Secret,   // en claro sobre TLS; el servicio lo cifra. null = conserva.
            Query: null,
            MappingJson: mappingJson,
            IsActive: body.IsActive ?? existing?.IsActive ?? true,
            ContainerId: targetId,
            HeadersJson: headersJson,
            TokenExchangeJson: tokenExchangeJson);

        var saved = await s.Import.SaveConnectorAsync(save, SystemActor, ct);
        if (saved is null) { return Results.BadRequest(new { error = "no se pudo guardar el conector (revisa 'model', 'targetTable' y 'name')" }); }

        var action = idToSave is null ? "config-api.connector.create" : "config-api.connector.update";
        await AuditAsync(s, tenantId, action, nameof(Domain.Entities.DataConnector), saved.Id, new { saved.Name, saved.AuthKind }, ct);
        return Results.Json(saved, Json, statusCode: idToSave is null ? 201 : 200);
    }

    /// <summary>Serializa el MappingJson (RestFetchSpec) desde el body: arrayPath + paginacion + fields.</summary>
    private static string? BuildMappingJson(ConnectorBody body)
    {
        object? paging = null;
        if (body.Paging is { } p && !string.Equals(p.Mode, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Mode))
        {
            paging = new
            {
                mode = string.Equals(p.Mode, "Page", StringComparison.OrdinalIgnoreCase) ? "Page" : "Offset",
                offsetParam = string.IsNullOrWhiteSpace(p.OffsetParam) ? "start" : p.OffsetParam,
                limitParam = string.IsNullOrWhiteSpace(p.LimitParam) ? "limit" : p.LimitParam,
                pageParam = string.IsNullOrWhiteSpace(p.PageParam) ? "page" : p.PageParam,
                startValue = p.StartValue,
                pageSize = p.PageSize <= 0 ? 100 : p.PageSize,
                maxPages = p.MaxPages <= 0 ? 1000 : p.MaxPages
            };
        }
        var fields = (body.Fields ?? new List<FieldBody>())
            .Where(f => !string.IsNullOrWhiteSpace(f.Column) && !string.IsNullOrWhiteSpace(f.Path))
            .Select(f => new { column = f.Column!.Trim(), path = f.Path!.Trim() })
            .ToList();
        var spec = new
        {
            baseUrl = body.EndpointUrl ?? "",
            listPath = "",
            httpMethod = string.IsNullOrWhiteSpace(body.HttpMethod) ? "GET" : body.HttpMethod,
            authKind = (body.AuthKind ?? ConnectorAuthKind.None).ToString(),
            arrayPath = string.IsNullOrWhiteSpace(body.ArrayPath) ? null : body.ArrayPath,
            paging,
            fields = fields.Count > 0 ? fields : null
        };
        return JsonSerializer.Serialize(spec, Json);
    }

    /// <summary>Reconstruye un SaveDataConnectorRequest preservando TODO el conector y cambiando solo el secreto.</summary>
    private static SaveDataConnectorRequest ToSaveRequest(DataConnectorDto dto, string? credentials) =>
        new(
            Id: dto.Id, ModelId: dto.ModelId, Name: dto.Name, Kind: dto.Kind,
            EndpointUrl: dto.EndpointUrl, HttpMethod: dto.HttpMethod, AuthKind: dto.AuthKind,
            DbEngine: dto.DbEngine, Host: dto.Host, Port: dto.Port, DatabaseName: dto.DatabaseName, Username: dto.Username,
            Credentials: credentials, Query: dto.Query, MappingJson: dto.MappingJson, IsActive: dto.IsActive,
            ContainerId: dto.ContainerId, HeadersJson: dto.HeadersJson, TokenExchangeJson: dto.TokenExchangeJson);

    /// <summary>Forma de salida de una programacion: el estado que interesa a un cliente HTTP, sin
    /// exponer los ids internos de contenedor/cliente.</summary>
    private static object ScheduleView(ImportProcessDto p) => new
    {
        scheduleId = p.Id,
        connectorId = p.ConnectorId,
        scheduleKind = p.ScheduleKind.ToString(),
        cronExpression = p.CronExpression,
        intervalMinutes = p.IntervalMinutes,
        mode = p.Mode.ToString(),
        keyColumn = p.KeyColumn,
        isActive = p.IsActive,
        nextRunAt = p.NextRunAt,
        lastRunAt = p.LastRunAt,
        disabledReason = p.DisabledReason
    };

    private static async Task<DataModelDto?> ResolveModelAsync(ConfigServices s, string modelRef, CancellationToken ct)
    {
        var models = await s.Models.ListAsync(ct);
        if (Guid.TryParse(modelRef, out var g)) { return models.FirstOrDefault(m => m.Id == g); }
        return models.FirstOrDefault(m => string.Equals(m.Name, modelRef, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Guid?> ResolveTableAsync(ConfigServices s, Guid modelId, string? tableRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tableRef)) { return null; }
        var detail = await s.Models.GetAsync(modelId, ct);
        if (detail is null) { return null; }
        if (Guid.TryParse(tableRef, out var g)) { return detail.Tables.Any(t => t.Id == g) ? g : null; }
        return detail.Tables.FirstOrDefault(t => string.Equals(t.Name, tableRef, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static async Task<DataConnectorDto?> FindConnectorAsync(ConfigServices s, Guid id, CancellationToken ct)
    {
        var modelId = await s.Db.DataConnectors.AsNoTracking().Where(c => c.Id == id).Select(c => c.ModelId).FirstOrDefaultAsync(ct);
        if (modelId is null) { return null; }
        var list = await s.Import.ListConnectorsAsync(modelId.Value, ct);
        return list.FirstOrDefault(c => c.Id == id);
    }

    // ================= Auth: cookie de admin y Bearer per-tenant =================

    /// <summary>Valida la COOKIE de admin del tenant: exige claim tenant_id + rol Owner/Admin.</summary>
    private static bool TryTenantAdmin(HttpContext http, out Guid tenantId, out Guid actor)
    {
        tenantId = Guid.Empty;
        actor = SystemActor;
        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true) { return false; }
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out tenantId)) { return false; }
        var role = user.FindFirst("tenant_role")?.Value;
        if (!string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) { return false; }
        if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) { actor = uid; }
        return true;
    }

    private static async Task<IResult> Bearer(HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct,
        Func<ConfigServices, Guid, CancellationToken, Task<IResult>> work)
    {
        var token = ExtractBearer(req);
        if (token is null) { return Results.Json(new { error = "falta Authorization: Bearer <token>" }, Json, statusCode: 401); }

        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var hash = ApiTokenHasher.Hash(token);
        var tok = await db.TenantApiTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (tok is null) { return Results.Json(new { error = "token invalido o revocado" }, Json, statusCode: 401); }

        var tenantId = tok.TenantId;
        using var _ = AmbientTenantContext.Begin(tenantId);
        tok.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var svc = ResolveConfig(sp);
        return await work(svc, tenantId, ct);
    }

    private static async Task<IResult> BearerBody<TBody>(HttpRequest req, IServiceScopeFactory scopes, CancellationToken ct,
        Func<ConfigServices, TBody?, Guid, CancellationToken, Task<IResult>> work) where TBody : class
    {
        TBody? body;
        try { body = await req.ReadFromJsonAsync<TBody>(Json, ct); }
        catch (JsonException) { return Results.BadRequest(new { error = "cuerpo JSON invalido" }); }
        return await Bearer(req, scopes, ct, (s, tenantId, c) => work(s, body, tenantId, c));
    }

    private static string? ExtractBearer(HttpRequest req)
    {
        var header = req.Headers.Authorization.ToString();
        const string p = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { return null; }
        var t = header[p.Length..].Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static ConfigServices ResolveConfig(IServiceProvider sp) => new(
        sp.GetRequiredService<IDataModelService>(),
        sp.GetRequiredService<IDataImportConfigService>(),
        sp.GetRequiredService<IApiImportService>(),
        sp.GetRequiredService<IAgentClientService>(),
        sp.GetRequiredService<IAuditWriter>(),
        sp.GetRequiredService<IApplicationDbContext>(),
        sp.GetRequiredService<ConfigRunStore>());

    private static async Task AuditAsync(ConfigServices s, Guid tenantId, string action, string entityName, Guid? entityId, object? newValue, CancellationToken ct)
    {
        s.Audit.Write(SystemActor, action, entityName, entityId, previousValue: null, newValue: newValue,
            tenantId: tenantId, reason: AuditReason, actorType: AuditActorType.System);
        await s.Db.SaveChangesAsync(ct);
    }

    private readonly record struct ConfigServices(
        IDataModelService Models,
        IDataImportConfigService Import,
        IApiImportService Api,
        IAgentClientService Agents,
        IAuditWriter Audit,
        IApplicationDbContext Db,
        ConfigRunStore Runs);

    // ================= Cuerpos de entrada de la API =================

    private sealed record TokenIssueBody(string? Name, string? Scope);
    private sealed record SecretBody(string? Secret);
    private sealed record AgentBody(string? Name, string? Description);
    private sealed record RunBody(ApiImportMode? Mode, string? KeyColumn, string? ArrayPath);
    private sealed record ScheduleBody(
        ImportScheduleKind ScheduleKind,
        string? CronExpression,
        int? IntervalMinutes,
        ApiImportMode? Mode,
        string? KeyColumn,
        bool? IsActive);

    private sealed record HeaderBody(string? Name, string? Value);
    private sealed record FieldBody(string? Column, string? Path);
    private sealed record PagingBody(string? Mode, string? OffsetParam, string? LimitParam, string? PageParam, int StartValue, int PageSize, int MaxPages);
    private sealed record TokenExchangeBody(string? TokenUrl, string? Method, string? Username, string? SecretParamName, string? TokenJsonPath, string? ApplyHeaderName, string? ApplyPrefix, string? BodyFormat);

    private sealed record ConnectorBody(
        string? Name,
        string? Model,
        string? TargetTable,
        string? EndpointUrl,
        string? HttpMethod,
        ConnectorAuthKind? AuthKind,
        List<HeaderBody>? Headers,
        TokenExchangeBody? TokenExchange,
        string? ArrayPath,
        PagingBody? Paging,
        List<FieldBody>? Fields,
        string? Secret,
        bool? IsActive);
}
