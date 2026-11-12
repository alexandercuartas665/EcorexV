using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Common;
using Ecorex.Application.Directorio;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// GESTION de chats, agente, contactos, GRUPOS y PERFIL de la linea (ADR-0164). Fuera de alcance a proposito (no se
/// exponen): logout/borrar/crear instancia, webhook, proxy y settings de Evolution (desconectarian Conversaciones y
/// a SARA), llamadas y plantillas de Cloud API.
/// </summary>
public sealed partial class WhatsAppMcpToolset
{
    private const string ChatProp = "\"chat\":{\"type\":\"string\",\"description\":\"Numero, jid o id de conversacion (ver listar_chats)\"}";
    private const string GrupoProp = "\"grupo\":{\"type\":\"string\",\"description\":\"jid del grupo (termina en @g.us; ver listar_grupos)\"}";

    private static readonly AiToolSpec[] ManageSpecs =
    {
        new("editar_mensaje",
            "Edita el texto de un mensaje ENVIADO por la linea (por mensaje_id). WhatsApp solo permite editar los recientes.",
            """{"type":"object","properties":{"mensaje_id":{"type":"string"},"texto":{"type":"string","maxLength":4096},""" + ChatProp + """},"required":["mensaje_id","texto"],"additionalProperties":false}"""),
        new("borrar_mensaje",
            "Borra PARA TODOS un mensaje enviado por la linea (por mensaje_id). No se puede deshacer: confirma con el usuario antes.",
            """{"type":"object","properties":{"mensaje_id":{"type":"string"},""" + ChatProp + """},"required":["mensaje_id"],"additionalProperties":false}"""),
        new("marcar_leido",
            "Marca como leidos (doble check azul) los ultimos mensajes del cliente en un chat.",
            """{"type":"object","properties":{""" + ChatProp + ""","cantidad":{"type":"integer","minimum":1,"maximum":50}},"required":["chat"],"additionalProperties":false}"""),
        new("gestionar_chat",
            "Acciones sobre un chat: archivar, desarchivar, marcar_no_leido, bloquear o desbloquear el contacto. Bloquear corta la comunicacion: confirma con el usuario.",
            """{"type":"object","properties":{""" + ChatProp + ""","accion":{"type":"string","enum":["archivar","desarchivar","marcar_no_leido","bloquear","desbloquear"]}},"required":["chat","accion"],"additionalProperties":false}"""),
        new("etiquetar_chat",
            "Agrega o quita una etiqueta de WhatsApp Business a un chat (etiqueta_id de listar_etiquetas).",
            """{"type":"object","properties":{""" + ChatProp + ""","etiqueta_id":{"type":"string"},"accion":{"type":"string","enum":["agregar","quitar"]}},"required":["chat","etiqueta_id"],"additionalProperties":false}"""),
        new("pausar_agente",
            "Silencia al agente SARA para un contacto (pasa a atencion humana). Usa la lista negra de la empresa: aplica a ese numero en TODAS las lineas hasta reanudar_agente.",
            """{"type":"object","properties":{""" + ChatProp + ""","motivo":{"type":"string"}},"required":["chat"],"additionalProperties":false}"""),
        new("reanudar_agente",
            "Vuelve a dejar que el agente SARA atienda a un contacto (lo saca de la lista negra).",
            """{"type":"object","properties":{""" + ChatProp + """},"required":["chat"],"additionalProperties":false}"""),
        new("guardar_contacto",
            "Guarda al contacto de un chat en el Directorio de ECOREX (persona, sin duplicar si ya existe). WhatsApp no permite crear contactos en la agenda del telefono; esto lo deja en el sistema.",
            """{"type":"object","properties":{""" + ChatProp + ""","nombre":{"type":"string","description":"Por defecto el nombre del chat"},"email":{"type":"string"},"ciudad":{"type":"string"},"cargo":{"type":"string"},"identificacion":{"type":"string"}},"required":["chat"],"additionalProperties":false}"""),
    };

    private static readonly AiToolSpec[] GroupSpecs =
    {
        new("crear_grupo",
            "Crea un grupo de WhatsApp con la linea como admin. Agregar personas a un grupo sin su permiso puede causar bloqueos: confirma con el usuario.",
            """{"type":"object","properties":{"nombre":{"type":"string","maxLength":100},"descripcion":{"type":"string"},"participantes":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":50}},"required":["nombre","participantes"],"additionalProperties":false}"""),
        new("editar_grupo",
            "Cambia un grupo: nombre, descripcion, foto (url/base64), solo_admins_escriben / todos_escriben, solo_admins_editan / todos_editan o mensajes_temporales (dias: 0, 1, 7 o 90). La linea debe ser admin.",
            """{"type":"object","properties":{""" + GrupoProp + ""","accion":{"type":"string","enum":["nombre","descripcion","foto","solo_admins_escriben","todos_escriben","solo_admins_editan","todos_editan","mensajes_temporales"]},"valor":{"type":"string","description":"Nuevo nombre o descripcion"},"url":{"type":"string"},"base64":{"type":"string"},"dias":{"type":"integer","enum":[0,1,7,90]}},"required":["grupo","accion"],"additionalProperties":false}"""),
        new("participantes_grupo",
            "Agrega, quita, promueve a admin o degrada participantes de un grupo (numeros).",
            """{"type":"object","properties":{""" + GrupoProp + ""","accion":{"type":"string","enum":["agregar","quitar","promover","degradar"]},"participantes":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":50}},"required":["grupo","accion","participantes"],"additionalProperties":false}"""),
        new("invitacion_grupo",
            "Enlaces de invitacion: ver_enlace, revocar (genera uno nuevo), enviar (a 'numeros'), info (de un 'codigo') o unirse (con un 'codigo').",
            """{"type":"object","properties":{"accion":{"type":"string","enum":["ver_enlace","revocar","enviar","info","unirse"]},""" + GrupoProp + ""","numeros":{"type":"array","items":{"type":"string"},"maxItems":20},"codigo":{"type":"string"},"descripcion":{"type":"string"}},"required":["accion"],"additionalProperties":false}"""),
        new("salir_grupo",
            "La linea SALE del grupo. No se puede deshacer sin una nueva invitacion: confirma con el usuario.",
            """{"type":"object","properties":{""" + GrupoProp + """},"required":["grupo"],"additionalProperties":false}"""),
        new("perfil_linea",
            "Perfil de la LINEA: ver, cambiar_nombre, cambiar_estado (texto 'info'), cambiar_foto (url/base64) o quitar_foto. Cambiar requiere permiso de editar Lineas.",
            """{"type":"object","properties":{"accion":{"type":"string","enum":["ver","cambiar_nombre","cambiar_estado","cambiar_foto","quitar_foto"]},"valor":{"type":"string"},"url":{"type":"string"},"base64":{"type":"string"}},"required":["accion"],"additionalProperties":false}"""),
        new("privacidad_linea",
            "Privacidad de la linea: ver o cambiar (solo los campos enviados). Valores: todos, contactos, contactos_excepto, nadie (en_linea: todos o igual_ultima_vez; leido: todos o nadie).",
            """{"type":"object","properties":{"accion":{"type":"string","enum":["ver","cambiar"]},"leido":{"type":"string","enum":["todos","nadie"]},"foto_perfil":{"type":"string","enum":["todos","contactos","contactos_excepto","nadie"]},"estado":{"type":"string","enum":["todos","contactos","contactos_excepto","nadie"]},"en_linea":{"type":"string","enum":["todos","igual_ultima_vez"]},"ultima_vez":{"type":"string","enum":["todos","contactos","contactos_excepto","nadie"]},"agregar_a_grupos":{"type":"string","enum":["todos","contactos","contactos_excepto"]}},"required":["accion"],"additionalProperties":false}"""),
        new("reiniciar_linea",
            "Reinicia la conexion de la linea con WhatsApp (util si deja de recibir mensajes). No cierra la sesion.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
    };

    // ===================== Mensajes =====================

    private async Task<McpToolResult> EditMessageAsync(JsonElement args, CancellationToken ct)
    {
        var id = Str(args, "mensaje_id");
        var text = Str(args, "texto");
        if (id is null || text is null) { return Err("'mensaje_id' y 'texto' son obligatorios."); }
        var key = await MessageKeyAsync(id, Str(args, "chat"), true, ct);
        if (key.Error is not null) { return Err(key.Error); }
        if (!key.FromMe) { return Err("Solo se pueden editar mensajes enviados por la linea."); }
        var (_, raw) = await EvoAsync("POST", "/chat/updateMessage/{instance}",
            new { number = key.RemoteJid, key = new { remoteJid = key.RemoteJid, fromMe = true, id }, text }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo editar el mensaje"); }
        var msg = await _db.Messages.FirstOrDefaultAsync(m => m.ExternalId == id, ct);
        if (msg is not null) { msg.Body = text; await _db.SaveChangesAsync(ct); }
        return Ok(new { ok = true, mensaje_id = id, editado = true });
    }

    private async Task<McpToolResult> DeleteMessageAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var id = Str(args, "mensaje_id");
        if (id is null) { return Err("'mensaje_id' es obligatorio."); }
        // Si el sistema lo tiene, se borra por la capa de chat (marca el mensaje en Conversaciones).
        var sys = await _db.Messages.AsNoTracking().Where(m => m.ExternalId == id && m.Direction == MessageDirection.Outbound)
            .Select(m => new { m.Id, m.ConversationId }).FirstOrDefaultAsync(ct);
        if (sys is not null && await _db.Conversations.AsNoTracking().AnyAsync(c => c.Id == sys.ConversationId && c.WhatsAppLineId == _lineId, ct))
        {
            var res = await _chat.DeleteMessageForEveryoneAsync(sys.Id, actor.PlatformUserId, ct);
            return res.Ok ? Ok(new { ok = true, mensaje_id = id, borrado_para_todos = true }) : Err(res.Error ?? "No se pudo borrar el mensaje.");
        }
        var key = await MessageKeyAsync(id, Str(args, "chat"), true, ct);
        if (key.Error is not null) { return Err(key.Error); }
        if (!key.FromMe) { return Err("Solo se pueden borrar para todos los mensajes enviados por la linea."); }
        var (_, raw) = await EvoAsync("DELETE", "/chat/deleteMessageForEveryone/{instance}",
            new { id, remoteJid = key.RemoteJid, fromMe = true }, ct);
        return raw.Ok ? Ok(new { ok = true, mensaje_id = id, borrado_para_todos = true }) : EvoErr(raw, "No se pudo borrar el mensaje");
    }

    private async Task<McpToolResult> MarkReadAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        var count = Math.Clamp(Int(args, "cantidad") ?? 10, 1, 50);
        List<string> ids;
        if (t.ConversationId is Guid convId)
        {
            ids = await _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == convId && m.Direction == MessageDirection.Inbound && m.ExternalId != null)
                .OrderByDescending(m => m.SentAt).Take(count).Select(m => m.ExternalId!).ToListAsync(ct);
        }
        else
        {
            var (node, raw) = await EvoAsync("POST", "/chat/findMessages/{instance}",
                new { where = new { key = new { remoteJid = t.RemoteJid, fromMe = false } }, page = 1, offset = count }, ct);
            if (!raw.Ok) { return EvoErr(raw, "No se pudieron leer los mensajes"); }
            var records = node?["messages"]?["records"] as JsonArray ?? node as JsonArray ?? new JsonArray();
            ids = records.Select(r => S(r?["key"]?["id"])).Where(x => x is not null).Select(x => x!).ToList();
        }
        if (ids.Count == 0) { return Ok(new { ok = true, marcados = 0, nota = "No hay mensajes del cliente para marcar." }); }
        var (_, mark) = await EvoAsync("POST", "/chat/markMessageAsRead/{instance}",
            new { readMessages = ids.Select(i => new { remoteJid = t.RemoteJid, fromMe = false, id = i }).ToList() }, ct);
        return mark.Ok ? Ok(new { ok = true, marcados = ids.Count }) : EvoErr(mark, "No se pudieron marcar como leidos");
    }

    // ===================== Chat =====================

    private async Task<McpToolResult> ManageChatAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        var action = Str(args, "accion");
        if (action is "bloquear" or "desbloquear")
        {
            if (t.IsGroup) { return Err("Un grupo no se bloquea; usa salir_grupo."); }
            var (_, raw) = await EvoAsync("POST", "/chat/updateBlockStatus/{instance}",
                new { number = t.Number, status = action == "bloquear" ? "block" : "unblock" }, ct);
            return raw.Ok ? Ok(new { ok = true, chat = t.Number, accion = action }) : EvoErr(raw, "No se pudo cambiar el bloqueo");
        }
        if (action is not ("archivar" or "desarchivar" or "marcar_no_leido"))
        {
            return Err("'accion' debe ser archivar, desarchivar, marcar_no_leido, bloquear o desbloquear.");
        }

        // WhatsApp exige la clave del ULTIMO mensaje del chat para archivar / marcar no leido.
        object? lastKey = null;
        if (t.ConversationId is Guid convId)
        {
            var last = await _db.Messages.AsNoTracking().Where(m => m.ConversationId == convId && m.ExternalId != null)
                .OrderByDescending(m => m.SentAt).Select(m => new { m.ExternalId, m.Direction }).FirstOrDefaultAsync(ct);
            if (last is not null) { lastKey = new { remoteJid = t.RemoteJid, fromMe = last.Direction == MessageDirection.Outbound, id = last.ExternalId }; }
        }
        if (lastKey is null)
        {
            var (node, raw) = await EvoAsync("POST", "/chat/findMessages/{instance}",
                new { where = new { key = new { remoteJid = t.RemoteJid } }, page = 1, offset = 1 }, ct);
            var rec = raw.Ok ? (node?["messages"]?["records"] as JsonArray ?? node as JsonArray)?.FirstOrDefault() : null;
            if (rec is not null)
            {
                lastKey = new { remoteJid = t.RemoteJid, fromMe = B(rec["key"]?["fromMe"]) ?? false, id = S(rec["key"]?["id"]) };
            }
        }
        if (lastKey is null) { return Err("No encontre mensajes en ese chat para aplicar la accion."); }

        var (_, res) = action == "marcar_no_leido"
            ? await EvoAsync("POST", "/chat/markChatUnread/{instance}", new { lastMessage = new { key = lastKey }, chat = t.RemoteJid }, ct)
            : await EvoAsync("POST", "/chat/archiveChat/{instance}", new { lastMessage = new { key = lastKey }, chat = t.RemoteJid, archive = action == "archivar" }, ct);
        if (!res.Ok) { return EvoErr(res, "No se pudo aplicar la accion al chat"); }
        if (action != "marcar_no_leido" && t.ConversationId is Guid cid)
        {
            await _chat.SetConversationArchivedAsync(cid, action == "archivar", ct); // mismo estado en Conversaciones
        }
        return Ok(new { ok = true, chat = t.Number, accion = action });
    }

    private async Task<McpToolResult> LabelChatAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        var label = Str(args, "etiqueta_id");
        if (label is null) { return Err("'etiqueta_id' es obligatorio (ver listar_etiquetas)."); }
        var add = Str(args, "accion") != "quitar";
        var (_, raw) = await EvoAsync("POST", "/label/handleLabel/{instance}",
            new { number = t.Number, labelId = label, action = add ? "add" : "remove" }, ct);
        return raw.Ok ? Ok(new { ok = true, chat = t.Number, etiqueta_id = label, accion = add ? "agregada" : "quitada" }) : EvoErr(raw, "No se pudo etiquetar");
    }

    // ===================== Agente SARA (handoff) =====================

    private async Task<McpToolResult> PauseAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        if (t.IsGroup || t.Phone is null) { return Err("Solo se puede pausar el agente para un contacto con numero."); }
        var list = await _blocked.ListAsync(ct);
        if (AgentControlCommands.IsBlocked(t.Phone, list.Select(b => b.Phone)))
        {
            return Ok(new { ok = true, numero = t.Phone, pausado = true, nota = "Ya estaba pausado (en la lista negra)." });
        }
        var motivo = Str(args, "motivo");
        var row = await _blocked.AddAsync(t.Phone, Clip($"Agente pausado via IA (MCP) por {actor.DisplayName}{(motivo is null ? "" : ": " + motivo)}", 300), actor.PlatformUserId, ct);
        if (row is null) { return Err("No se pudo pausar el agente para ese numero."); }
        return Ok(new { ok = true, numero = t.Phone, pausado = true, nota = "Aplica a ese numero en TODAS las lineas de la empresa hasta reanudar_agente." });
    }

    private async Task<McpToolResult> ResumeAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        if (t.IsGroup || t.Phone is null) { return Err("Solo aplica a un contacto con numero."); }
        var list = await _blocked.ListAsync(ct);
        var matches = list.Where(b => AgentControlCommands.IsBlocked(t.Phone, new[] { b.Phone })).ToList();
        foreach (var b in matches) { await _blocked.RemoveAsync(b.Id, actor.PlatformUserId, ct); }
        return Ok(new { ok = true, numero = t.Phone, reanudado = true, entradas_quitadas = matches.Count, nota = matches.Count == 0 ? "No estaba pausado." : null });
    }

    // ===================== Directorio =====================

    private async Task<McpToolResult> SaveContactAsync(JsonElement args, McpActor actor, EffectivePermissions perms, CancellationToken ct)
    {
        if (_directorio is null) { return Err("El Directorio no esta disponible."); }
        if (!perms.Can(DirectorioSubPermisos.CrearCliente, PermissionAction.Create) && !perms.Can(DirectorioSubPermisos.CrearSospechoso, PermissionAction.Create))
        {
            return Err("Tu usuario no tiene permiso para crear personas en el Directorio.");
        }
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        if (t.IsGroup || t.Phone is null) { return Err("Solo se guarda un contacto con numero (no un grupo)."); }
        var name = Str(args, "nombre");
        if (name is null && t.ConversationId is Guid cid)
        {
            name = await _db.Conversations.AsNoTracking().Where(c => c.Id == cid).Select(c => c.ContactName).FirstOrDefaultAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(name)) { return Err("Indica 'nombre' (el chat no tiene nombre guardado)."); }

        var payload = JsonSerializer.Serialize(new
        {
            nombre = name,
            tipo = "persona",
            telefono = t.Phone,
            email = Str(args, "email"),
            ciudad = Str(args, "ciudad"),
            cargo = Str(args, "cargo"),
            identificacion = Str(args, "identificacion")
        }, JsonOut);
        var res = await _directorio.ExecuteAsync("crear_contacto", payload, actor.PlatformUserId, autonomous: false, ct);
        var isError = false;
        try
        {
            using var doc = JsonDocument.Parse(res.Json);
            isError = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False;
        }
        catch (JsonException) { isError = true; }
        return new McpToolResult(res.Json, isError);
    }

    // ===================== Grupos =====================

    private async Task<McpToolResult> CreateGroupAsync(JsonElement args, CancellationToken ct)
    {
        var name = Str(args, "nombre");
        var members = StrList(args, "participantes").Select(p => PhoneNumber.ToMsisdn(p)).Where(p => p.Length >= 7).Distinct().Take(50).ToList();
        if (name is null || members.Count == 0) { return Err("Indica 'nombre' y al menos un participante valido."); }
        if (!SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())) { return Err("Limite de envios de la linea alcanzado. Espera un momento."); }
        var (node, raw) = await EvoAsync("POST", "/group/create/{instance}",
            new { subject = Clip(name, 100), description = Str(args, "descripcion"), participants = members }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo crear el grupo"); }
        return Ok(new { ok = true, grupo = S(node?["id"]), nombre = S(node?["subject"]) ?? name, participantes = members.Count });
    }

    private async Task<McpToolResult> EditGroupAsync(JsonElement args, CancellationToken ct)
    {
        var jid = GroupJid(args);
        if (jid is null) { return Err("'grupo' debe ser un jid de grupo (termina en @g.us)."); }
        var q = "?groupJid=" + Uri.EscapeDataString(jid);
        var action = Str(args, "accion");
        (string Path, object Body)? call = action switch
        {
            "nombre" => Str(args, "valor") is { } v1 ? ("/group/updateGroupSubject/{instance}" + q, new { subject = Clip(v1, 100) }) : null,
            "descripcion" => Str(args, "valor") is { } v2 ? ("/group/updateGroupDescription/{instance}" + q, new { description = v2 }) : null,
            "solo_admins_escriben" => ("/group/updateSetting/{instance}" + q, new { action = "announcement" }),
            "todos_escriben" => ("/group/updateSetting/{instance}" + q, new { action = "not_announcement" }),
            "solo_admins_editan" => ("/group/updateSetting/{instance}" + q, new { action = "locked" }),
            "todos_editan" => ("/group/updateSetting/{instance}" + q, new { action = "unlocked" }),
            "mensajes_temporales" => ("/group/toggleEphemeral/{instance}" + q, new
            {
                expiration = (Int(args, "dias") ?? 0) switch { 1 => 86400, 7 => 604800, 90 => 7776000, _ => 0 }
            }),
            _ => null
        };
        if (action == "foto")
        {
            var file = await LoadFileAsync(args, ct);
            if (file.Error is not null) { return Err(file.Error); }
            call = ("/group/updateGroupPicture/{instance}" + q, new { image = file.Base64 });
        }
        if (call is null) { return Err("Accion no valida o falta 'valor' (nombre/descripcion)."); }
        var (_, raw) = await EvoAsync("POST", call.Value.Path, call.Value.Body, ct);
        return raw.Ok ? Ok(new { ok = true, grupo = jid, accion = action }) : EvoErr(raw, "No se pudo editar el grupo");
    }

    private async Task<McpToolResult> GroupParticipantsAsync(JsonElement args, CancellationToken ct)
    {
        var jid = GroupJid(args);
        if (jid is null) { return Err("'grupo' debe ser un jid de grupo (termina en @g.us)."); }
        var action = Str(args, "accion") switch { "agregar" => "add", "quitar" => "remove", "promover" => "promote", "degradar" => "demote", _ => null };
        if (action is null) { return Err("'accion' debe ser agregar, quitar, promover o degradar."); }
        var members = StrList(args, "participantes").Select(p => p.Contains('@') ? p : PhoneNumber.ToMsisdn(p)).Where(p => p.Length >= 7).Distinct().Take(50).ToList();
        if (members.Count == 0) { return Err("Indica al menos un participante valido."); }
        if (action == "add" && !SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())) { return Err("Limite de envios de la linea alcanzado. Espera un momento."); }
        var (node, raw) = await EvoAsync("POST", "/group/updateParticipant/{instance}?groupJid=" + Uri.EscapeDataString(jid),
            new { action, participants = members }, ct);
        return raw.Ok ? Ok(new { ok = true, grupo = jid, accion = Str(args, "accion"), resultado = Shrink(node) }) : EvoErr(raw, "No se pudieron actualizar los participantes");
    }

    private async Task<McpToolResult> GroupInviteAsync(JsonElement args, CancellationToken ct)
    {
        var action = Str(args, "accion");
        var jid = GroupJid(args);
        var code = Str(args, "codigo");
        var gq = jid is null ? null : "?groupJid=" + Uri.EscapeDataString(jid);
        switch (action)
        {
            case "ver_enlace":
            case "revocar":
            case "enviar":
                if (gq is null) { return Err("'grupo' (jid @g.us) es obligatorio para esa accion."); }
                break;
            case "info":
            case "unirse":
                if (code is null) { return Err("'codigo' de invitacion es obligatorio para esa accion."); }
                break;
            default:
                return Err("'accion' debe ser ver_enlace, revocar, enviar, info o unirse.");
        }

        JsonNode? node;
        Admin.EvolutionRawResult raw;
        if (action == "ver_enlace") { (node, raw) = await EvoAsync("GET", "/group/inviteCode/{instance}" + gq, null, ct); }
        else if (action == "revocar") { (node, raw) = await EvoAsync("POST", "/group/revokeInviteCode/{instance}" + gq, new { }, ct); }
        else if (action == "enviar")
        {
            var numbers = StrList(args, "numeros").Select(n => PhoneNumber.ToMsisdn(n)).Where(n => n.Length >= 7).Distinct().Take(20).ToList();
            if (numbers.Count == 0) { return Err("Indica 'numeros' para enviar la invitacion."); }
            if (!SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())) { return Err("Limite de envios de la linea alcanzado. Espera un momento."); }
            (node, raw) = await EvoAsync("POST", "/group/sendInvite/{instance}",
                new { groupJid = jid, description = Str(args, "descripcion") ?? "Te invito a nuestro grupo", numbers }, ct);
        }
        else if (action == "info") { (node, raw) = await EvoAsync("GET", "/group/inviteInfo/{instance}?inviteCode=" + Uri.EscapeDataString(code!), null, ct); }
        else { (node, raw) = await EvoAsync("GET", "/group/acceptInviteCode/{instance}?inviteCode=" + Uri.EscapeDataString(code!), null, ct); }

        return raw.Ok ? Ok(new { ok = true, accion = action, resultado = Shrink(node) }) : EvoErr(raw, "No se pudo completar la invitacion");
    }

    private async Task<McpToolResult> LeaveGroupAsync(JsonElement args, CancellationToken ct)
    {
        var jid = GroupJid(args);
        if (jid is null) { return Err("'grupo' debe ser un jid de grupo (termina en @g.us)."); }
        var (_, raw) = await EvoAsync("DELETE", "/group/leaveGroup/{instance}?groupJid=" + Uri.EscapeDataString(jid), null, ct);
        return raw.Ok ? Ok(new { ok = true, grupo = jid, salio = true }) : EvoErr(raw, "No se pudo salir del grupo");
    }

    // ===================== Perfil / privacidad / reinicio de la linea =====================

    private async Task<McpToolResult> LineProfileAsync(JsonElement args, WhatsAppLineSummary line, CancellationToken ct)
    {
        var action = Str(args, "accion") ?? "ver";
        JsonNode? node;
        Admin.EvolutionRawResult raw;
        switch (action)
        {
            case "ver":
                var own = PhoneNumber.ToMsisdn(line.PhoneNumber);
                if (own.Length < 7) { return Err("La linea no tiene numero registrado en el sistema (conectala de nuevo)."); }
                (node, raw) = await EvoAsync("POST", "/chat/fetchProfile/{instance}", new { number = own }, ct);
                return raw.Ok ? Ok(new { ok = true, perfil = Shrink(node) }) : EvoErr(raw, "No se pudo leer el perfil");
            case "cambiar_nombre":
                var name = Str(args, "valor");
                if (name is null) { return Err("'valor' (nuevo nombre) es obligatorio."); }
                (node, raw) = await EvoAsync("POST", "/chat/updateProfileName/{instance}", new { name = Clip(name, 25) }, ct);
                break;
            case "cambiar_estado":
                var status = Str(args, "valor");
                if (status is null) { return Err("'valor' (nuevo estado/info) es obligatorio."); }
                (node, raw) = await EvoAsync("POST", "/chat/updateProfileStatus/{instance}", new { status = Clip(status, 139) }, ct);
                break;
            case "cambiar_foto":
                var file = await LoadFileAsync(args, ct);
                if (file.Error is not null) { return Err(file.Error); }
                (node, raw) = await EvoAsync("POST", "/chat/updateProfilePicture/{instance}", new { picture = file.Base64 }, ct);
                break;
            case "quitar_foto":
                (node, raw) = await EvoAsync("DELETE", "/chat/removeProfilePicture/{instance}", null, ct);
                break;
            default:
                return Err("'accion' debe ser ver, cambiar_nombre, cambiar_estado, cambiar_foto o quitar_foto.");
        }
        return raw.Ok ? Ok(new { ok = true, accion = action }) : EvoErr(raw, "No se pudo actualizar el perfil");
    }

    private async Task<McpToolResult> LinePrivacyAsync(JsonElement args, CancellationToken ct)
    {
        var (current, raw) = await EvoAsync("GET", "/chat/fetchPrivacySettings/{instance}", null, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo leer la privacidad"); }
        if ((Str(args, "accion") ?? "ver") == "ver") { return Ok(new { ok = true, privacidad = Shrink(current) }); }

        static string? Map(string? v) => v switch
        {
            "todos" => "all",
            "contactos" => "contacts",
            "contactos_excepto" => "contact_blacklist",
            "nadie" => "none",
            "igual_ultima_vez" => "match_last_seen",
            _ => null
        };
        // Evolution exige el set COMPLETO: se parte del actual y se cambian solo los campos enviados.
        var body = new Dictionary<string, string?>
        {
            ["readreceipts"] = Map(Str(args, "leido")) ?? S(current?["readreceipts"]) ?? "all",
            ["profile"] = Map(Str(args, "foto_perfil")) ?? S(current?["profile"]) ?? "all",
            ["status"] = Map(Str(args, "estado")) ?? S(current?["status"]) ?? "contacts",
            ["online"] = Map(Str(args, "en_linea")) ?? S(current?["online"]) ?? "all",
            ["last"] = Map(Str(args, "ultima_vez")) ?? S(current?["last"]) ?? "contacts",
            ["groupadd"] = Map(Str(args, "agregar_a_grupos")) ?? S(current?["groupadd"]) ?? "all"
        };
        var (_, upd) = await EvoAsync("POST", "/chat/updatePrivacySettings/{instance}", body, ct);
        return upd.Ok ? Ok(new { ok = true, privacidad = body }) : EvoErr(upd, "No se pudo cambiar la privacidad");
    }

    private async Task<McpToolResult> RestartLineAsync(CancellationToken ct)
    {
        var (_, raw) = await EvoAsync("POST", "/instance/restart/{instance}", new { }, ct, requireConnected: false);
        return raw.Ok ? Ok(new { ok = true, reiniciada = true, nota = "Revisa estado_linea en unos segundos." }) : EvoErr(raw, "No se pudo reiniciar la linea");
    }
}
