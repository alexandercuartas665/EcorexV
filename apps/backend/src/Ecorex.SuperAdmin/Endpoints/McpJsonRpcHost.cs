using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>Identidad publica de un servidor MCP (lo que ve el cliente en initialize).</summary>
public sealed record McpServerInfo(string Name, string Title, string Instructions);

/// <summary>Resultado de ligar una peticion a su toolset: el toolset + su identidad, o una respuesta de error.</summary>
public sealed record McpBinding(IMcpToolset? Toolset, McpServerInfo? Info, IResult? Error)
{
    public static McpBinding Ok(IMcpToolset toolset, McpServerInfo info) => new(toolset, info, null);

    public static McpBinding Fail(IResult error) => new(null, null, error);
}

/// <summary>
/// Host JSON-RPC 2.0 COMUN de los servidores MCP de ECOREX (tableros ADR-0159, WhatsApp por linea ADR-0164).
/// - Transporte "Streamable HTTP" (MCP 2025-06-18): POST con UN mensaje, respuesta application/json directa (sin SSE).
///   Las notificaciones responden 202. Sin sesiones.
/// - AUTH: Authorization: Bearer &lt;token personal&gt; del ALCANCE del servidor; sin token / revocado / de otro alcance /
///   usuario inactivo -> 401 con WWW-Authenticate.
/// - AISLAMIENTO: la resolucion por hash es la unica lectura cross-tenant; luego AmbientTenantContext.Begin + scope
///   DI propio, y el filtro global aisla todo lo demas.
/// - AUDITORIA: toda herramienta de ESCRITURA exitosa queda en super_admin_audit_logs (actor = el usuario).
/// - MEDIA: si la herramienta devuelve un archivo, va como bloque image/audio (la IA lo ve/oye) o recurso embebido.
/// </summary>
public static class McpJsonRpcHost
{
    private const string LatestProtocol = "2025-06-18";
    private static readonly string[] SupportedProtocols = { "2025-06-18", "2025-03-26", "2024-11-05" };
    private const int MaxBodyBytes = 6 * 1024 * 1024; // admite base64 de archivos pequenos (enviar_media)

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <param name="scope">Alcance del token exigido (ej. mcp:tableros, mcp:whatsapp).</param>
    /// <param name="realm">Realm del WWW-Authenticate.</param>
    /// <param name="auditPrefix">Prefijo de la accion de auditoria (ej. mcp.tableros -> mcp.tableros.crear_tarea).</param>
    /// <param name="auditReason">Reason de la auditoria.</param>
    /// <param name="bind">Liga la peticion (ya en el tenant y como el usuario) a su toolset e identidad.</param>
    public static async Task<IResult> HandleAsync(HttpContext http, IServiceScopeFactory scopes, string scope, string realm,
        string auditPrefix, string auditReason, Func<IServiceProvider, McpActor, CancellationToken, Task<McpBinding>> bind, CancellationToken ct)
    {
        // 1) Auth por token personal (antes de leer el cuerpo).
        var bearer = ExtractBearer(http.Request);
        if (bearer is null) { return Unauthorized(http, realm, "falta Authorization: Bearer <token>"); }

        McpActor? actor;
        await using (var authScope = scopes.CreateAsyncScope())
        {
            var tokens = authScope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
            actor = await tokens.ResolveAsync(bearer, scope, ct);
        }
        if (actor is null) { return Unauthorized(http, realm, "token invalido, revocado, de otro servidor o de usuario inactivo"); }

        // 2) Cuerpo JSON-RPC (un solo mensaje).
        string body;
        if (http.Request.ContentLength is > MaxBodyBytes) { return Rpc(Error(null, -32600, "Mensaje demasiado grande.")); }
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8)) { body = await reader.ReadToEndAsync(ct); }
        if (body.Length > MaxBodyBytes) { return Rpc(Error(null, -32600, "Mensaje demasiado grande.")); }

        JsonNode? root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException) { return Rpc(Error(null, -32700, "JSON invalido (parse error).")); }
        if (root is not JsonObject msg) { return Rpc(Error(null, -32600, "Se espera UN mensaje JSON-RPC (objeto); no se admiten lotes.")); }

        var method = msg["method"]?.GetValue<string>();
        var id = msg["id"];
        if (id is null || method is null || method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        // 3) Ejecucion en el tenant y como el usuario del token.
        using var ambient = AmbientTenantContext.Begin(actor.TenantId, actor.PlatformUserId);
        await using var reqScope = scopes.CreateAsyncScope();
        var sp = reqScope.ServiceProvider;
        var binding = await bind(sp, actor, ct);
        if (binding.Error is not null) { return binding.Error; }
        var toolset = binding.Toolset!;
        var info = binding.Info!;

        switch (method)
        {
            case "initialize":
                {
                    var requested = msg["params"]?["protocolVersion"]?.GetValue<string>();
                    var version = requested is not null && SupportedProtocols.Contains(requested) ? requested : LatestProtocol;
                    var result = new JsonObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = info.Name,
                            ["title"] = info.Title,
                            ["version"] = AppVersion.Current
                        },
                        ["instructions"] = info.Instructions
                    };
                    return Rpc(Ok(id, result));
                }

            case "ping":
                return Rpc(Ok(id, new JsonObject()));

            case "tools/list":
                {
                    var tools = new JsonArray();
                    foreach (var s in toolset.GetSpecs())
                    {
                        tools.Add(new JsonObject
                        {
                            ["name"] = s.Name,
                            ["description"] = s.Description,
                            ["inputSchema"] = JsonNode.Parse(s.ParametersJsonSchema),
                            ["annotations"] = new JsonObject
                            {
                                ["readOnlyHint"] = toolset.ReadOnlyTools.Contains(s.Name),
                                ["destructiveHint"] = toolset.DestructiveTools.Contains(s.Name),
                                ["openWorldHint"] = false
                            }
                        });
                    }
                    return Rpc(Ok(id, new JsonObject { ["tools"] = tools }));
                }

            case "tools/call":
                {
                    var name = msg["params"]?["name"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(name)) { return Rpc(Error(id, -32602, "params.name es obligatorio.")); }
                    if (!toolset.GetSpecs().Any(s => s.Name == name)) { return Rpc(Error(id, -32602, $"Herramienta desconocida: {name}")); }
                    var argsNode = msg["params"]?["arguments"];
                    var argsJson = argsNode is JsonObject ? argsNode.ToJsonString() : "{}";

                    var tr = await toolset.ExecuteAsync(name, argsJson, actor, ct);

                    if (!tr.IsError && !toolset.ReadOnlyTools.Contains(name))
                    {
                        var audit = sp.GetRequiredService<IAuditWriter>();
                        var db = sp.GetRequiredService<IApplicationDbContext>();
                        audit.Write(actor.PlatformUserId, $"{auditPrefix}.{name}", "McpTool", null,
                            previousValue: tr.AuditBefore,
                            newValue: new { tool = name, token = actor.TokenId, args = Truncate(argsJson, 2000), despues = tr.AuditAfter },
                            tenantId: actor.TenantId, reason: auditReason);
                        await db.SaveChangesAsync(ct);
                    }

                    var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = tr.Json });
                    if (tr.Media is { } media) { content.Add(MediaBlock(media)); }
                    return Rpc(Ok(id, new JsonObject { ["content"] = content, ["isError"] = tr.IsError }));
                }

            default:
                return Rpc(Error(id, -32601, $"Metodo no soportado: {method}"));
        }
    }

    /// <summary>Bloque de contenido MCP para un archivo: image/* y audio/* nativos; lo demas como recurso embebido.</summary>
    private static JsonObject MediaBlock(McpMedia media)
    {
        if (media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject { ["type"] = "image", ["data"] = media.Base64, ["mimeType"] = media.MimeType };
        }
        if (media.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject { ["type"] = "audio", ["data"] = media.Base64, ["mimeType"] = media.MimeType };
        }
        return new JsonObject
        {
            ["type"] = "resource",
            ["resource"] = new JsonObject
            {
                ["uri"] = "ecorex://media/" + Uri.EscapeDataString(media.FileName ?? "archivo"),
                ["mimeType"] = media.MimeType,
                ["blob"] = media.Base64
            }
        };
    }

    public static IResult Unauthorized(HttpContext http, string realm, string message)
    {
        http.Response.Headers.WWWAuthenticate = $"Bearer realm=\"{realm}\"";
        return Results.Json(new { error = message }, Json, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static string? ExtractBearer(HttpRequest req)
    {
        var header = req.Headers.Authorization.ToString();
        const string p = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { return null; }
        var t = header[p.Length..].Trim();
        return t.Length == 0 ? null : t;
    }

    private static JsonObject Ok(JsonNode id, JsonNode result)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static IResult Rpc(JsonObject payload)
        => Results.Content(payload.ToJsonString(Json), "application/json; charset=utf-8");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
