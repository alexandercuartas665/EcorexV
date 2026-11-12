using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <inheritdoc />
public sealed class AgentCierreService : IAgentCierreService
{
    private readonly IApplicationDbContext _db;
    private readonly INotificationChannelSender _sender;
    private readonly INotifyTokenResolver _tokens;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public AgentCierreService(IApplicationDbContext db, INotificationChannelSender sender,
        INotifyTokenResolver tokens, IAuditWriter audit, TimeProvider clock)
    {
        _db = db;
        _sender = sender;
        _tokens = tokens;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AgentCierreConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var json = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.CierreJson)
            .FirstOrDefaultAsync(cancellationToken);
        return AgentCierreConfig.Parse(json);
    }

    public async Task<bool> SaveConfigAsync(Guid agentId, AgentCierreConfig config, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return false; }

        var previous = agent.CierreJson;
        // Config vacia => null (mantiene limpio el campo y la compatibilidad hacia atras).
        agent.CierreJson = config.IsNoop ? null : config.Serialize();

        _audit.Write(actorUserId, "ai-agent.cierre-config", nameof(Domain.Entities.AiAgent), agent.Id,
            previousValue: new { hadConfig = previous is not null },
            newValue: new { config.Olvidar, alertas = config.Alertas?.Count ?? 0 },
            tenantId: agent.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task HandleCloseAsync(Guid agentId, Guid conversationId, string? summary, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var agent = await _db.AiAgents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Name, a.CierreJson })
                .FirstOrDefaultAsync(cancellationToken);
            if (agent is null) { return; }

            var config = AgentCierreConfig.Parse(agent.CierreJson);
            if (config.IsNoop) { return; }

            var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
            if (conv is null) { return; }

            // (a) Olvidar al cliente: reinicio NO destructivo del contexto (saluda desde cero la proxima vez).
            if (config.Olvidar)
            {
                conv.AgentContextResetAt = _clock.GetUtcNow();
            }

            // (b) Alertas: cada una se resuelve y envia de forma independiente (un fallo no frena las demas).
            if (config.Alertas is { Count: > 0 })
            {
                var tokenMap = BuildTokenMap(agent.Name, conv, summary);
                foreach (var alerta in config.Alertas)
                {
                    try { await DispatchAlertAsync(alerta, conv, agentId, tokenMap, summary, actorUserId, cancellationToken); }
                    catch (Exception ex)
                    {
                        // Un envio fallido no debe romper el cierre ni las demas alertas, pero NO es silencioso:
                        // el motivo queda en la bitacora del agente (/bitacora-agente).
                        RecordClose(conv, agentId, false, ChannelLabel(alerta.Canal), "(error)", ex.Message, null);
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Best-effort: el cierre nunca debe propagar una excepcion a la respuesta del agente.
        }
    }

    private async Task DispatchAlertAsync(AgentCierreAlerta alerta, Domain.Entities.Conversation conv, Guid agentId,
        IReadOnlyDictionary<string, string> tokenMap, string? summary, Guid actor, CancellationToken ct)
    {
        // Grupo de Evolution -> texto plano al jid del grupo (no depende de un usuario destino).
        if (alerta.Canal == CierreCanal.WhatsAppGrupo)
        {
            var groupJid = alerta.GrupoJid?.Trim();
            var groupLine = alerta.LineaId ?? conv.WhatsAppLineId;
            if (string.IsNullOrWhiteSpace(groupJid) || groupLine is not Guid glid)
            {
                RecordClose(conv, agentId, false, "WhatsApp (grupo)", groupJid ?? "(sin jid)",
                    "falta el jid del grupo o la linea de la conversacion.", null);
                return;
            }
            var okg = await _sender.SendWhatsAppGroupAsync(glid, groupJid!, BuildGroupText(tokenMap, summary), actor, ct);
            RecordClose(conv, agentId, okg, "WhatsApp (grupo)", groupJid!, okg ? null : "el proveedor no acepto el envio al grupo.", null);
            return;
        }

        // Telegram -> mensaje al chat_id via el bot del tenant.
        if (alerta.Canal == CierreCanal.Telegram)
        {
            var chatId = alerta.ChatId?.Trim();
            if (string.IsNullOrWhiteSpace(chatId))
            {
                RecordClose(conv, agentId, false, "Telegram", "(sin chat_id)", "no se configuro el chat_id.", null);
                return;
            }
            var okt = await _sender.SendTelegramAsync(chatId!, BuildGroupText(tokenMap, summary), ct);
            RecordClose(conv, agentId, okt, "Telegram", chatId!, okt ? null : "Telegram no acepto el envio (revisa el bot/chat_id).", null);
            return;
        }

        // Correo / WhatsApp (ADR-0163): destinatarios = el usuario elegido (Destino=Usuario) o el USUARIO ASIGNADO
        // de cada caso que el agente CREO o ANOTO en este turno. Sin casos tocados en el turno se cae al usuario
        // asignado de la LINEA (concepto heredado del backbone); si tampoco hay, no se notifica y queda como
        // informacion (no como error): no habia a quien avisar.
        var channel = ChannelLabel(alerta.Canal);
        if (alerta.Destino == CierreDestino.Usuario)
        {
            if (alerta.UsuarioId is not Guid fixedUser)
            {
                RecordClose(conv, agentId, false, channel, "(sin usuario)", "no se configuro el usuario destino.", null);
                return;
            }
            var firstTouched = await LoadTouchedTasksAsync(ct);
            await NotifyUserAsync(alerta, conv, agentId, fixedUser, firstTouched.FirstOrDefault(), tokenMap, summary, actor, ct);
            return;
        }

        var touched = await LoadTouchedTasksAsync(ct);
        if (touched.Count > 0)
        {
            foreach (var task in touched)
            {
                if (task.AssigneeTenantUserId is not Guid assignee)
                {
                    RecordClose(conv, agentId, false, channel, "(sin usuario)", "el caso no tiene usuario asignado.", null, task.Number);
                    continue;
                }
                await NotifyUserAsync(alerta, conv, agentId, assignee, task, tokenMap, summary, actor, ct);
            }
            return;
        }

        if (await ResolveAssignedUserIdAsync(conv, ct) is Guid lineOwner)
        {
            await NotifyUserAsync(alerta, conv, agentId, lineOwner, null, tokenMap, summary, actor, ct);
            return;
        }
        RecordClose(conv, agentId, true, channel, "(nadie)",
            "no se notifico: en este turno el agente no creo ni anoto un caso con usuario asignado.", null);
    }

    /// <summary>Casos creados o anotados por el agente en el turno (ADR-0163), en el orden en que se tocaron.</summary>
    private async Task<List<Domain.Entities.TaskItem>> LoadTouchedTasksAsync(CancellationToken ct)
    {
        var ids = AiToolRunContext.TouchedTaskIds;
        if (ids.Count == 0) { return new(); }
        var rows = await _db.TaskItems.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        return ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>Envia la alerta de cierre a UN usuario del tenant, con los tokens del caso (si lo hay). Un solo
    /// mensaje por usuario en el turno: si el aviso de tarea asignada u otra alerta ya le escribio, se omite.</summary>
    private async Task NotifyUserAsync(AgentCierreAlerta alerta, Domain.Entities.Conversation conv, Guid agentId, Guid uid,
        Domain.Entities.TaskItem? task, IReadOnlyDictionary<string, string> convTokens, string? summary, Guid actor, CancellationToken ct)
    {
        var caso = task?.Number;

        // De-dup (ADR-0161/0163): si el AVISO de tarea asignada (u otra alerta) ya notifico a este usuario en el
        // mismo turno, la alerta de cierre NO lo repite. Un solo mensaje al comercial.
        if (AiToolRunContext.WasCloseNotified(uid))
        {
            RecordClose(conv, agentId, true, ChannelLabel(alerta.Canal), "(asignado)",
                "omitido: ya se le notifico en este turno (de-dup de cierre).", null, caso);
            return;
        }

        var user = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new { u.Email, u.Phone })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            RecordClose(conv, agentId, false, ChannelLabel(alerta.Canal), "(usuario)", "el usuario destino no existe.", null, caso);
            return;
        }

        // Tokens: los del cierre (conversacion) + los del caso tocado ({tarea.*}, {usuario.*}, {tercero.*}...). Un
        // valor del caso solo pisa al de la conversacion si no viene vacio (p.ej. {cliente} sin solicitante).
        var tokenMap = task is null ? convTokens : await MergeTaskTokensAsync(convTokens, task, ct);

        switch (alerta.Canal)
        {
            case CierreCanal.Correo:
                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    RecordClose(conv, agentId, false, "Correo", user.Phone ?? "(usuario)", "el usuario no tiene correo.", null, caso);
                    return;
                }
                var subject = string.IsNullOrWhiteSpace(alerta.Asunto)
                    ? $"Cierre de atencion - {tokenMap.GetValueOrDefault("cliente", conv.ContactPhone)}"
                    : alerta.Asunto!;
                var oke = await _sender.SendEmailAsync(user.Email!, subject, BuildEmailHtml(tokenMap, summary), ct);
                if (oke) { AiToolRunContext.MarkCloseNotified(uid); }
                RecordClose(conv, agentId, oke, "Correo", user.Email!, oke ? null : "el correo no se pudo enviar.", null, caso);
                break;

            case CierreCanal.WhatsApp:
                if (string.IsNullOrWhiteSpace(user.Phone))
                {
                    RecordClose(conv, agentId, false, "WhatsApp", user.Email ?? "(usuario)", "el usuario no tiene telefono.", null, caso);
                    return;
                }
                if (string.IsNullOrWhiteSpace(alerta.Plantilla))
                {
                    RecordClose(conv, agentId, false, "WhatsApp", user.Phone!, "no se configuro la plantilla del aviso.", null, caso);
                    return;
                }
                var fromLine = alerta.LineaId ?? conv.WhatsAppLineId;
                if (fromLine is not Guid lineId)
                {
                    RecordClose(conv, agentId, false, "WhatsApp", user.Phone!, "no hay linea de WhatsApp (ni en la alerta ni en la conversacion).", null, caso);
                    return;
                }
                // Binding de variables: cada variable de la plantilla se resuelve con los tokens del cierre
                // (igual que el aviso de tarea y NodeNotifyService). Sin esto, las plantillas con variables
                // posicionales ({{1}},{{2}}...) salian vacias y el proveedor cortaba el envio en silencio.
                var waTokens = ApplyVariableBindings(alerta.Variables, tokenMap);
                var outcome = await _sender.SendWhatsAppTemplateAsync(lineId, user.Phone!, alerta.Plantilla!, alerta.Idioma, waTokens, actor, cancellationToken: ct);
                if (outcome.Ok) { AiToolRunContext.MarkCloseNotified(uid); }
                RecordClose(conv, agentId, outcome.Ok, "WhatsApp", user.Phone!, outcome.Ok ? null : outcome.Error, outcome.MessageId, caso);
                break;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> MergeTaskTokensAsync(
        IReadOnlyDictionary<string, string> convTokens, Domain.Entities.TaskItem task, CancellationToken ct)
    {
        var merged = new Dictionary<string, string>(convTokens, StringComparer.OrdinalIgnoreCase);
        var taskTokens = await _tokens.BuildAsync(task, ct);
        foreach (var (k, v) in taskTokens)
        {
            if (!string.IsNullOrWhiteSpace(v) || !merged.ContainsKey(k)) { merged[k] = v; }
        }
        return merged;
    }

    // Aplica el binding VARIABLE -> expresion (igual que AgentAvisoTareaService / NodeNotifyService): copia los
    // tokens y sobrescribe el nombre de cada variable (sin acentos, minuscula) con el valor de su expresion.
    private IReadOnlyDictionary<string, string> ApplyVariableBindings(
        IReadOnlyDictionary<string, string>? variables, IReadOnlyDictionary<string, string> tokens)
    {
        if (variables is not { Count: > 0 }) { return tokens; }
        var copy = new Dictionary<string, string>(tokens, StringComparer.OrdinalIgnoreCase);
        foreach (var (varName, expr) in variables)
        {
            if (string.IsNullOrWhiteSpace(varName) || string.IsNullOrWhiteSpace(expr)) { continue; }
            copy[StripAccents(varName.Trim().ToLowerInvariant())] = _tokens.Render(expr, tokens);
        }
        return copy;
    }

    // Registra el resultado de la alerta de cierre en la bitacora del agente (/bitacora-agente). Nada
    // silencioso: tanto el envio como el fallo (con su motivo) quedan visibles. ADR-0161.
    private void RecordClose(Domain.Entities.Conversation conv, Guid agentId, bool ok,
        string channel, string recipient, string? reason, string? messageId, string? caso = null)
    {
        // ADR-0163: el caso tocado va en el texto para que la bitacora diga DE QUE caso habla la alerta.
        var c = string.IsNullOrWhiteSpace(caso) ? "" : $" del caso {caso}";
        var text = ok
            ? (string.IsNullOrWhiteSpace(reason)
                ? $"Cierre{c}: se notifico por {channel} a {recipient}."
                : $"Cierre{c} por {channel} a {recipient}: {reason!.Trim()}")
            : $"Cierre{c}: NO se pudo notificar por {channel} a {recipient}. Motivo: {(string.IsNullOrWhiteSpace(reason) ? "sin detalle del proveedor." : reason!.Trim())}";

        _db.AiAgentRunLogs.Add(new Domain.Entities.AiAgentRunLog
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            AgentId = agentId,
            OccurredAt = _clock.GetUtcNow(),
            Kind = ok ? Domain.Enums.AiAgentRunLogKind.Info : Domain.Enums.AiAgentRunLogKind.Error,
            // ok + motivo = se omitio a proposito (de-dup o nadie a quien avisar): no es un envio ni un error.
            Title = !ok ? $"Alerta de cierre NO enviada - {channel}"
                : string.IsNullOrWhiteSpace(reason) ? $"Alerta de cierre enviada - {channel}"
                : $"Alerta de cierre omitida - {channel}",
            Content = SurrogateText.SafeTruncate(text, 4000)!,
            Response = string.IsNullOrWhiteSpace(messageId) ? null : messageId
        });
    }

    private static string ChannelLabel(CierreCanal canal) => canal switch
    {
        CierreCanal.WhatsApp => "WhatsApp",
        CierreCanal.Correo => "Correo",
        CierreCanal.WhatsAppGrupo => "WhatsApp (grupo)",
        CierreCanal.Telegram => "Telegram",
        _ => canal.ToString()
    };

    private static string StripAccents(string text)
    {
        var d = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task<Guid?> ResolveAssignedUserIdAsync(Domain.Entities.Conversation conv, CancellationToken ct)
    {
        if (conv.WhatsAppLineId is not Guid lineId) { return null; }
        return await _db.WhatsAppLines.AsNoTracking()
            .Where(l => l.Id == lineId)
            .Select(l => l.AssignedToTenantUserId)
            .FirstOrDefaultAsync(ct);
    }

    // ---- Composicion del contenido (especifica del Cierre) ----

    private static Dictionary<string, string> BuildTokenMap(string agentName, Domain.Entities.Conversation conv, string? summary)
    {
        var cliente = string.IsNullOrWhiteSpace(conv.ContactName) ? conv.ContactPhone : conv.ContactName!;
        var resumen = string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cliente"] = cliente,
            ["contacto"] = cliente,
            ["nombre"] = cliente,
            ["telefono"] = conv.ContactPhone,
            ["celular"] = conv.ContactPhone,
            ["whatsapp"] = conv.ContactPhone,
            ["agente"] = agentName,
            ["bot"] = agentName,
            ["resumen"] = resumen,
            ["detalle"] = resumen,
            ["mensaje"] = resumen,
            ["pedido"] = resumen,
            ["nota"] = resumen
        };
    }

    // Texto plano para el grupo de WhatsApp / Telegram (no usan plantilla HSM).
    private static string BuildGroupText(IReadOnlyDictionary<string, string> tokenMap, string? summary)
    {
        var cliente = tokenMap.GetValueOrDefault("cliente", "");
        var telefono = tokenMap.GetValueOrDefault("telefono", "");
        var agente = tokenMap.GetValueOrDefault("agente", "");
        var resumen = string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim();
        var sb = new StringBuilder();
        sb.Append("*Cierre de atencion*\n");
        sb.Append($"Cliente: {cliente} ({telefono})\n");
        sb.Append($"Agente: {agente}\n\n");
        sb.Append(resumen);
        return sb.ToString();
    }

    private static string BuildEmailHtml(IReadOnlyDictionary<string, string> tokenMap, string? summary)
    {
        var cliente = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("cliente", ""));
        var telefono = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("telefono", ""));
        var agente = System.Net.WebUtility.HtmlEncode(tokenMap.GetValueOrDefault("agente", ""));
        var resumen = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(summary) ? "Atencion cerrada." : summary!.Trim())
            .Replace("\n", "<br>");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:14px;color:#111;\">");
        sb.Append("<h2 style=\"margin:0 0 8px;font-size:16px;\">Cierre de atencion</h2>");
        sb.Append($"<p style=\"margin:2px 0;\"><b>Cliente:</b> {cliente} ({telefono})</p>");
        sb.Append($"<p style=\"margin:2px 0;\"><b>Agente:</b> {agente}</p>");
        sb.Append($"<div style=\"margin-top:10px;padding:10px 12px;background:#f6f7f9;border-radius:8px;\">{resumen}</div>");
        sb.Append("</div>");
        return sb.ToString();
    }
}
