using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
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
        }

        return app;
    }
}
