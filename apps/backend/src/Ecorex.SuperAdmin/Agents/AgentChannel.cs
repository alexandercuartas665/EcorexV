using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Cableado del canal del Agente Conector On-Prem en el host SuperAdmin (doc 03): esquema bearer
/// "Agent" (no-default, no toca la auth de cookies), registro de presencia, emisor de token y los
/// endpoints REST (token/push/status). El hub en si es <see cref="AgenteHub"/>.
/// </summary>
public static class AgentChannel
{
    /// <summary>Nombre del esquema de autenticacion del hub (bearer del JWT de agente).</summary>
    public const string Scheme = "Agent";

    /// <summary>Ruta del hub (coincide con <see cref="AgentProtocol.HubRoute"/>).</summary>
    public const string HubPath = AgentProtocol.HubRoute;

    public static IServiceCollection AddAgentChannel(this IServiceCollection services, IConfiguration config)
    {
        // Reusa la seccion "Jwt" del backbone. Si no hay SigningKey configurada, genera una clave
        // efimera (dev/local) para NO romper el arranque; el mismo proceso firma y valida, asi que es
        // consistente. En produccion se debe fijar Jwt:SigningKey (tokens sobreviven reinicios).
        var issuer = config["Jwt:Issuer"] ?? "Ecorex";
        var audience = config["Jwt:Audience"] ?? "Ecorex";
        var signingKey = config["Jwt:SigningKey"];
        var keyBytes = string.IsNullOrWhiteSpace(signingKey)
            ? RandomNumberGenerator.GetBytes(48)
            : Encoding.UTF8.GetBytes(signingKey);
        var key = new SymmetricSecurityKey(keyBytes);

        services.AddSingleton(new AgentTokenIssuer(key, issuer, audience, minutes: 15));
        services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
        services.AddSingleton<AgentNonceCache>();

        // El AgenteHub recibe FetchResult (datos) y BrowserResult (screenshots base64) que superan el
        // limite por defecto de SignalR (32 KB). Se sube solo para este hub (32 MB), sin tocar los demas.
        services.AddSignalR().AddHubOptions<AgenteHub>(o => o.MaximumReceiveMessageSize = 32L * 1024 * 1024);
        // Ola 3: orquestador de ingesta via agente (pending-fetch + ingesta al contenedor).
        services.AddSingleton<IAgentImportService, AgentImportService>();
        // Runtime del flujo de extraccion (modulo 000730, Olas 3-4). El canal request/response une el
        // envio y la respuesta del Navegador por correlationId (singleton: mantiene las esperas). El
        // runtime ejecuta el flujo paso a paso (singleton, corre en scopes propios). El orquestador del
        // paso de IA y la bitacora son scoped (usan DbContext).
        services.AddSingleton<IBrowserActionChannel, BrowserActionChannel>();
        // Bitacora transversal de actividad de los agentes colmena (ADR-0045, Ola 2). Singleton porque abre
        // su propio scope por escritura (se llama desde el background del runtime, sin scope de peticion).
        services.AddSingleton<IAgentActivityLog, AgentActivityLogWriter>();
        services.AddSingleton<IBrowserRunService, BrowserRunService>();
        services.AddScoped<IAiStepOrchestrator, AiStepOrchestrator>();
        services.AddScoped<IAiProviderResolver, AiProviderResolver>();
        services.AddScoped<IScrapeRowSink, ScrapeRowSink>();
        services.AddScoped<IScrapeFlowRunLog, ScrapeFlowRunLog>();
        // Ejecuta una programacion AHORA (boton "Actualizar datos"). Scoped: usa el DbContext y el
        // tenant del request. Lo reusa el scheduler para que el horario y el boton hagan lo mismo.
        services.AddScoped<IProcessRunner, ProcessRunner>();
        // Bitacora de corridas. Scoped (DbContext); el canal, que es singleton, la resuelve por scope.
        services.AddScoped<IImportRunLog, ImportRunLog>();
        // Motor de horarios: quien decide QUE toca disparar. El worker (hosted service) lo resuelve
        // por scope y con el tenant fijado, igual que el de 000889.
        services.AddScoped<IImportScheduleDispatcher, ImportScheduleDispatcher>();

        // Esquema bearer nombrado SOLO para el hub. AddAuthentication() sin argumentos no cambia el
        // esquema por defecto (cookie), solo agrega este.
        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "client_id",
            };
            // SignalR sobre WebSockets manda el JWT por query (?access_token=...): lo tomamos solo
            // para la ruta del hub del agente.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments(HubPath))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
            };
        });

        return services;
    }

    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        // POST /api/agente/token: handshake opcion A (doc 02 s2). Anonimo + rate-limit-friendly.
        app.MapPost("/api/agente/token", async (
            AgentTokenRequest body,
            IApplicationDbContext db,
            ISecretProtector protector,
            AgentTokenIssuer issuer,
            AgentNonceCache nonces,
            CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.ClientId)
                || string.IsNullOrWhiteSpace(body.Nonce) || string.IsNullOrWhiteSpace(body.Hmac))
            {
                return Results.Json(new { error = "Solicitud incompleta." }, statusCode: 400);
            }

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(nowUnix - body.Ts) > 120)
            {
                return Results.Json(new { error = "Marca de tiempo fuera de rango." }, statusCode: 401);
            }

            if (!nonces.TryUse(body.Nonce))
            {
                return Results.Json(new { error = "Nonce repetido." }, statusCode: 401);
            }

            // DataClient por clientId, CROSS-tenant (endpoint anonimo, sin contexto de tenant).
            var client = await db.DataClients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ClientId == body.ClientId && c.IsActive, ct);
            if (client is null || string.IsNullOrEmpty(client.ClientSecretEncrypted))
            {
                return Results.Json(new { error = "Cliente invalido o inactivo." }, statusCode: 401);
            }

            string secret;
            try { secret = protector.Unprotect(client.ClientSecretEncrypted); }
            catch { return Results.Json(new { error = "Credencial ilegible." }, statusCode: 401); }

            var expected = AgentHmac.Compute(secret, body.ClientId, body.Ts, body.Nonce);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(body.Hmac)))
            {
                return Results.Json(new { error = "Firma invalida." }, statusCode: 401);
            }

            var token = issuer.Issue(client.ClientId, client.TenantId);
            return Results.Json(token);
        }).AllowAnonymous().DisableAntiforgery();

        // POST /api/agente/push/{clientId}: disparador MANUAL de una orden de prueba (doc 05 Ola 1).
        // Lo reemplaza el scheduler en una ola posterior. Restringido a operador de plataforma.
        app.MapPost("/api/agente/push/{clientId}", async (
            string clientId,
            IAgentRegistry registry,
            IHubContext<AgenteHub> hub,
            CancellationToken ct) =>
        {
            var presence = registry.Get(clientId);
            if (presence is null)
            {
                return Results.Json(new { ok = false, error = "Agente offline." }, statusCode: 409);
            }

            var req = new FetchRequestMsg(
                CorrelationId: Guid.NewGuid().ToString("N")[..8],
                TenantId: presence.TenantId.ToString(),
                Connector: new ConnectorSpec("Database", DbEngine: "SqlServer", Host: "10.0.0.20", Port: 1433, Database: "db3dev", Username: "ecorex_ro"),
                Query: new QuerySpec("SELECT id, name FROM items WHERE updated_at > @since",
                    new Dictionary<string, string?> { ["since"] = "2026-07-01T00:00:00Z" }),
                Paging: new PagingSpec("Offset", 500, 100000));

            await hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.FetchRequest, req, ct);
            return Results.Json(new { ok = true, correlationId = req.CorrelationId });
        }).RequireAuthorization("PlatformOperator");

        // GET /api/agente/status/{clientId}: estado en linea/offline (para el panel web).
        app.MapGet("/api/agente/status/{clientId}", (string clientId, IAgentRegistry registry) =>
        {
            var p = registry.Get(clientId);
            return Results.Json(new { clientId, online = p is not null, host = p?.Host, version = p?.Version, lastSeen = p?.LastSeen });
        }).RequireAuthorization("PlatformOperator");

        // POST /api/agente/dev/seed-client: SOLO en Development. Upserta un DataClient de prueba
        // (clientId + secreto conocidos) bajo el primer tenant, para verificar el canal E2E sin
        // depender del alta por UI. NO existe en produccion.
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/api/agente/dev/seed-client", async (
                IApplicationDbContext db,
                ISecretProtector protector,
                CancellationToken ct) =>
            {
                var tenantId = await db.Tenants.IgnoreQueryFilters().Select(t => t.Id).FirstOrDefaultAsync(ct);
                if (tenantId == Guid.Empty)
                {
                    return Results.Json(new { error = "No hay tenants en la BD." }, statusCode: 400);
                }

                const string clientId = "cli_dev_agent";
                const string secret = "dev-secret-ola-b";

                using (AmbientTenantContext.Begin(tenantId))
                {
                    var existing = await db.DataClients.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
                    if (existing is null)
                    {
                        db.DataClients.Add(new DataClient
                        {
                            Name = "Agente DEV (Ola B)",
                            ClientId = clientId,
                            ClientSecretEncrypted = protector.Protect(secret),
                            IsActive = true,
                            TenantId = tenantId,
                        });
                    }
                    else
                    {
                        existing.ClientSecretEncrypted = protector.Protect(secret);
                        existing.IsActive = true;
                    }
                    await db.SaveChangesAsync(ct);
                }

                return Results.Json(new { clientId, secret, tenantId });
            }).AllowAnonymous().DisableAntiforgery();

            // POST /api/agente/dev/push/{clientId}: version anonima del push para el E2E de dev (el
            // push real es admin-gated). SOLO en Development.
            app.MapPost("/api/agente/dev/push/{clientId}", async (
                string clientId,
                string? q,
                IAgentRegistry registry,
                IHubContext<AgenteHub> hub,
                CancellationToken ct) =>
            {
                var presence = registry.Get(clientId);
                if (presence is null)
                {
                    return Results.Json(new { ok = false, error = "Agente offline." }, statusCode: 409);
                }

                var custom = !string.IsNullOrWhiteSpace(q);
                var query = custom
                    ? new QuerySpec(q!)
                    : new QuerySpec("SELECT id, name FROM items WHERE updated_at > @since",
                        new Dictionary<string, string?> { ["since"] = "2026-07-01T00:00:00Z" });

                var req = new FetchRequestMsg(
                    CorrelationId: Guid.NewGuid().ToString("N")[..8],
                    TenantId: presence.TenantId.ToString(),
                    Connector: new ConnectorSpec("Database", DbEngine: "SqlServer", Host: "lan", Database: "M700_GEN"),
                    Query: query,
                    Paging: new PagingSpec("Offset", 500, 100000));

                await hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.FetchRequest, req, ct);
                return Results.Json(new { ok = true, correlationId = req.CorrelationId });
            }).AllowAnonymous().DisableAntiforgery();

            // POST /api/agente/dev/ingest/{clientId}?mode=Replace&top=20 : E2E de INGESTA (doc 03 s6).
            // Crea/reusa un contenedor "Ciudades (agente)" con columnas y dispara una consulta cuyo
            // FetchResult ingiere el AgentImportService. SOLO Development.
            app.MapPost("/api/agente/dev/ingest/{clientId}", async (
                string clientId,
                string? mode,
                int? top,
                IApplicationDbContext db,
                IAgentRegistry registry,
                IAgentImportService imports,
                CancellationToken ct) =>
            {
                var presence = registry.Get(clientId);
                if (presence is null)
                {
                    return Results.Json(new { ok = false, error = "Agente offline." }, statusCode: 409);
                }
                var tenantId = presence.TenantId;
                var importMode = Enum.TryParse<ApiImportMode>(mode, ignoreCase: true, out var m) ? m : ApiImportMode.Replace;
                var take = top is > 0 and <= 5000 ? top.Value : 20;

                Guid containerId;
                Dictionary<Guid, string> mapping;
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var container = await db.DataContainers.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Name == "Ciudades (agente)", ct);
                    if (container is null)
                    {
                        container = new DataContainer { TenantId = tenantId, Name = "Ciudades (agente)" };
                        db.DataContainers.Add(container);
                        mapping = new Dictionary<Guid, string>();
                        var i = 0;
                        foreach (var field in new[] { "NOMBRE", "PAIS", "CODIGO_POSTAL" })
                        {
                            var col = new DataContainerColumn { TenantId = tenantId, ContainerId = container.Id, Name = field, Type = DataContainerColumnType.Text, SortOrder = i++ };
                            db.DataContainerColumns.Add(col);
                            mapping[col.Id] = field;
                        }
                        await db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        var cols = await db.DataContainerColumns.IgnoreQueryFilters()
                            .Where(c => c.ContainerId == container.Id).ToListAsync(ct);
                        mapping = cols.ToDictionary(c => c.Id, c => c.Name);
                    }
                    containerId = container.Id;
                }

                Guid? keyCol = importMode == ApiImportMode.Upsert
                    ? mapping.FirstOrDefault(kv => kv.Value == "CODIGO_POSTAL").Key
                    : null;
                var query = $"SELECT TOP {take} {string.Join(", ", mapping.Values)} FROM ciudades";

                var corr = await imports.DispatchFetchAsync(clientId, tenantId, containerId, mapping, importMode, keyCol, query, connector: null, ct);
                return Results.Json(new { ok = true, correlationId = corr, containerId, mode = importMode.ToString(), query });
            }).AllowAnonymous().DisableAntiforgery();

            // GET /api/agente/dev/ingest-status/{correlationId} : resultado de la ingesta.
            app.MapGet("/api/agente/dev/ingest-status/{correlationId}", (string correlationId, IAgentImportService imports) =>
            {
                return imports.TryGetOutcome(correlationId, out var outcome)
                    ? Results.Json(new { done = true, outcome })
                    : Results.Json(new { done = false });
            }).AllowAnonymous();

            // POST /api/agente/dev/run-process/{processId} : dispara una programacion REAL (conector +
            // consulta + credencial) via el mismo IProcessRunner del boton, y devuelve el correlationId
            // para poder cancelarla. Sirve para probar Cancel con una consulta lenta. SOLO Development.
            app.MapPost("/api/agente/dev/run-process/{processId:guid}", async (
                Guid processId, ITenantContext tenant, IProcessRunner runner,
                IApplicationDbContext db, CancellationToken ct) =>
            {
                // El runner necesita el tenant fijado (normalmente lo pone el request autenticado); en
                // este endpoint anonimo se toma el del propio proceso.
                var tenantId = await db.ImportProcesses.IgnoreQueryFilters()
                    .Where(p => p.Id == processId).Select(p => (Guid?)p.TenantId).FirstOrDefaultAsync(ct);
                if (tenantId is null) { return Results.NotFound(new { error = "proceso no existe" }); }
                using (AmbientTenantContext.Begin(tenantId.Value))
                {
                    var r = await runner.RunNowAsync(processId, ct: ct);
                    return Results.Json(new { r.Ok, r.CorrelationId, r.Message });
                }
            }).AllowAnonymous().DisableAntiforgery();

            // POST /api/agente/dev/cancel/{correlationId} : pide al agente ABORTAR el fetch en curso.
            // SOLO Development.
            app.MapPost("/api/agente/dev/cancel/{correlationId}", async (
                string correlationId, IAgentImportService imports, CancellationToken ct) =>
            {
                var cancelled = await imports.CancelAsync(correlationId, "prueba manual", ct);
                return Results.Json(new { cancelled });
            }).AllowAnonymous().DisableAntiforgery();

            // GET /api/agente/dev/container-count/{containerId} : filas ingeridas + celdas de la 1a fila.
            app.MapGet("/api/agente/dev/container-count/{containerId:guid}", async (
                Guid containerId, IApplicationDbContext db, CancellationToken ct) =>
            {
                var rowIds = await db.DataContainerRows.IgnoreQueryFilters()
                    .Where(r => r.ContainerId == containerId).Select(r => r.Id).ToListAsync(ct);
                var sample = rowIds.Count > 0
                    ? await db.DataContainerCells.IgnoreQueryFilters()
                        .Where(c => c.RowId == rowIds[0]).Select(c => c.Value).ToListAsync(ct)
                    : new List<string?>();
                return Results.Json(new { containerId, rowCount = rowIds.Count, firstRow = sample });
            }).AllowAnonymous();

            // POST /api/agente/dev/browse/{clientId}?url=... : ordena al sub-agente Navegador navegar,
            // leer el titulo (eval) y capturar. SOLO Development.
            app.MapPost("/api/agente/dev/browse/{clientId}", async (
                string clientId,
                string? url,
                bool? nosign,
                IAgentRegistry registry,
                IHubContext<AgenteHub> hub,
                IApplicationDbContext db,
                ISecretProtector protector,
                CancellationToken ct) =>
            {
                var presence = registry.Get(clientId);
                if (presence is null)
                {
                    return Results.Json(new { ok = false, error = "Agente offline." }, statusCode: 409);
                }

                // Firma del JS con el secreto del DataClient (doc 06 s4). nosign=true la omite (para
                // verificar que el agente rechaza JS sin firmar).
                string? secret = null;
                var client = await db.DataClients.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ClientId == clientId && c.IsActive, ct);
                if (client?.ClientSecretEncrypted is not null)
                {
                    try { secret = protector.Unprotect(client.ClientSecretEncrypted); } catch { /* ilegible */ }
                }

                var target = string.IsNullOrWhiteSpace(url) ? "https://example.com" : url;
                var corr = Guid.NewGuid().ToString("N")[..8];
                const string evalScript = "document.title";
                var evalSig = (nosign == true || secret is null) ? null : AgentSign.SignJs(secret, corr, evalScript);

                var req = new BrowserRequestMsg(corr, presence.TenantId.ToString(), new List<BrowserAction>
                {
                    new(BrowserActionKind.Navigate, Url: target),
                    new(BrowserActionKind.Eval, Script: evalScript, Signature: evalSig),
                    new(BrowserActionKind.Screenshot),
                });
                await hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.BrowserRequest, req, ct);
                return Results.Json(new { ok = true, correlationId = corr, url = target, signed = evalSig is not null });
            }).AllowAnonymous().DisableAntiforgery();

            // POST /api/agente/dev/files/{clientId}?op=list|read|write&path=...&content=... : ordena una
            // accion tipada al sub-agente Archivos. SOLO Development.
            app.MapPost("/api/agente/dev/files/{clientId}", async (
                string clientId,
                string? op,
                string? path,
                string? content,
                IAgentRegistry registry,
                IHubContext<AgenteHub> hub,
                CancellationToken ct) =>
            {
                var presence = registry.Get(clientId);
                if (presence is null)
                {
                    return Results.Json(new { ok = false, error = "Agente offline." }, statusCode: 409);
                }
                var kind = (op ?? "list").ToLowerInvariant() switch
                {
                    "read" => FileActionKind.Read,
                    "write" => FileActionKind.Write,
                    "delete" => FileActionKind.Delete,
                    "exists" => FileActionKind.Exists,
                    "mkdir" => FileActionKind.MakeDir,
                    _ => FileActionKind.List,
                };
                var corr = Guid.NewGuid().ToString("N")[..8];
                var req = new FileRequestMsg(corr, presence.TenantId.ToString(),
                    new List<FileAction> { new(kind, Path: path, Content: content) });
                await hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.FileRequest, req, ct);
                return Results.Json(new { ok = true, correlationId = corr, op = kind.ToString(), path });
            }).AllowAnonymous().DisableAntiforgery();
        }

        return app;
    }
}
