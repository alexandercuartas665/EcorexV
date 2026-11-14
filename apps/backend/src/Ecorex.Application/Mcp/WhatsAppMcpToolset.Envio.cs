using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// ENVIO por la linea (ADR-0164). Regla: a un chat 1:1 se envia por IChatService (texto, imagen/video/documento/nota
/// de voz, ubicacion) para que quede en Conversaciones a nombre del usuario "(via IA)" y SARA ceda el turno; lo que
/// IChatService no sabe enviar (respuesta citada, contacto, encuesta, lista, sticker, video-nota) va directo a
/// Evolution y luego se REGISTRA en la conversacion (RecordOutboundAsync). A grupos se envia directo (el sistema no
/// guarda hilos de grupo). Todo envio pasa por SendGuard (primer contacto confirmado + limite por linea).
/// </summary>
public sealed partial class WhatsAppMcpToolset
{
    private const int MaxMediaBytes = 16 * 1024 * 1024;
    private const int MaxInlineBase64Bytes = 2 * 1024 * 1024;

    private const string ParaProp = "\"para\":{\"type\":\"string\",\"description\":\"Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion\"}";
    private const string ConfirmProp = "\"primer_contacto_confirmado\":{\"type\":\"boolean\",\"description\":\"true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea\"}";

    private static readonly AiToolSpec[] SendSpecs =
    {
        new("enviar_texto",
            "Envia un mensaje de texto (un destinatario por llamada). 'responder_a' = mensaje_id para citarlo. A un chat 1:1 queda en Conversaciones a tu nombre y SARA deja de responder ese chat. Lee el chat antes de responder.",
            """{"type":"object","properties":{""" + ParaProp + ""","texto":{"type":"string","maxLength":4096},"responder_a":{"type":"string","description":"mensaje_id a citar (ver leer_mensajes)"},""" + ConfirmProp + """},"required":["para","texto"],"additionalProperties":false}"""),
        new("enviar_media",
            "Envia un archivo: tipo imagen, video, documento, audio (llega como nota de voz), sticker o video_nota. El archivo va por 'url' (https publica, maximo 16 MB) o 'base64' (maximo 2 MB, con 'mime'). 'pie' = texto debajo (imagen/video/documento).",
            """{"type":"object","properties":{""" + ParaProp + ""","tipo":{"type":"string","enum":["imagen","video","documento","audio","sticker","video_nota"]},"url":{"type":"string"},"base64":{"type":"string"},"mime":{"type":"string"},"nombre_archivo":{"type":"string"},"pie":{"type":"string"},""" + ConfirmProp + """},"required":["para","tipo"],"additionalProperties":false}"""),
        new("enviar_ubicacion",
            "Envia una ubicacion (latitud/longitud) con nombre y direccion opcionales.",
            """{"type":"object","properties":{""" + ParaProp + ""","latitud":{"type":"number"},"longitud":{"type":"number"},"nombre":{"type":"string"},"direccion":{"type":"string"},""" + ConfirmProp + """},"required":["para","latitud","longitud"],"additionalProperties":false}"""),
        new("enviar_contacto",
            "Envia una o varias tarjetas de contacto (vCard).",
            """{"type":"object","properties":{""" + ParaProp + ""","contactos":{"type":"array","minItems":1,"maxItems":5,"items":{"type":"object","properties":{"nombre":{"type":"string"},"telefono":{"type":"string"},"empresa":{"type":"string"},"email":{"type":"string"}},"required":["nombre","telefono"],"additionalProperties":false}},""" + ConfirmProp + """},"required":["para","contactos"],"additionalProperties":false}"""),
        new("enviar_encuesta",
            "Envia una encuesta con 2 a 12 opciones. 'multiple' = se pueden elegir varias.",
            """{"type":"object","properties":{""" + ParaProp + ""","pregunta":{"type":"string"},"opciones":{"type":"array","items":{"type":"string"},"minItems":2,"maxItems":12},"multiple":{"type":"boolean"},""" + ConfirmProp + """},"required":["para","pregunta","opciones"],"additionalProperties":false}"""),
        new("enviar_lista",
            "Envia un mensaje con lista de opciones (menu). Ojo: WhatsApp restringe los mensajes interactivos en cuentas no oficiales y en algunos telefonos no se muestran; si importa que llegue, usa texto o encuesta.",
            """{"type":"object","properties":{""" + ParaProp + ""","titulo":{"type":"string"},"descripcion":{"type":"string"},"boton":{"type":"string","description":"Texto del boton que abre la lista"},"pie":{"type":"string"},"secciones":{"type":"array","minItems":1,"maxItems":10,"items":{"type":"object","properties":{"titulo":{"type":"string"},"filas":{"type":"array","minItems":1,"maxItems":10,"items":{"type":"object","properties":{"titulo":{"type":"string"},"descripcion":{"type":"string"},"id":{"type":"string"}},"required":["titulo"],"additionalProperties":false}}},"required":["titulo","filas"],"additionalProperties":false}},""" + ConfirmProp + """},"required":["para","titulo","descripcion","boton","secciones"],"additionalProperties":false}"""),
        new("publicar_estado",
            "Publica un ESTADO de WhatsApp (historia) de la linea: texto, imagen, video o audio. Exige 'para_todos'=true (todos los contactos) o una lista de 'contactos'. Llega a mucha gente: confirma con el usuario antes.",
            """{"type":"object","properties":{"tipo":{"type":"string","enum":["texto","imagen","video","audio"]},"contenido":{"type":"string","description":"Texto del estado (tipo texto)"},"url":{"type":"string"},"base64":{"type":"string"},"pie":{"type":"string"},"color_fondo":{"type":"string","description":"Hex, ej. #25D366 (tipo texto)"},"para_todos":{"type":"boolean"},"contactos":{"type":"array","items":{"type":"string"},"maxItems":256}},"required":["tipo"],"additionalProperties":false}"""),
        new("reaccionar",
            "Reacciona con un emoji a un mensaje (por mensaje_id). Emoji vacio = quitar la reaccion. Si el mensaje no esta en el sistema indica 'chat'.",
            """{"type":"object","properties":{"mensaje_id":{"type":"string"},"emoji":{"type":"string"},"chat":{"type":"string"},"de_nosotros":{"type":"boolean","description":"Si el mensaje lo enviamos nosotros (solo si no esta en el sistema)"}},"required":["mensaje_id","emoji"],"additionalProperties":false}"""),
        new("escribiendo",
            "Muestra 'escribiendo...' o 'grabando audio...' en el chat durante unos segundos (o lo detiene con 'pausa').",
            """{"type":"object","properties":{"chat":{"type":"string"},"estado":{"type":"string","enum":["escribiendo","grabando","pausa"]},"segundos":{"type":"integer","minimum":1,"maximum":25}},"required":["chat"],"additionalProperties":false}"""),
    };

    // ===================== Texto =====================

    private async Task<McpToolResult> SendTextAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        var text = Str(args, "texto");
        if (text is null) { return Err("'texto' es obligatorio."); }
        if (text.Length > 4096) { return Err("El texto supera 4096 caracteres."); }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }

        var quoted = Str(args, "responder_a");
        // Por la capa de chat (persiste + SARA cede el turno) cuando es 1:1, sin cita y con numero conocido.
        if (!t.IsGroup && quoted is null && t.Phone is not null)
        {
            var convId = await EnsureConversationAsync(t, ct);
            if (convId is null) { return Err("No se pudo abrir la conversacion de ese numero."); }
            var res = await _chat.SendViaLineAsync(convId.Value, _lineId, text, actor.PlatformUserId, ct, Label(actor));
            if (!res.Ok) { return Err(res.Error ?? "No se pudo enviar."); }
            return Ok(new { ok = true, enviado_a = t.Number, conversacion_id = convId, mensaje_id = await ExternalIdOfAsync(res.Message?.Id, ct), registrado_en_conversaciones = true });
        }

        var body = new Dictionary<string, object?> { ["number"] = t.Number, ["text"] = text };
        if (quoted is not null) { body["quoted"] = new { key = new { id = quoted } }; }
        var (_, raw) = await EvoAsync("POST", "/message/sendText/{instance}", body, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar"); }
        return await RecordAndOkAsync(t, text, "text", raw.MessageId, actor, ct);
    }

    // ===================== Media =====================

    private async Task<McpToolResult> SendMediaAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        var kind = Str(args, "tipo");
        if (kind is not ("imagen" or "video" or "documento" or "audio" or "sticker" or "video_nota"))
        {
            return Err("'tipo' debe ser imagen, video, documento, audio, sticker o video_nota.");
        }
        var file = await LoadFileAsync(args, ct);
        if (file.Error is not null) { return Err(file.Error); }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }

        var caption = Str(args, "pie");
        var fileName = Str(args, "nombre_archivo") ?? file.FileName;
        var mime = Str(args, "mime") ?? file.Mime;
        var mediaType = kind switch
        {
            "imagen" => MessageMediaType.Image,
            "video" => MessageMediaType.Video,
            "documento" => MessageMediaType.Document,
            "audio" => MessageMediaType.Audio,
            _ => MessageMediaType.None
        };

        // 1:1 con imagen/video/documento/audio -> capa de chat (persiste con la media y SARA cede el turno).
        if (!t.IsGroup && t.Phone is not null && mediaType != MessageMediaType.None)
        {
            var convId = await EnsureConversationAsync(t, ct);
            if (convId is null) { return Err("No se pudo abrir la conversacion de ese numero."); }
            var res = await _chat.SendMediaViaLineAsync(convId.Value, _lineId, mediaType, file.Base64!, Str(args, "url") ?? string.Empty,
                mime, fileName, caption, actor.PlatformUserId, ct, Label(actor));
            if (!res.Ok) { return Err(res.Error ?? "No se pudo enviar el archivo."); }
            return Ok(new { ok = true, enviado_a = t.Number, tipo = kind, conversacion_id = convId, mensaje_id = await ExternalIdOfAsync(res.Message?.Id, ct), registrado_en_conversaciones = true });
        }

        (string Path, object Body, string Summary) call = kind switch
        {
            "audio" => ("/message/sendWhatsAppAudio/{instance}", new { number = t.Number, audio = file.Base64 }, "[nota de voz]"),
            "sticker" => ("/message/sendSticker/{instance}", new { number = t.Number, sticker = file.Base64 }, "[sticker]"),
            "video_nota" => ("/message/sendPtv/{instance}", new { number = t.Number, video = file.Base64 }, "[video nota]"),
            _ => ("/message/sendMedia/{instance}", new
            {
                number = t.Number,
                mediatype = kind switch { "imagen" => "image", "video" => "video", _ => "document" },
                mimetype = mime,
                caption,
                media = file.Base64,
                fileName
            }, caption ?? $"[{kind}] {fileName}")
        };
        var (_, raw) = await EvoAsync("POST", call.Path, call.Body, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar el archivo"); }
        return await RecordAndOkAsync(t, call.Summary, kind, raw.MessageId, actor, ct);
    }

    // ===================== Ubicacion / contacto / encuesta / lista =====================

    private async Task<McpToolResult> SendLocationAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        var lat = Dbl(args, "latitud");
        var lng = Dbl(args, "longitud");
        if (lat is null or < -90 or > 90 || lng is null or < -180 or > 180) { return Err("'latitud' y 'longitud' son obligatorias y validas."); }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }
        var name = Str(args, "nombre");

        if (!t.IsGroup && t.Phone is not null)
        {
            var convId = await EnsureConversationAsync(t, ct);
            if (convId is null) { return Err("No se pudo abrir la conversacion de ese numero."); }
            var res = await _chat.SendLocationViaLineAsync(convId.Value, _lineId, lat.Value, lng.Value, name, actor.PlatformUserId, ct, Label(actor));
            if (!res.Ok) { return Err(res.Error ?? "No se pudo enviar la ubicacion."); }
            return Ok(new { ok = true, enviado_a = t.Number, conversacion_id = convId, registrado_en_conversaciones = true });
        }
        var (_, raw) = await EvoAsync("POST", "/message/sendLocation/{instance}",
            new { number = t.Number, latitude = lat, longitude = lng, name, address = Str(args, "direccion") }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar la ubicacion"); }
        return await RecordAndOkAsync(t, "[ubicacion] " + (name ?? $"{lat},{lng}"), "location", raw.MessageId, actor, ct);
    }

    private async Task<McpToolResult> SendContactCardAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        if (!args.TryGetProperty("contactos", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            return Err("'contactos' es obligatorio (al menos uno con nombre y telefono).");
        }
        var cards = new List<object>();
        var names = new List<string>();
        foreach (var c in list.EnumerateArray().Take(5))
        {
            var name = Str(c, "nombre");
            var phone = PhoneNumber.ToMsisdn(Str(c, "telefono"));
            if (name is null || phone.Length < 7) { return Err("Cada contacto necesita 'nombre' y un 'telefono' valido."); }
            cards.Add(new { fullName = name, wuid = phone, phoneNumber = "+" + phone, organization = Str(c, "empresa"), email = Str(c, "email") });
            names.Add($"{name} ({phone})");
        }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }
        var (_, raw) = await EvoAsync("POST", "/message/sendContact/{instance}", new { number = t.Number, contact = cards }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar el contacto"); }
        return await RecordAndOkAsync(t, "[contacto] " + string.Join(", ", names), "contact", raw.MessageId, actor, ct);
    }

    private async Task<McpToolResult> SendPollAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        var question = Str(args, "pregunta");
        var options = StrList(args, "opciones").Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (question is null || options.Count < 2) { return Err("Indica 'pregunta' y al menos 2 'opciones' distintas."); }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }
        var (_, raw) = await EvoAsync("POST", "/message/sendPoll/{instance}",
            new { number = t.Number, name = question, selectableCount = Bool(args, "multiple") == true ? options.Count : 1, values = options }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar la encuesta"); }
        return await RecordAndOkAsync(t, $"[encuesta] {question}: {string.Join(" | ", options)}", "poll", raw.MessageId, actor, ct);
    }

    private async Task<McpToolResult> SendListAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "para"), ct);
        if (t is null) { return Err(error!); }
        var title = Str(args, "titulo");
        var desc = Str(args, "descripcion");
        var button = Str(args, "boton");
        if (title is null || desc is null || button is null) { return Err("'titulo', 'descripcion' y 'boton' son obligatorios."); }
        if (!args.TryGetProperty("secciones", out var secs) || secs.ValueKind != JsonValueKind.Array || secs.GetArrayLength() == 0)
        {
            return Err("'secciones' es obligatorio (con sus filas).");
        }
        var sections = new List<object>();
        var n = 0;
        foreach (var s in secs.EnumerateArray().Take(10))
        {
            var rows = new List<object>();
            if (s.TryGetProperty("filas", out var filas) && filas.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in filas.EnumerateArray().Take(10))
                {
                    var rt = Str(r, "titulo");
                    if (rt is null) { continue; }
                    rows.Add(new { title = rt, description = Str(r, "descripcion"), rowId = Str(r, "id") ?? $"op{++n}" });
                }
            }
            if (rows.Count == 0) { return Err("Cada seccion necesita al menos una fila con 'titulo'."); }
            sections.Add(new { title = Str(s, "titulo") ?? "Opciones", rows });
        }
        var guard = SendGuard(t, args);
        if (guard is not null) { return Err(guard); }
        var (_, raw) = await EvoAsync("POST", "/message/sendList/{instance}",
            new { number = t.Number, title, description = desc, buttonText = button, footerText = Str(args, "pie") ?? string.Empty, sections }, ct);
        if (!raw.Ok) { return EvoErr(raw, "No se pudo enviar la lista"); }
        return await RecordAndOkAsync(t, $"[lista] {title}", "list", raw.MessageId, actor, ct);
    }

    // ===================== Estado / reaccion / presencia =====================

    private async Task<McpToolResult> PublishStatusAsync(JsonElement args, CancellationToken ct)
    {
        var kind = Str(args, "tipo");
        if (kind is not ("texto" or "imagen" or "video" or "audio")) { return Err("'tipo' debe ser texto, imagen, video o audio."); }
        var all = Bool(args, "para_todos") == true;
        var jids = StrList(args, "contactos").Select(c => c.Contains('@') ? c : PhoneNumber.ToMsisdn(c) + "@s.whatsapp.net").Distinct().ToList();
        if (!all && jids.Count == 0) { return Err("Indica 'para_todos'=true o una lista de 'contactos'."); }

        string content;
        if (kind == "texto")
        {
            content = Str(args, "contenido") ?? string.Empty;
            if (content.Length == 0) { return Err("'contenido' es obligatorio para un estado de texto."); }
        }
        else
        {
            var file = await LoadFileAsync(args, ct);
            if (file.Error is not null) { return Err(file.Error); }
            content = StatusMediaContent(kind, file.Base64!, Str(args, "mime") ?? file.Mime);
        }
        if (!SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())) { return Err("Limite de envios de la linea alcanzado. Espera un momento."); }

        var (_, raw) = await EvoAsync("POST", "/message/sendStatus/{instance}", new
        {
            type = kind switch { "texto" => "text", "imagen" => "image", "video" => "video", _ => "audio" },
            content,
            // Evolution exige caption string y statusJidList array aunque sea para todos (rechaza null con HTTP 400).
            caption = Str(args, "pie") ?? string.Empty,
            backgroundColor = Str(args, "color_fondo") ?? "#25D366",
            font = 1,
            allContacts = all,
            statusJidList = all ? new List<string>() : jids
        }, ct);
        return raw.Ok ? Ok(new { ok = true, estado = kind, para_todos = all, contactos = all ? (int?)null : jids.Count }) : EvoErr(raw, "No se pudo publicar el estado");
    }

    /// <summary>
    /// Contenido de un estado con media para Evolution 2.3.x: en imagen y video arma { url: content } y Baileys solo
    /// acepta ahi http(s), data: URI o una RUTA de archivo; el base64 suelto lo abre como ruta (ENAMETOOLONG). Por eso
    /// imagen y video van como data: URI. El audio lo convierte Evolution con ffmpeg desde base64 crudo.
    /// </summary>
    internal static string StatusMediaContent(string kind, string base64, string? mime)
    {
        if (kind is not ("imagen" or "video")) { return base64; }
        var tipo = !string.IsNullOrWhiteSpace(mime) && mime.Contains('/') ? mime.Trim() : kind == "video" ? "video/mp4" : "image/jpeg";
        return $"data:{tipo};base64,{base64}";
    }

    private async Task<McpToolResult> ReactAsync(JsonElement args, CancellationToken ct)
    {
        var id = Str(args, "mensaje_id");
        if (id is null) { return Err("'mensaje_id' es obligatorio."); }
        var emoji = args.TryGetProperty("emoji", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty;
        var key = await MessageKeyAsync(id, Str(args, "chat"), Bool(args, "de_nosotros"), ct);
        if (key.Error is not null) { return Err(key.Error); }
        if (!SendLimiter.TryAcquire(_lineId, _clock.GetUtcNow())) { return Err("Limite de envios de la linea alcanzado. Espera un momento."); }
        var (_, raw) = await EvoAsync("POST", "/message/sendReaction/{instance}",
            new { key = new { remoteJid = key.RemoteJid, fromMe = key.FromMe, id }, reaction = emoji }, ct);
        return raw.Ok ? Ok(new { ok = true, mensaje_id = id, reaccion = emoji.Length == 0 ? "(quitada)" : emoji }) : EvoErr(raw, "No se pudo reaccionar");
    }

    private async Task<McpToolResult> PresenceAsync(JsonElement args, CancellationToken ct)
    {
        var (t, error) = await ResolveTargetAsync(Str(args, "chat"), ct);
        if (t is null) { return Err(error!); }
        var presence = Str(args, "estado") switch { "grabando" => "recording", "pausa" => "paused", _ => "composing" };
        var seconds = Math.Clamp(Int(args, "segundos") ?? 5, 1, 25);
        var (_, raw) = await EvoAsync("POST", "/chat/sendPresence/{instance}", new { number = t.Number, presence, delay = seconds * 1000 }, ct);
        return raw.Ok ? Ok(new { ok = true, chat = t.Number, estado = presence }) : EvoErr(raw, "No se pudo cambiar la presencia");
    }

    // ===================== Comunes de envio =====================

    /// <summary>Registra en Conversaciones (si es 1:1 con numero conocido) un envio hecho directo por Evolution.</summary>
    private async Task<McpToolResult> RecordAndOkAsync(Target t, string summary, string type, string? externalId, McpActor actor, CancellationToken ct)
    {
        Guid? convId = null;
        if (!t.IsGroup)
        {
            convId = await EnsureConversationAsync(t, ct);
            if (convId is Guid id)
            {
                await _chat.RecordOutboundAsync(id, _lineId, summary, type, externalId, actor.PlatformUserId, Label(actor), ct);
            }
        }
        return Ok(new
        {
            ok = true,
            enviado_a = t.Number,
            mensaje_id = externalId,
            conversacion_id = convId,
            registrado_en_conversaciones = convId is not null,
            nota = t.IsGroup ? "Los grupos no se guardan en Conversaciones (solo en WhatsApp)." : null
        });
    }

    private async Task<string?> ExternalIdOfAsync(Guid? messageId, CancellationToken ct)
        => messageId is Guid id
            ? await _db.Messages.AsNoTracking().Where(m => m.Id == id).Select(m => m.ExternalId).FirstOrDefaultAsync(ct)
            : null;

    /// <summary>Clave WhatsApp (remoteJid + fromMe) de un mensaje: del sistema si lo tiene; si no, de 'chat'.</summary>
    private async Task<(string? RemoteJid, bool FromMe, string? Error)> MessageKeyAsync(string externalId, string? chat, bool? fromMeHint, CancellationToken ct)
    {
        var msg = await _db.Messages.AsNoTracking().Where(m => m.ExternalId == externalId)
            .Select(m => new { m.ConversationId, m.Direction }).FirstOrDefaultAsync(ct);
        if (msg is not null)
        {
            var conv = await _db.Conversations.AsNoTracking().Where(c => c.Id == msg.ConversationId && c.WhatsAppLineId == _lineId)
                .Select(c => new { c.RemoteJid, c.ContactPhone }).FirstOrDefaultAsync(ct);
            if (conv is not null)
            {
                return (conv.RemoteJid ?? conv.ContactPhone + "@s.whatsapp.net", msg.Direction == MessageDirection.Outbound, null);
            }
        }
        if (chat is null) { return (null, false, "Ese mensaje no esta en el sistema: indica tambien 'chat'."); }
        var (t, error) = await ResolveTargetAsync(chat, ct);
        return t is null ? (null, false, error) : (t.RemoteJid, fromMeHint ?? false, null);
    }

    /// <summary>Archivo de 'url' (https publica, descarga blindada) o 'base64' (pequeno). Nunca lee rutas locales.</summary>
    private static async Task<(string? Base64, string? Mime, string? FileName, string? Error)> LoadFileAsync(JsonElement args, CancellationToken ct)
    {
        var inline = Str(args, "base64");
        if (inline is not null)
        {
            var comma = inline.IndexOf(',');
            if (inline.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) { inline = inline[(comma + 1)..]; }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(inline); }
            catch (FormatException) { return (null, null, null, "'base64' no es valido."); }
            if (bytes.Length > MaxInlineBase64Bytes) { return (null, null, null, "Por base64 el maximo es 2 MB: usa 'url'."); }
            return (inline, Str(args, "mime"), Str(args, "nombre_archivo"), null);
        }
        return await DownloadAsync(Str(args, "url"), ct);
    }

    // Cliente HTTP propio para descargar media de la IA: SIN redirecciones y validando la IP EN EL MOMENTO de conectar
    // (bloquea redes privadas, loopback, link-local y el DNS rebinding). Asi una URL no puede apuntar a servicios
    // internos (SSRF).
    private static readonly HttpClient MediaHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
            {
                throw new HttpRequestException("La URL apunta a una red privada o no resuelve.");
            }
            var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[0], ctx.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private static async Task<(string? Base64, string? Mime, string? FileName, string? Error)> DownloadAsync(string? url, CancellationToken ct)
    {
        if (url is null) { return (null, null, null, "Indica el archivo por 'url' (https) o 'base64'."); }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return (null, null, null, "Solo se aceptan URLs https publicas.");
        }
        try
        {
            using var resp = await MediaHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400) { return (null, null, null, "La URL redirige: usa el enlace directo al archivo."); }
            if (!resp.IsSuccessStatusCode) { return (null, null, null, $"No se pudo descargar el archivo (HTTP {(int)resp.StatusCode})."); }
            if (resp.Content.Headers.ContentLength > MaxMediaBytes) { return (null, null, null, "El archivo supera 16 MB."); }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                ms.Write(buffer, 0, read);
                if (ms.Length > MaxMediaBytes) { return (null, null, null, "El archivo supera 16 MB."); }
            }
            var name = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName
                       ?? Path.GetFileName(uri.LocalPath);
            return (Convert.ToBase64String(ms.ToArray()), resp.Content.Headers.ContentType?.MediaType, name?.Trim('"'), null);
        }
        catch (HttpRequestException ex) { return (null, null, null, "No se pudo descargar el archivo: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return (null, null, null, "Tiempo agotado descargando el archivo."); }
    }

    /// <summary>True si la IP es privada/reservada (no se permite descargar de ahi).</summary>
    internal static bool IsPrivateAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) { ip = ip.MapToIPv4(); }
        if (IPAddress.IsLoopback(ip)) { return true; }
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] is 0 or 10 or 127
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254)
                   || (b[0] == 100 && b[1] is >= 64 and <= 127)
                   || b[0] >= 224;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast
                   || (b[0] & 0xFE) == 0xFC
                   || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None);
        }
        return true;
    }
}
