using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Tenancy;

/// <inheritdoc />
public sealed class AgentAvisoTareaService : IAgentAvisoTareaService
{
    private readonly IApplicationDbContext _db;
    private readonly INotificationChannelSender _sender;
    private readonly INotifyTokenResolver _tokens;
    private readonly INotifyLinkBuilder _link;
    private readonly ILogger<AgentAvisoTareaService> _logger;

    public AgentAvisoTareaService(IApplicationDbContext db, INotificationChannelSender sender,
        INotifyTokenResolver tokens, INotifyLinkBuilder link, ILogger<AgentAvisoTareaService> logger)
    {
        _db = db;
        _sender = sender;
        _tokens = tokens;
        _link = link;
        _logger = logger;
    }

    public async Task NotifyTaskAssignedAsync(Guid agentId, Guid taskId, Guid? conversationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var agent = await _db.AiAgents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Name, a.CierreJson })
                .FirstOrDefaultAsync(cancellationToken);
            if (agent is null) { return; }

            var config = AgentCierreConfig.Parse(agent.CierreJson);
            if (config.AvisosTarea is not { Count: > 0 } avisos) { return; }

            var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            if (task is null) { return; }

            // Tokens de la tarea + {usuario.*} (asignado) + {sistema.*}; el deep-link tambien como variable.
            var baseTokens = await _tokens.BuildAsync(task, cancellationToken);
            var tokens = new Dictionary<string, string>(baseTokens, StringComparer.OrdinalIgnoreCase);
            var link = _link.BuildTaskLink(task.Id);
            if (!string.IsNullOrWhiteSpace(link))
            {
                tokens["enlace"] = link!;
                tokens["url"] = link!;
                tokens["link"] = link!;
            }

            var any = false;
            foreach (var aviso in avisos)
            {
                try
                {
                    if (await DispatchAsync(aviso, agentId, agent.Name, task, conversationId, tokens, actorUserId, cancellationToken))
                    {
                        any = true;
                    }
                }
                catch (Exception ex)
                {
                    // Un aviso fallido no frena los demas ni la creacion de la tarea; el motivo queda en el log.
                    _logger.LogWarning(ex, "[AVISO-TAREA] fallo al procesar un aviso del agente {AgentId} para la tarea {TaskId}: {Motivo}",
                        agentId, taskId, ex.Message);
                }
            }

            if (any) { await _db.SaveChangesAsync(cancellationToken); }
        }
        catch (Exception ex)
        {
            // Best-effort: el aviso nunca debe romper la creacion de la actividad. Pero no es silencioso.
            _logger.LogWarning(ex, "[AVISO-TAREA] fallo GENERAL notificando la tarea {TaskId} (agente {AgentId}): {Motivo}",
                taskId, agentId, ex.Message);
        }
    }

    // Devuelve true si agrego algo a la bitacora (hay cambios por guardar).
    private async Task<bool> DispatchAsync(AgentCierreAlerta aviso, Guid agentId, string agentName,
        Domain.Entities.TaskItem task, Guid? conversationId, IReadOnlyDictionary<string, string> tokens,
        Guid actor, CancellationToken ct)
    {
        // Grupo de WhatsApp / Telegram: texto plano, no depende de un usuario destino.
        if (aviso.Canal == CierreCanal.WhatsAppGrupo)
        {
            if (string.IsNullOrWhiteSpace(aviso.GrupoJid) || aviso.LineaId is not Guid glid) { return false; }
            var ok = await _sender.SendWhatsAppGroupAsync(glid, aviso.GrupoJid!.Trim(), BuildPlainText(task, tokens), actor, ct);
            return Record(task, conversationId, agentId, ok, "WhatsApp (grupo)", aviso.GrupoJid!.Trim(), ok ? null : "el proveedor no acepto el envio al grupo.", null);
        }

        if (aviso.Canal == CierreCanal.Telegram)
        {
            if (string.IsNullOrWhiteSpace(aviso.ChatId)) { return false; }
            var ok = await _sender.SendTelegramAsync(aviso.ChatId!.Trim(), BuildPlainText(task, tokens), ct);
            return Record(task, conversationId, agentId, ok, "Telegram", aviso.ChatId!.Trim(), ok ? null : "Telegram no acepto el envio (revisa el bot/chat_id).", null);
        }

        // Correo / WhatsApp: destinatario = usuario de la tarea (asignado) o un usuario elegido.
        var userId = aviso.Destino == CierreDestino.Usuario ? aviso.UsuarioId : task.AssigneeTenantUserId;
        if (userId is not Guid uid)
        {
            return Record(task, conversationId, agentId, false, ChannelLabel(aviso.Canal), "(sin usuario)",
                aviso.Destino == CierreDestino.Usuario ? "no se configuro el usuario destino del aviso." : "la tarea no tiene usuario asignado.", null);
        }

        var user = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new { u.Email, u.Phone, Display = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .FirstOrDefaultAsync(ct);
        if (user is null) { return false; }
        var who = string.IsNullOrWhiteSpace(user.Display) ? user.Email : user.Display!;

        switch (aviso.Canal)
        {
            case CierreCanal.Correo:
                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    return Record(task, conversationId, agentId, false, "Correo", who, "el usuario no tiene correo.", null);
                }
                var subject = string.IsNullOrWhiteSpace(aviso.Asunto)
                    ? $"Tarea asignada - {task.Number}"
                    : _tokens.Render(aviso.Asunto, tokens);
                var ok = await _sender.SendEmailAsync(user.Email!, subject, BuildEmailHtml(task, tokens), ct);
                return Record(task, conversationId, agentId, ok, "Correo", $"{who} ({user.Email})", ok ? null : "el correo no se pudo enviar.", null);

            case CierreCanal.WhatsApp:
                if (string.IsNullOrWhiteSpace(user.Phone))
                {
                    return Record(task, conversationId, agentId, false, "WhatsApp", who, "el usuario no tiene telefono.", null);
                }
                if (string.IsNullOrWhiteSpace(aviso.Plantilla))
                {
                    return Record(task, conversationId, agentId, false, "WhatsApp", who, "no se configuro la plantilla del aviso.", null);
                }
                // Linea DESDE la que se envia: la configurada, o la ligada al agente.
                var lineId = aviso.LineaId ?? await ResolveAgentLineAsync(agentId, ct);
                if (lineId is not Guid lid)
                {
                    return Record(task, conversationId, agentId, false, "WhatsApp", who, "no hay linea de WhatsApp (ni en el aviso ni ligada al agente).", null);
                }
                // Binding de variables: la expresion de cada variable se resuelve con los tokens y se inyecta
                // bajo el nombre de la variable (normalizado), para que el llenado por nombre tome ese valor.
                var waTokens = ApplyVariableBindings(aviso.Variables, tokens);
                var outcome = await _sender.SendWhatsAppTemplateAsync(lid, user.Phone!, aviso.Plantilla!, aviso.Idioma, waTokens, actor, cancellationToken: ct);
                return Record(task, conversationId, agentId, outcome.Ok, "WhatsApp", $"{who} ({user.Phone})", outcome.Ok ? null : outcome.Error, outcome.MessageId);

            default:
                return false;
        }
    }

    // Linea de WhatsApp ligada al agente (la primera): origen por defecto del aviso cuando no se eligio una.
    private async Task<Guid?> ResolveAgentLineAsync(Guid agentId, CancellationToken ct)
        => await _db.AiAgentLineBindings.AsNoTracking()
            .Where(b => b.AgentId == agentId)
            .Select(b => (Guid?)b.WhatsAppLineId)
            .FirstOrDefaultAsync(ct);

    // Aplica el binding VARIABLE -> expresion (igual que NodeNotifyService): copia los tokens y sobrescribe el
    // nombre de cada variable (sin acentos, minuscula) con el valor renderizado de su expresion.
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

    // Registra el resultado del aviso en la historia de la tarea y en la bitacora del agente (si hay
    // conversacion). Nada silencioso: tanto el exito como el fallo (con su motivo) quedan visibles.
    private bool Record(Domain.Entities.TaskItem task, Guid? conversationId, Guid agentId, bool ok,
        string channel, string recipient, string? reason, string? messageId)
    {
        var now = DateTimeOffset.UtcNow;
        var text = ok
            ? $"Se notifico por {channel} la tarea {task.Number} a {recipient}."
            : $"No se pudo notificar por {channel} la tarea {task.Number} a {recipient}. Motivo: {(string.IsNullOrWhiteSpace(reason) ? "sin detalle del proveedor." : reason!.Trim())}";

        _db.TaskItemActivities.Add(new Domain.Entities.TaskItemActivity
        {
            TenantId = task.TenantId,
            TaskItemId = task.Id,
            Type = Domain.Enums.TaskActivityType.Action,
            ActorName = "Agente IA",
            Text = text.Length > 4000 ? text[..4000] : text
        });

        // La bitacora del agente (/bitacora-agente) exige ConversationId: solo se escribe si la tarea nacio de
        // una conversacion (el caso normal de crear_actividad). Sin conversacion, la historia de la tarea basta.
        if (conversationId is Guid convId)
        {
            _db.AiAgentRunLogs.Add(new Domain.Entities.AiAgentRunLog
            {
                TenantId = task.TenantId,
                ConversationId = convId,
                AgentId = agentId,
                OccurredAt = now,
                Kind = ok ? Domain.Enums.AiAgentRunLogKind.Info : Domain.Enums.AiAgentRunLogKind.Error,
                Title = ok ? $"Aviso de tarea asignada enviado - tarea {task.Number}" : $"Aviso de tarea asignada NO enviado - tarea {task.Number}",
                Content = text,
                Response = string.IsNullOrWhiteSpace(messageId) ? null : messageId
            });
        }
        return true;
    }

    private string BuildPlainText(Domain.Entities.TaskItem task, IReadOnlyDictionary<string, string> tokens)
    {
        var sb = new StringBuilder();
        sb.Append("*Tarea asignada*\n");
        sb.Append($"Tarea: {task.Number}");
        if (!string.IsNullOrWhiteSpace(task.Title)) { sb.Append($" - {task.Title}"); }
        sb.Append('.');
        var solicitante = tokens.GetValueOrDefault("tarea.solicitante", tokens.GetValueOrDefault("tarea.cliente", ""));
        if (!string.IsNullOrWhiteSpace(solicitante)) { sb.Append($"\nSolicitante: {solicitante}"); }
        var enlace = tokens.GetValueOrDefault("enlace", "");
        if (!string.IsNullOrWhiteSpace(enlace)) { sb.Append($"\n{enlace}"); }
        return sb.ToString();
    }

    private string BuildEmailHtml(Domain.Entities.TaskItem task, IReadOnlyDictionary<string, string> tokens)
    {
        string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        var solicitante = tokens.GetValueOrDefault("tarea.solicitante", tokens.GetValueOrDefault("tarea.cliente", ""));
        var enlace = tokens.GetValueOrDefault("enlace", "");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:14px;color:#111;\">");
        sb.Append("<h2 style=\"margin:0 0 8px;font-size:16px;\">Tarea asignada</h2>");
        sb.Append($"<p style=\"margin:2px 0;\"><b>Tarea:</b> {Enc(task.Number)} - {Enc(task.Title)}</p>");
        if (!string.IsNullOrWhiteSpace(solicitante)) { sb.Append($"<p style=\"margin:2px 0;\"><b>Solicitante:</b> {Enc(solicitante)}</p>"); }
        if (!string.IsNullOrWhiteSpace(task.Description)) { sb.Append($"<div style=\"margin-top:10px;padding:10px 12px;background:#f6f7f9;border-radius:8px;\">{Enc(task.Description).Replace("\n", "<br>")}</div>"); }
        if (!string.IsNullOrWhiteSpace(enlace)) { sb.Append($"<p style=\"margin-top:10px;\"><a href=\"{Enc(enlace)}\">Abrir la tarea</a></p>"); }
        sb.Append("</div>");
        return sb.ToString();
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
}
