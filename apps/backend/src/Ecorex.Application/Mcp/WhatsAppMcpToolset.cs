using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>Linea WhatsApp a la que se liga el MCP (la de la URL /api/mcp/whatsapp/{lineId}).</summary>
public sealed record WhatsAppLineSummary(Guid Id, string Name, string? PhoneNumber, WhatsAppLineStatus Status, WhatsAppProvider Provider);

/// <summary>
/// Servidor MCP de WhatsApp POR LINEA Evolution API (ADR-0164). Una IA externa (Claude, Codex, Gemini) opera UNA
/// linea con todo lo util de Evolution v2: leer chats/mensajes/contactos, enviar cualquier tipo de mensaje, gestionar
/// chats, etiquetas, grupos y el perfil de la linea. Corre COMO el usuario del token personal (scope mcp:whatsapp):
/// permisos de "conversaciones"/"lineas", auditoria y bitacora "(via IA)".
/// </summary>
public interface IWhatsAppMcpToolset : IMcpToolset
{
    /// <summary>Liga el toolset a la linea de la URL. Se llama una vez por peticion, antes de ejecutar.</summary>
    IWhatsAppMcpToolset ForLine(Guid lineId);

    /// <summary>Linea del tenant ACTUAL (filtro global) o null si no existe.</summary>
    Task<WhatsAppLineSummary?> DescribeLineAsync(Guid lineId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementacion. Capa DELGADA: las llamadas a Evolution pasan por IWhatsAppConnectorService.EvolutionCallAsync
/// (resuelve servidor + llave + instancia de la linea, nunca expone credenciales) y los ENVIOS a un chat 1:1 pasan
/// por IChatService para que el mensaje quede en Conversaciones a nombre del usuario: asi el agente SARA cede el
/// turno (regla "el ultimo saliente es de una persona") y el historial no se parte. Lo unico que agrega son
/// SALVAGUARDAS contra bloqueos de WhatsApp: un destinatario por llamada, confirmacion antes de escribirle a un
/// numero que nunca escribio a la linea y un limite de envios por linea.
/// </summary>
public sealed partial class WhatsAppMcpToolset : IWhatsAppMcpToolset
{
    private const string Conv = "conversaciones";
    private const string Lin = "lineas";
    private const int MaxJsonChars = 30000;
    private const int MaxListItems = 60;

    /// <summary>Limite de envios por linea desde el MCP (anti-bloqueo de WhatsApp): por minuto y por hora.</summary>
    public const int SendsPerMinute = 20;
    public const int SendsPerHour = 300;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatService _chat;
    private readonly IBlockedNumberService _blocked;
    private readonly TimeProvider _clock;
    private readonly IRolService? _roles;
    private readonly IDirectorioToolset? _directorio;
    private Guid _lineId;

    public WhatsAppMcpToolset(IApplicationDbContext db, IWhatsAppConnectorService connector, IChatService chat,
        IBlockedNumberService blocked, TimeProvider clock, IRolService? roles = null, IDirectorioToolset? directorio = null)
    {
        _db = db;
        _connector = connector;
        _chat = chat;
        _blocked = blocked;
        _clock = clock;
        _roles = roles;
        _directorio = directorio;
    }

    public IWhatsAppMcpToolset ForLine(Guid lineId)
    {
        _lineId = lineId;
        return this;
    }

    public async Task<WhatsAppLineSummary?> DescribeLineAsync(Guid lineId, CancellationToken cancellationToken = default)
        => await _db.WhatsAppLines.AsNoTracking().Where(l => l.Id == lineId)
            .Select(l => new WhatsAppLineSummary(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.Provider))
            .FirstOrDefaultAsync(cancellationToken);

    // ===================== Catalogo =====================

    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "estado_linea", "listar_chats", "leer_mensajes", "buscar_contactos", "verificar_numeros",
        "ver_perfil_contacto", "descargar_media", "listar_grupos", "ver_grupo", "listar_etiquetas", "catalogo_business"
    };

    private static readonly HashSet<string> Destructive = new(StringComparer.Ordinal) { "borrar_mensaje", "salir_grupo" };

    public IReadOnlySet<string> ReadOnlyTools => ReadOnly;

    public IReadOnlySet<string> DestructiveTools => Destructive;

    private static AiToolSpec[]? _allSpecs;

    public IReadOnlyList<AiToolSpec> GetSpecs()
        => _allSpecs ??= ReadSpecs.Concat(SendSpecs).Concat(ManageSpecs).Concat(GroupSpecs).ToArray();

    /// <summary>Permiso (modulo, accion) por herramienta. perfil_linea/privacidad_linea dependen de la accion.</summary>
    private static readonly Dictionary<string, (string Module, PermissionAction Action)> Gate = new(StringComparer.Ordinal)
    {
        ["estado_linea"] = (Conv, PermissionAction.View),
        ["listar_chats"] = (Conv, PermissionAction.View),
        ["leer_mensajes"] = (Conv, PermissionAction.View),
        ["buscar_contactos"] = (Conv, PermissionAction.View),
        ["verificar_numeros"] = (Conv, PermissionAction.View),
        ["ver_perfil_contacto"] = (Conv, PermissionAction.View),
        ["descargar_media"] = (Conv, PermissionAction.View),
        ["listar_grupos"] = (Conv, PermissionAction.View),
        ["ver_grupo"] = (Conv, PermissionAction.View),
        ["listar_etiquetas"] = (Conv, PermissionAction.View),
        ["catalogo_business"] = (Conv, PermissionAction.View),
        ["enviar_texto"] = (Conv, PermissionAction.Create),
        ["enviar_media"] = (Conv, PermissionAction.Create),
        ["enviar_ubicacion"] = (Conv, PermissionAction.Create),
        ["enviar_contacto"] = (Conv, PermissionAction.Create),
        ["enviar_encuesta"] = (Conv, PermissionAction.Create),
        ["enviar_lista"] = (Conv, PermissionAction.Create),
        ["publicar_estado"] = (Conv, PermissionAction.Create),
        ["reaccionar"] = (Conv, PermissionAction.Create),
        ["escribiendo"] = (Conv, PermissionAction.Create),
        ["editar_mensaje"] = (Conv, PermissionAction.Edit),
        ["marcar_leido"] = (Conv, PermissionAction.Edit),
        ["gestionar_chat"] = (Conv, PermissionAction.Edit),
        ["etiquetar_chat"] = (Conv, PermissionAction.Edit),
        ["pausar_agente"] = (Conv, PermissionAction.Edit),
        ["reanudar_agente"] = (Conv, PermissionAction.Edit),
        ["guardar_contacto"] = (Conv, PermissionAction.View),
        ["crear_grupo"] = (Conv, PermissionAction.Edit),
        ["editar_grupo"] = (Conv, PermissionAction.Edit),
        ["participantes_grupo"] = (Conv, PermissionAction.Edit),
        ["invitacion_grupo"] = (Conv, PermissionAction.Edit),
        ["borrar_mensaje"] = (Conv, PermissionAction.Delete),
        ["salir_grupo"] = (Conv, PermissionAction.Delete),
        ["perfil_linea"] = (Lin, PermissionAction.Edit),
        ["privacidad_linea"] = (Lin, PermissionAction.Edit),
        ["reiniciar_linea"] = (Lin, PermissionAction.Edit)
    };

    // ===================== Ejecucion =====================

    public async Task<McpToolResult> ExecuteAsync(string toolName, string argumentsJson, McpActor actor, CancellationToken cancellationToken = default)
    {
        if (_lineId == Guid.Empty) { return Err("El servidor no esta ligado a una linea."); }
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException) { return Err("Los argumentos no son un JSON valido."); }
        if (args.ValueKind != JsonValueKind.Object) { return Err("Los argumentos deben ser un objeto JSON."); }
        if (!Gate.TryGetValue(toolName, out var gate)) { return Err($"Herramienta desconocida: {toolName}"); }

        var line = await DescribeLineAsync(_lineId, cancellationToken);
        if (line is null) { return Err("La linea no existe en tu empresa."); }
        if (line.Provider != WhatsAppProvider.Evolution) { return Err("Este MCP solo opera lineas de Evolution API."); }

        // Permisos del usuario (misma matriz que las pantallas; Owner/Admin y sin rol = sin restriccion).
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, cancellationToken);
        var action = gate.Action;
        if (toolName is "perfil_linea" or "privacidad_linea" && (Str(args, "accion") ?? "ver") == "ver")
        {
            action = PermissionAction.View;
        }
        if (!perms.Can(gate.Module, action))
        {
            return Err($"Tu usuario no tiene permiso ({ActionEs(action)} en {(gate.Module == Conv ? "Conversaciones" : "Lineas")}) para usar '{toolName}'.");
        }

        try
        {
            return toolName switch
            {
                "estado_linea" => await LineStatusAsync(line, cancellationToken),
                "listar_chats" => await ListChatsAsync(args, cancellationToken),
                "leer_mensajes" => await ReadMessagesAsync(args, cancellationToken),
                "buscar_contactos" => await SearchContactsAsync(args, cancellationToken),
                "verificar_numeros" => await CheckNumbersAsync(args, cancellationToken),
                "ver_perfil_contacto" => await ContactProfileAsync(args, cancellationToken),
                "descargar_media" => await DownloadMediaAsync(args, cancellationToken),
                "listar_grupos" => await ListGroupsAsync(args, cancellationToken),
                "ver_grupo" => await ViewGroupAsync(args, cancellationToken),
                "listar_etiquetas" => await ListLabelsAsync(cancellationToken),
                "catalogo_business" => await CatalogAsync(args, cancellationToken),
                _ => await ExecuteWriteAsync(toolName, args, actor, perms, line, cancellationToken)
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<McpToolResult> ExecuteWriteAsync(string tool, JsonElement args, McpActor actor, EffectivePermissions perms,
        WhatsAppLineSummary line, CancellationToken ct)
        => tool switch
        {
            "enviar_texto" => await SendTextAsync(args, actor, ct),
            "enviar_media" => await SendMediaAsync(args, actor, ct),
            "enviar_ubicacion" => await SendLocationAsync(args, actor, ct),
            "enviar_contacto" => await SendContactCardAsync(args, actor, ct),
            "enviar_encuesta" => await SendPollAsync(args, actor, ct),
            "enviar_lista" => await SendListAsync(args, actor, ct),
            "publicar_estado" => await PublishStatusAsync(args, ct),
            "reaccionar" => await ReactAsync(args, ct),
            "escribiendo" => await PresenceAsync(args, ct),
            "editar_mensaje" => await EditMessageAsync(args, ct),
            "borrar_mensaje" => await DeleteMessageAsync(args, actor, ct),
            "marcar_leido" => await MarkReadAsync(args, ct),
            "gestionar_chat" => await ManageChatAsync(args, ct),
            "etiquetar_chat" => await LabelChatAsync(args, ct),
            "pausar_agente" => await PauseAgentAsync(args, actor, ct),
            "reanudar_agente" => await ResumeAgentAsync(args, actor, ct),
            "guardar_contacto" => await SaveContactAsync(args, actor, perms, ct),
            "crear_grupo" => await CreateGroupAsync(args, ct),
            "editar_grupo" => await EditGroupAsync(args, ct),
            "participantes_grupo" => await GroupParticipantsAsync(args, ct),
            "invitacion_grupo" => await GroupInviteAsync(args, ct),
            "salir_grupo" => await LeaveGroupAsync(args, ct),
            "perfil_linea" => await LineProfileAsync(args, line, ct),
            "privacidad_linea" => await LinePrivacyAsync(args, ct),
            "reiniciar_linea" => await RestartLineAsync(ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };

    // ===================== Destinatario =====================

    /// <summary>Destino resuelto: grupo (jid @g.us) o persona (numero/jid), con su conversacion si existe en la linea.</summary>
    private sealed record Target(bool IsGroup, string Number, string RemoteJid, Guid? ConversationId, string? Phone, bool HasInbound);

    /// <summary>
    /// 'chat' / 'para' acepta: id de conversacion (de listar_chats), numero (con o sin indicativo), jid de persona
    /// (@s.whatsapp.net / @lid) o jid de grupo (@g.us). NO crea conversacion (eso ocurre solo al enviar).
    /// </summary>
    private async Task<(Target? Target, string? Error)> ResolveTargetAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) { return (null, "Indica el chat: numero, jid o id de conversacion (ver listar_chats)."); }
        key = key.Trim();
        if (key.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)) { return (new Target(true, key, key, null, null, true), null); }

        if (Guid.TryParse(key, out var convId))
        {
            var conv = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convId && c.WhatsAppLineId == _lineId)
                .Select(c => new { c.Id, c.ContactPhone, c.RemoteJid }).FirstOrDefaultAsync(ct);
            if (conv is null) { return (null, "Esa conversacion no existe en esta linea."); }
            var jid = conv.RemoteJid ?? (conv.ContactPhone + "@s.whatsapp.net");
            return (new Target(false, conv.RemoteJid ?? conv.ContactPhone, jid, conv.Id, conv.ContactPhone, await HasInboundAsync(conv.Id, ct)), null);
        }

        if (key.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
        {
            var conv = await _db.Conversations.AsNoTracking()
                .Where(c => c.WhatsAppLineId == _lineId && c.RemoteJid == key)
                .Select(c => new { c.Id, c.ContactPhone }).FirstOrDefaultAsync(ct);
            return (new Target(false, key, key, conv?.Id, conv?.ContactPhone, conv is not null && await HasInboundAsync(conv.Id, ct)), null);
        }

        var raw = key.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase) ? key.Split('@')[0] : key;
        var msisdn = PhoneNumber.ToMsisdn(raw);
        if (msisdn.Length < 7) { return (null, $"'{key}' no es un numero de WhatsApp valido."); }
        var forms = PhoneNumber.LookupForms(raw);
        var existing = await _db.Conversations.AsNoTracking()
            .Where(c => c.WhatsAppLineId == _lineId && forms.Contains(c.ContactPhone))
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new { c.Id, c.RemoteJid }).FirstOrDefaultAsync(ct);
        var number = existing?.RemoteJid ?? msisdn;
        return (new Target(false, number, existing?.RemoteJid ?? msisdn + "@s.whatsapp.net", existing?.Id, msisdn,
            existing is not null && await HasInboundAsync(existing.Id, ct)), null);
    }

    private Task<bool> HasInboundAsync(Guid conversationId, CancellationToken ct)
        => _db.Messages.AsNoTracking().AnyAsync(m => m.ConversationId == conversationId && m.Direction == MessageDirection.Inbound, ct);

    /// <summary>Conversacion del destino (la existente o una nueva ligada a la linea). Null para grupos y @lid sin hilo.</summary>
    private async Task<Guid?> EnsureConversationAsync(Target t, CancellationToken ct)
    {
        if (t.IsGroup) { return null; }
        if (t.ConversationId is Guid id) { return id; }
        if (t.Phone is null) { return null; }
        return (await _chat.GetOrCreateForLineAsync(_lineId, t.Phone, null, ct))?.Id;
    }

    /// <summary>
    /// Salvaguardas de envio: (1) a una PERSONA que nunca escribio a esta linea solo con primer_contacto_confirmado=true
    /// (el mensaje en frio es lo que mas bloquea numeros en WhatsApp); (2) limite de envios por linea.
    /// </summary>
    private string? SendGuard(Target t, JsonElement args)
    {
        if (!t.IsGroup && !t.HasInbound && Bool(args, "primer_contacto_confirmado") != true)
        {
            return "Ese numero nunca le ha escrito a esta linea. Escribirle en frio puede hacer que WhatsApp bloquee el " +
                   "numero de la linea: confirma con el usuario y repite con primer_contacto_confirmado=true.";
        }
        return SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())
            ? null
            : $"Limite de envios de la linea alcanzado ({SendsPerMinute}/min, {SendsPerHour}/hora). Espera un momento.";
    }

    /// <summary>Ventana deslizante de envios por linea (en memoria del proceso).</summary>
    private static class SendLimiter
    {
        private static readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Windows = new();

        public static bool TryAcquire(Guid lineId, DateTimeOffset now)
        {
            var q = Windows.GetOrAdd(lineId, _ => new Queue<DateTimeOffset>());
            lock (q)
            {
                while (q.Count > 0 && now - q.Peek() > TimeSpan.FromHours(1)) { q.Dequeue(); }
                if (q.Count >= SendsPerHour) { return false; }
                if (q.Count(t => now - t <= TimeSpan.FromMinutes(1)) >= SendsPerMinute) { return false; }
                q.Enqueue(now);
                return true;
            }
        }

        /// <summary>Solo para pruebas: vacia la ventana de una linea.</summary>
        public static void Reset(Guid lineId) => Windows.TryRemove(lineId, out _);
    }

    /// <summary>Solo para pruebas: reinicia el limite de envios de la linea.</summary>
    internal static void ResetSendLimit(Guid lineId) => SendLimiter.Reset(lineId);

    private static string Label(McpActor actor) => $"{actor.DisplayName} (via IA)";

    // ===================== Evolution =====================

    private async Task<(JsonNode? Node, EvolutionRawResult Raw)> EvoAsync(string method, string path, object? body, CancellationToken ct, bool requireConnected = true)
    {
        var r = await _connector.EvolutionCallAsync(_lineId, method, path, body, requireConnected, ct);
        JsonNode? node = null;
        if (!string.IsNullOrWhiteSpace(r.Body))
        {
            try { node = JsonNode.Parse(r.Body); } catch (JsonException) { node = JsonValue.Create(Clip(r.Body, 2000)); }
        }
        return (node, r);
    }

    private static McpToolResult EvoErr(EvolutionRawResult r, string what) => Err($"{what}: {r.Error ?? "Evolution no respondio"}");

    /// <summary>Respuesta de Evolution para la IA: listas recortadas y tope de tamano (no saturar el contexto).</summary>
    private static JsonNode? Shrink(JsonNode? node)
    {
        if (node is JsonArray arr && arr.Count > MaxListItems)
        {
            var cut = new JsonArray();
            foreach (var item in arr.Take(MaxListItems)) { cut.Add(item?.DeepClone()); }
            node = cut;
        }
        var text = node?.ToJsonString() ?? "null";
        return text.Length <= MaxJsonChars ? node : JsonValue.Create(text[..MaxJsonChars] + "... (recortado)");
    }

    // ===================== Lectura =====================

    private static readonly AiToolSpec[] ReadSpecs =
    {
        new("estado_linea",
            "Estado de ESTA linea: nombre, numero, conexion real con WhatsApp, si el agente SARA la atiende y cuantos chats tiene en el sistema. Usala primero.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("listar_chats",
            "Chats de la linea, el mas reciente primero. fuente='sistema' (por defecto: conversaciones guardadas en ECOREX, solo chats 1:1) o 'whatsapp' (lo que tiene WhatsApp, incluye grupos). Devuelve el id de conversacion o jid para usar en 'chat'.",
            """{"type":"object","properties":{"fuente":{"type":"string","enum":["sistema","whatsapp"]},"texto":{"type":"string","description":"Filtra por nombre o numero"},"incluir_archivados":{"type":"boolean"},"limite":{"type":"integer","minimum":1,"maximum":60}},"additionalProperties":false}"""),
        new("leer_mensajes",
            "Ultimos mensajes de un chat (mas reciente al final), con su mensaje_id (para reaccionar, citar, editar, borrar o descargar media). 'chat' = id de conversacion, numero o jid. fuente='sistema' (por defecto) o 'whatsapp' (historial que guarda WhatsApp/Evolution: grupos o mensajes que el sistema no tiene).",
            """{"type":"object","properties":{"chat":{"type":"string"},"fuente":{"type":"string","enum":["sistema","whatsapp"]},"limite":{"type":"integer","minimum":1,"maximum":100}},"required":["chat"],"additionalProperties":false}"""),
        new("buscar_contactos",
            "Contactos que conoce la linea en WhatsApp (nombre visible y jid). 'texto' filtra por nombre o numero.",
            """{"type":"object","properties":{"texto":{"type":"string"},"limite":{"type":"integer","minimum":1,"maximum":60}},"additionalProperties":false}"""),
        new("verificar_numeros",
            "Verifica que numeros tienen WhatsApp (hasta 20) y devuelve su jid. Util antes de escribir a un numero nuevo.",
            """{"type":"object","properties":{"numeros":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":20}},"required":["numeros"],"additionalProperties":false}"""),
        new("ver_perfil_contacto",
            "Perfil publico de un contacto: nombre, estado/descripcion, foto y si es cuenta business.",
            """{"type":"object","properties":{"chat":{"type":"string","description":"Numero, jid o id de conversacion"}},"required":["chat"],"additionalProperties":false}"""),
        new("descargar_media",
            "Descarga la imagen, audio, video o documento de un mensaje (por mensaje_id, ver leer_mensajes) para que la IA lo vea/oiga. Tope 4 MB.",
            """{"type":"object","properties":{"mensaje_id":{"type":"string"}},"required":["mensaje_id"],"additionalProperties":false}"""),
        new("listar_grupos",
            "Grupos de WhatsApp en los que esta la linea (jid, nombre, tamano).",
            """{"type":"object","properties":{"con_participantes":{"type":"boolean"}},"additionalProperties":false}"""),
        new("ver_grupo",
            "Detalle de un grupo: nombre, descripcion, ajustes y participantes (con admins). 'grupo' = jid @g.us (ver listar_grupos).",
            """{"type":"object","properties":{"grupo":{"type":"string"}},"required":["grupo"],"additionalProperties":false}"""),
        new("listar_etiquetas",
            "Etiquetas de WhatsApp Business de la linea (id, nombre, color) para etiquetar_chat.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("catalogo_business",
            "Catalogo de productos de WhatsApp Business (de la linea o de otro numero business).",
            """{"type":"object","properties":{"numero":{"type":"string","description":"Vacio = catalogo de la propia linea"},"limite":{"type":"integer","minimum":1,"maximum":50}},"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> LineStatusAsync(WhatsAppLineSummary line, CancellationToken ct)
    {
        var (node, raw) = await EvoAsync("GET", "/instance/connectionState/{instance}", null, ct, requireConnected: false);
        var state = S(node?["instance"]?["state"]) ?? S(node?["state"]);
        var agent = await _db.AiAgentLineBindings.AsNoTracking()
            .Where(b => b.WhatsAppLineId == _lineId && b.IsConnected)
            .Select(b => new { b.Agent!.Name, b.Agent.IsActive }).FirstOrDefaultAsync(ct);
        var chats = await _db.Conversations.AsNoTracking().CountAsync(c => c.WhatsAppLineId == _lineId, ct);
        return Ok(new
        {
            linea = line.Name,
            numero = line.PhoneNumber,
            estado_sistema = line.Status.ToString(),
            conexion_whatsapp = raw.Ok ? state switch { "open" => "conectada", "connecting" => "conectando", "close" => "desconectada", _ => state } : null,
            error_conexion = raw.Ok ? null : raw.Error,
            agente_sara = agent is null ? "no atiende esta linea" : $"{agent.Name}{(agent.IsActive ? "" : " (apagado)")}",
            chats_en_sistema = chats,
            nota = agent is not null ? "Cuando envias un mensaje, SARA deja de responder ese chat hasta que el cliente vuelva a escribir y se cumplan sus reglas; para silenciarla del todo usa pausar_agente." : null
        });
    }

    private async Task<McpToolResult> ListChatsAsync(JsonElement args, CancellationToken ct)
    {
        var limit = Math.Clamp(Int(args, "limite") ?? 25, 1, MaxListItems);
        var text = Str(args, "texto");
        if (Str(args, "fuente") == "whatsapp")
        {
            var (node, raw) = await EvoAsync("POST", "/chat/findChats/{instance}", new { }, ct);
            if (!raw.Ok) { return EvoErr(raw, "No se pudieron leer los chats de WhatsApp"); }
            var rows = (node as JsonArray ?? new JsonArray())
                .Select(c => new
                {
                    jid = S(c?["remoteJid"]) ?? S(c?["id"]),
                    nombre = S(c?["pushName"]) ?? S(c?["name"]),
                    no_leidos = I(c?["unreadCount"]) ?? I(c?["unreadMessages"]),
                    actualizado = c?["updatedAt"]?.ToString()
                })
                .Where(c => text is null || (c.nombre ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (c.jid ?? "").Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(limit).ToList();
            return Ok(new { fuente = "whatsapp", total = rows.Count, chats = rows });
        }

        var includeArchived = Bool(args, "incluir_archivados") == true;
        var q = _db.Conversations.AsNoTracking().Where(c => c.WhatsAppLineId == _lineId);
        if (!includeArchived) { q = q.Where(c => c.ArchivedAt == null); }
        if (text is not null)
        {
            var low = text.ToLower();
            q = q.Where(c => c.ContactPhone.Contains(low) || (c.ContactName != null && c.ContactName.ToLower().Contains(low)));
        }
        var convs = await q.OrderByDescending(c => c.LastMessageAt).Take(limit)
            .Select(c => new { c.Id, c.ContactPhone, c.ContactName, c.RemoteJid, c.LastMessageAt, c.ArchivedAt })
            .ToListAsync(ct);
        var ids = convs.Select(c => c.Id).ToList();
        var last = await _db.Messages.AsNoTracking().Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.SentAt).Select(m => new { m.ConversationId, m.Body, m.Direction, m.MessageType }).First())
            .ToListAsync(ct);
        var lastBy = last.ToDictionary(l => l.ConversationId);
        return Ok(new
        {
            fuente = "sistema",
            total = convs.Count,
            chats = convs.Select(c => new
            {
                conversacion_id = c.Id,
                numero = c.ContactPhone,
                nombre = c.ContactName,
                jid = c.RemoteJid,
                ultimo = c.LastMessageAt,
                archivado = c.ArchivedAt is null ? (bool?)null : true,
                ultimo_mensaje = lastBy.TryGetValue(c.Id, out var l)
                    ? $"{(l.Direction == MessageDirection.Inbound ? "cliente" : "nosotros")}: {Clip(string.IsNullOrWhiteSpace(l.Body) ? "[" + l.MessageType + "]" : l.Body, 120)}"
                    : null
            })
        });
    }

    private async Task<McpToolResult> ReadMessagesAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        var limit = Math.Clamp(Int(args, "limite") ?? 30, 1, 100);

        if (Str(args, "fuente") == "whatsapp" || t.IsGroup || t.ConversationId is null)
        {
            var (node, raw) = await EvoAsync("POST", "/chat/findMessages/{instance}",
                new { where = new { key = new { remoteJid = t.RemoteJid } }, page = 1, offset = limit }, ct);
            if (!raw.Ok) { return EvoErr(raw, "No se pudo leer el historial de WhatsApp"); }
            var records = node?["messages"]?["records"] as JsonArray ?? node as JsonArray ?? new JsonArray();
            var msgs = records.Select(m => new
            {
                mensaje_id = S(m?["key"]?["id"]),
                de_nosotros = B(m?["key"]?["fromMe"]),
                autor = S(m?["pushName"]),
                participante = S(m?["key"]?["participant"]),
                tipo = S(m?["messageType"]),
                texto = ExtractText(m?["message"]),
                fecha = m?["messageTimestamp"]?.ToString()
            }).Take(limit).Reverse().ToList();
            return Ok(new
            {
                fuente = "whatsapp",
                chat = t.RemoteJid,
                total = msgs.Count,
                mensajes = msgs,
                aviso = msgs.Count == 0 ? "Sin historial en Evolution (puede que el servidor no guarde mensajes)." : null
            });
        }

        var rows = await _db.Messages.AsNoTracking().Where(m => m.ConversationId == t.ConversationId)
            .OrderByDescending(m => m.SentAt).Take(limit)
            .Select(m => new
            {
                m.ExternalId,
                m.Direction,
                m.Body,
                m.MessageType,
                m.MediaType,
                m.MediaFileName,
                m.SentAt,
                m.SentByName,
                m.DeliveryStatus,
                m.Reaction
            })
            .ToListAsync(ct);
        rows.Reverse();
        return Ok(new
        {
            fuente = "sistema",
            conversacion_id = t.ConversationId,
            numero = t.Phone,
            total = rows.Count,
            mensajes = rows.Select(m => new
            {
                mensaje_id = m.ExternalId,
                de = m.Direction == MessageDirection.Inbound ? "cliente" : (m.SentByName ?? "nosotros"),
                texto = string.IsNullOrWhiteSpace(m.Body) ? null : m.Body,
                tipo = m.MessageType,
                media = m.MediaType == MessageMediaType.None ? null : m.MediaType.ToString().ToLowerInvariant(),
                archivo = m.MediaFileName,
                fecha = m.SentAt,
                entrega = m.DeliveryStatus,
                reaccion = m.Reaction
            })
        });
    }

    private async Task<McpToolResult> SearchContactsAsync(JsonElement args, CancellationToken ct)
    {
        var limit = Math.Clamp(Int(args, "limite") ?? 25, 1, MaxListItems);
        var text = Str(args, "texto");
        var (node, raw) = await EvoAsync("POST", "/chat/findContacts/{instance}", new { where = new { } }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudieron leer los contactos"); }
        var rows = (node as JsonArray ?? new JsonArray())
            .Select(c => new
            {
                jid = S(c?["remoteJid"]) ?? S(c?["id"]),
                nombre = S(c?["pushName"]),
                foto = S(c?["profilePicUrl"])
            })
            .Where(c => c.jid is not null && !c.jid.EndsWith("@g.us", StringComparison.Ordinal))
            .Where(c => text is null || (c.nombre ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || c.jid!.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
        return Ok(new { total = rows.Count, contactos = rows });
    }

    private async Task<McpToolResult> CheckNumbersAsync(JsonElement args, CancellationToken ct)
    {
        var numbers = StrList(args, "numeros").Select(n => PhoneNumber.ToMsisdn(n)).Where(n => n.Length >= 7).Distinct().Take(20).ToList();
        if (numbers.Count == 0) { return Err("Indica al menos un numero valido en 'numeros'."); }
        var (node, raw) = await EvoAsync("POST", "/chat/whatsappNumbers/{instance}", new { numbers }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudieron verificar los numeros"); }
        var rows = (node as JsonArray ?? new JsonArray()).Select(n => new
        {
            numero = S(n?["number"]),
            tiene_whatsapp = B(n?["exists"]),
            jid = S(n?["jid"])
        });
        return Ok(new { resultados = rows });
    }

    private async Task<McpToolResult> ContactProfileAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        if (t.IsGroup) { return Err("Para un grupo usa ver_grupo."); }
        var (profile, raw) = await EvoAsync("POST", "/chat/fetchProfile/{instance}", new { number = t.Number }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo leer el perfil"); }
        JsonNode? business = null;
        if (B(profile?["isBusiness"]) == true)
        {
            var (b, braw) = await EvoAsync("POST", "/chat/fetchBusinessProfile/{instance}", new { number = t.Number }, ct);
            if (braw.Ok) { business = Shrink(b); }
        }
        return Ok(new { ok = true, perfil = Shrink(profile), business });
    }

    private async Task<McpToolResult> DownloadMediaAsync(JsonElement args, CancellationToken ct)
    {
        var id = Str(args, "mensaje_id");
        if (id is null) { return Err("'mensaje_id' es obligatorio (ver leer_mensajes)."); }
        var (node, raw) = await EvoAsync("POST", "/chat/getBase64FromMediaMessage/{instance}",
            new { message = new { key = new { id } }, convertToMp4 = false }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo descargar la media"); }
        var b64 = S(node?["base64"]);
        var mime = S(node?["mimetype"]) ?? S(node?["mimeType"]) ?? "application/octet-stream";
        if (string.IsNullOrWhiteSpace(b64)) { return Err("Ese mensaje no tiene media descargable."); }
        if (b64.Length > 4 * 1024 * 1024 * 4 / 3) { return Err("El archivo supera 4 MB: abrelo en la aplicacion."); }
        var kind = mime.Split(';')[0].Trim();
        return new McpToolResult(Serialize(new { ok = true, mensaje_id = id, tipo = kind, tamano_kb = b64.Length * 3 / 4 / 1024 }), false)
        {
            Media = new McpMedia(b64, kind, S(node?["fileName"]))
        };
    }

    private async Task<McpToolResult> ListGroupsAsync(JsonElement args, CancellationToken ct)
    {
        var withParts = Bool(args, "con_participantes") == true ? "true" : "false";
        var (node, raw) = await EvoAsync("GET", $"/group/fetchAllGroups/{{instance}}?getParticipants={withParts}", null, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudieron leer los grupos"); }
        var rows = (node as JsonArray ?? new JsonArray()).Select(g => new
        {
            jid = S(g?["id"]),
            nombre = S(g?["subject"]),
            tamano = I(g?["size"]),
            descripcion = Clip(S(g?["desc"]) ?? "", 160),
            participantes = withParts == "true" ? Shrink(g?["participants"]?.DeepClone()) : null
        }).Take(MaxListItems);
        return Ok(new { grupos = rows });
    }

    private async Task<McpToolResult> ViewGroupAsync(JsonElement args, CancellationToken ct)
    {
        var jid = GroupJid(args);
        if (jid is null) { return Err("'grupo' debe ser un jid de grupo (termina en @g.us; ver listar_grupos)."); }
        var (info, raw) = await EvoAsync("GET", $"/group/findGroupInfos/{{instance}}?groupJid={Uri.EscapeDataString(jid)}", null, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo leer el grupo"); }
        return Ok(new { ok = true, grupo = Shrink(info) });
    }

    private async Task<McpToolResult> ListLabelsAsync(CancellationToken ct)
    {
        var (node, raw) = await EvoAsync("GET", "/label/findLabels/{instance}", null, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudieron leer las etiquetas"); }
        return Ok(new { ok = true, etiquetas = Shrink(node) });
    }

    private async Task<McpToolResult> CatalogAsync(JsonElement args, CancellationToken ct)
    {
        var number = Str(args, "numero");
        var body = new Dictionary<string, object?> { ["limit"] = Math.Clamp(Int(args, "limite") ?? 20, 1, 50) };
        if (number is not null) { body["number"] = PhoneNumber.ToMsisdn(number); }
        var (node, raw) = await EvoAsync("POST", "/business/getCatalog/{instance}", body, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo leer el catalogo"); }
        return Ok(new { ok = true, catalogo = Shrink(node) });
    }

    // ===================== Helpers =====================

    /// <summary>Texto legible de un mensaje de Evolution (texto, pie de media, encuesta, ubicacion...).</summary>
    private static string? ExtractText(JsonNode? msg)
    {
        if (msg is null) { return null; }
        var text = S(msg["conversation"])
                   ?? S(msg["extendedTextMessage"]?["text"])
                   ?? S(msg["imageMessage"]?["caption"])
                   ?? S(msg["videoMessage"]?["caption"])
                   ?? S(msg["documentMessage"]?["fileName"])
                   ?? S(msg["documentWithCaptionMessage"]?["message"]?["documentMessage"]?["caption"]);
        if (text is not null) { return text; }
        if (msg["audioMessage"] is not null) { return "[audio]"; }
        if (msg["stickerMessage"] is not null) { return "[sticker]"; }
        var poll = S(msg["pollCreationMessageV3"]?["name"]) ?? S(msg["pollCreationMessage"]?["name"]);
        if (poll is not null) { return "[encuesta] " + poll; }
        if (msg["locationMessage"] is not null) { return "[ubicacion]"; }
        if (msg["contactMessage"] is not null) { return "[contacto] " + S(msg["contactMessage"]?["displayName"]); }
        if (msg["reactionMessage"] is not null) { return "[reaccion] " + S(msg["reactionMessage"]?["text"]); }
        return null;
    }

    /// <summary>Lectores TOLERANTES de JSON de Evolution: null si el nodo falta o es de otro tipo (nunca lanzan).</summary>
    private static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? I(JsonNode? n)
    {
        if (n is not JsonValue v) { return null; }
        if (v.TryGetValue<int>(out var i)) { return i; }
        if (v.TryGetValue<long>(out var l)) { return (int)Math.Clamp(l, int.MinValue, int.MaxValue); }
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var j) ? j : null;
    }

    private static bool? B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static string? GroupJid(JsonElement args)
    {
        var g = Str(args, "grupo");
        return g is not null && g.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) ? g : null;
    }

    private static string ActionEs(PermissionAction a) => a switch
    {
        PermissionAction.View => "Ver",
        PermissionAction.Create => "Crear",
        PermissionAction.Edit => "Editar",
        PermissionAction.Delete => "Eliminar",
        _ => a.ToString()
    };

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static string? Str(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int? Int(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) { return i; }
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j) ? j : null;
    }

    private static double? Dbl(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) { return d; }
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : null;
    }

    private static bool? Bool(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v)) { return null; }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    private static List<string> StrList(JsonElement a, string name)
    {
        if (!a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) { return new List<string>(); }
        return v.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ValueKind == JsonValueKind.Number ? x.GetRawText() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
    }

    private static string Serialize(object payload) => JsonSerializer.Serialize(payload, JsonOut);

    private static McpToolResult Ok(object payload) => new(Serialize(payload), false);

    private static McpToolResult Err(string message) => new(Serialize(new { ok = false, error = message }), true);
}
