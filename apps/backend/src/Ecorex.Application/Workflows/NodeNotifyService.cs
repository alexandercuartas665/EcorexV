using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <inheritdoc />
public sealed class NodeNotifyService : INodeNotifyService
{
    private readonly IApplicationDbContext _db;
    private readonly INotifyTokenResolver _tokens;
    private readonly INotificationChannelSender _sender;
    private readonly INotifyLinkBuilder _link;

    public NodeNotifyService(IApplicationDbContext db, INotifyTokenResolver tokens, INotificationChannelSender sender, INotifyLinkBuilder link)
    {
        _db = db;
        _tokens = tokens;
        _sender = sender;
        _link = link;
    }

    public async Task NotifyStepArrivalAsync(Guid nodeId, Guid stepId, Guid? taskId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var notifyJson = await _db.WorkflowNodes.AsNoTracking()
                .Where(n => n.Id == nodeId)
                .Select(n => n.NotifyJson)
                .FirstOrDefaultAsync(cancellationToken);
            var config = NodeNotifyConfig.Parse(notifyJson);
            if (config.IsEmpty) { return; }

            var step = await _db.WorkflowStepHistories.AsNoTracking()
                .Where(s => s.Id == stepId)
                .Select(s => new { s.AssignedToTenantUserId })
                .FirstOrDefaultAsync(cancellationToken);

            Domain.Entities.TaskItem? task = null;
            if (taskId is Guid tid)
            {
                task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, cancellationToken);
            }

            var baseTokens = task is not null
                ? await _tokens.BuildAsync(task, cancellationToken)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var link = task is not null ? _link.BuildTaskLink(task.Id) : null;

            // El enlace a la tarea se expone TAMBIEN como variable de plantilla (por nombre): una plantilla HSM
            // con {{enlace}}/{{url}}/{{link}} recibe el deep-link (el canal de texto lo agrega aparte con IncluirEnlace).
            var tokens = new Dictionary<string, string>(baseTokens, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(link))
            {
                tokens["enlace"] = link!;
                tokens["url"] = link!;
                tokens["link"] = link!;
            }

            foreach (var rule in config.Reglas!)
            {
                try { await DispatchRuleAsync(rule, step?.AssignedToTenantUserId, tokens, link, actorUserId, cancellationToken); }
                catch { /* un envio fallido no frena las demas reglas */ }
            }
        }
        catch
        {
            // Best-effort: una notificacion nunca debe romper el avance del flujo.
        }
    }

    private async Task DispatchRuleAsync(NodeNotifyRule rule, Guid? stepAssigneeId,
        IReadOnlyDictionary<string, string> tokens, string? link, Guid actor, CancellationToken ct)
    {
        var body = _tokens.Render(rule.Mensaje, tokens);
        var bodyWithLink = AppendLink(body, rule.IncluirEnlace ? link : null);

        switch (rule.Canal)
        {
            case NotifyChannel.WhatsAppGrupo:
                if (string.IsNullOrWhiteSpace(rule.GrupoJid) || rule.LineaGrupoId is not Guid glid) { return; }
                await _sender.SendWhatsAppGroupAsync(glid, rule.GrupoJid!.Trim(), bodyWithLink, actor, ct);
                return;

            case NotifyChannel.Telegram:
                if (string.IsNullOrWhiteSpace(rule.ChatId)) { return; }
                await _sender.SendTelegramAsync(rule.ChatId!.Trim(), bodyWithLink, ct);
                return;
        }

        // Correo / WhatsApp plantilla: se dirigen a un usuario (asignado del paso o uno elegido).
        var userId = rule.Destino == NodeNotifyRecipient.Usuario ? rule.UsuarioId : stepAssigneeId;
        if (userId is not Guid uid) { return; }
        var user = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new { u.Email, u.Phone })
            .FirstOrDefaultAsync(ct);
        if (user is null) { return; }

        switch (rule.Canal)
        {
            case NotifyChannel.Correo when !string.IsNullOrWhiteSpace(user.Email):
                var subject = string.IsNullOrWhiteSpace(rule.Asunto)
                    ? "Notificacion de tarea"
                    : _tokens.Render(rule.Asunto, tokens);
                await _sender.SendEmailAsync(user.Email!, subject, BuildEmailHtml(bodyWithLink), ct);
                break;

            case NotifyChannel.WhatsApp when !string.IsNullOrWhiteSpace(user.Phone) && !string.IsNullOrWhiteSpace(rule.Plantilla) && rule.LineaId is Guid lineId:
                // La plantilla HSM es rigida: sus variables se llenan por NOMBRE con el mapa de tokens.
                await _sender.SendWhatsAppTemplateAsync(lineId, user.Phone!, rule.Plantilla!, rule.Idioma, tokens, actor, ct);
                break;
        }
    }

    private static string AppendLink(string body, string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) { return body; }
        return string.IsNullOrWhiteSpace(body) ? link! : body + "\n\n" + link;
    }

    private static string BuildEmailHtml(string body)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:14px;color:#111;\">");
        sb.Append(encoded);
        sb.Append("</div>");
        return sb.ToString();
    }
}
