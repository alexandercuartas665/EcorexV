using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.Domain.Entities;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Servidor MCP de TABLEROS (ADR-0159) para IAs externas: Claude (Code/Desktop), Gemini CLI, ChatGPT, etc.
///
/// - Transporte "Streamable HTTP" (MCP 2025-06-18): POST /api/mcp/tableros con UN mensaje JSON-RPC 2.0,
///   respuesta application/json directa (sin SSE). GET responde 405 (no hay stream servidor->cliente);
///   las notificaciones responden 202. Sin sesiones (servidor sin estado).
/// - AUTH: Authorization: Bearer &lt;token personal&gt; (TenantApiToken con Scope "mcp:tableros" y TenantUserId).
///   El token resuelve tenant + USUARIO; toda herramienta corre COMO ese usuario (visibilidad, bitacora,
///   auditoria). Sin token / revocado / usuario inactivo -> 401 con WWW-Authenticate.
/// - AISLAMIENTO: la resolucion por hash es la unica lectura cross-tenant; luego AmbientTenantContext.Begin
///   + scope DI propio, y el filtro global aisla todo lo demas.
/// - AUDITORIA: toda herramienta de ESCRITURA exitosa escribe en super_admin_audit_logs (actor = el usuario,
///   reason "mcp-tableros"), ademas de la bitacora de la tarea ("Nombre (via IA)").
/// </summary>
public static class McpTablerosEndpoints
{
    public const string Route = "/api/mcp/tableros";

    private const string LatestProtocol = "2025-06-18";
    private static readonly string[] SupportedProtocols = { "2025-06-18", "2025-03-26", "2024-11-05" };
    private const string AuditReason = "mcp-tableros";
    private const int MaxBodyBytes = 256 * 1024;

    private const string ServerInstructions =
        "Servidor de tableros de tareas de ECOREX. Actuas como el usuario dueno del token: solo ves y cambias lo " +
        "que el puede. Empieza con listar_tableros o mis_pasos_pendientes. Las tareas se identifican por su numero " +
        "(ej. T00123). Si una tarea tiene flujo en curso no la muevas de columna: usa ver_tarea y completar_paso. " +
        "Confirma con el usuario antes de cerrar pasos o mover tareas a columnas de cierre. " +
        "Directorio de contactos: busca antes de crear (buscar_contacto) para no duplicar; ver_contacto muestra la " +
        "ficha completa; editar_contacto cambia solo lo que envies (consulta listar_campos_contacto antes de usar " +
        "'campos'). Confirma con el usuario antes de inactivar un contacto. " +
        "Autoria de flujos: crea con crear_flujo, agrega nodos (agregar_nodo) y conectalos (conectar_nodos) entre el " +
        "inicio y el fin, ponles encargado/formulario/condicion, publica (publicar_flujo) y engancha a un concepto " +
        "(enganchar_flujo_a_concepto) para que las tareas lo arranquen. Autoria de formularios: usa apply_form_spec " +
        "para construir uno completo, o create_form + add_container + add_question, y activate para dejarlo activo " +
        "(consulta list_forms/get_form antes de cambiar uno). La IA solo puede lo que el usuario puede (permisos de " +
        "flujos/formularios).";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static WebApplication MapMcpTablerosEndpoints(this WebApplication app)
    {
        app.MapPost(Route, HandlePostAsync).DisableAntiforgery().AllowAnonymous().WithTags("mcp-tableros");

        // Sin stream servidor->cliente: la spec permite responder 405 al GET.
        app.MapGet(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        app.MapDelete(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> HandlePostAsync(HttpContext http, IServiceScopeFactory scopes, CancellationToken ct)
    {
        // 1) Auth por token personal (antes de leer el cuerpo).
        var bearer = ExtractBearer(http.Request);
        if (bearer is null) { return Unauthorized(http, "falta Authorization: Bearer <token>"); }

        McpActor? actor;
        await using (var authScope = scopes.CreateAsyncScope())
        {
            var tokens = authScope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();
            actor = await tokens.ResolveAsync(bearer, TenantApiToken.ScopeMcpTableros, ct);
        }
        if (actor is null) { return Unauthorized(http, "token invalido, revocado o de usuario inactivo"); }

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
        // Respuestas del cliente o notificaciones: aceptadas, sin cuerpo.
        if (id is null || method is null || method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        // 3) Ejecucion en el tenant y como el usuario del token.
        using var ambient = AmbientTenantContext.Begin(actor.TenantId, actor.PlatformUserId);
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var toolset = sp.GetRequiredService<ITableroMcpToolset>();

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
                            ["name"] = "ecorex-tableros",
                            ["title"] = "ECOREX Tableros",
                            ["version"] = AppVersion.Current
                        },
                        ["instructions"] = ServerInstructions
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
                        var readOnly = toolset.ReadOnlyTools.Contains(s.Name);
                        tools.Add(new JsonObject
                        {
                            ["name"] = s.Name,
                            ["description"] = s.Description,
                            ["inputSchema"] = JsonNode.Parse(s.ParametersJsonSchema),
                            ["annotations"] = new JsonObject
                            {
                                ["readOnlyHint"] = readOnly,
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
                        audit.Write(actor.PlatformUserId, $"mcp.tableros.{name}", "McpTool", null,
                            previousValue: tr.AuditBefore,
                            newValue: new { tool = name, token = actor.TokenId, args = Truncate(argsJson, 2000), despues = tr.AuditAfter },
                            tenantId: actor.TenantId, reason: AuditReason);
                        await db.SaveChangesAsync(ct);
                    }

                    var result = new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = tr.Json }),
                        ["isError"] = tr.IsError
                    };
                    return Rpc(Ok(id, result));
                }

            default:
                return Rpc(Error(id, -32601, $"Metodo no soportado: {method}"));
        }
    }

    private static IResult Unauthorized(HttpContext http, string message)
    {
        http.Response.Headers.WWWAuthenticate = "Bearer realm=\"ecorex-mcp-tableros\"";
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
