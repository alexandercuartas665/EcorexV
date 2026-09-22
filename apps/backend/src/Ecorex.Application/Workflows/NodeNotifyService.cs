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
    private readonly Forms.IQuoteDocumentRenderer _quoteDoc;

    public NodeNotifyService(IApplicationDbContext db, INotifyTokenResolver tokens, INotificationChannelSender sender,
        INotifyLinkBuilder link, Forms.IQuoteDocumentRenderer quoteDoc)
    {
        _db = db;
        _tokens = tokens;
        _sender = sender;
        _link = link;
        _quoteDoc = quoteDoc;
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
                try { await DispatchRuleAsync(rule, task, step?.AssignedToTenantUserId, tokens, link, actorUserId, cancellationToken); }
                catch { /* un envio fallido no frena las demas reglas */ }
            }
        }
        catch
        {
            // Best-effort: una notificacion nunca debe romper el avance del flujo.
        }
    }

    private async Task DispatchRuleAsync(NodeNotifyRule rule, Domain.Entities.TaskItem? task, Guid? stepAssigneeId,
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

        // Correo / WhatsApp plantilla: resolver destinatario (correo + telefono). Puede ser el CONTACTO de la
        // tarea (el cliente) o un usuario del tenant (asignado del paso o uno elegido).
        string? email, phone;
        if (rule.Destino == NodeNotifyRecipient.ContactoTarea)
        {
            email = task?.RequesterEmail;
            phone = task?.RequesterPhone;
        }
        else
        {
            var userId = rule.Destino == NodeNotifyRecipient.Usuario ? rule.UsuarioId : stepAssigneeId;
            if (userId is not Guid uid) { return; }
            var user = await _db.TenantUsers.AsNoTracking()
                .Where(u => u.Id == uid)
                .Select(u => new { u.Email, u.Phone })
                .FirstOrDefaultAsync(ct);
            if (user is null) { return; }
            email = user.Email;
            phone = user.Phone;
        }

        switch (rule.Canal)
        {
            case NotifyChannel.Correo when !string.IsNullOrWhiteSpace(email):
                var subject = string.IsNullOrWhiteSpace(rule.Asunto)
                    ? "Notificacion de tarea"
                    : _tokens.Render(rule.Asunto, tokens);
                await _sender.SendEmailAsync(email!, subject, BuildEmailHtml(bodyWithLink), ct);
                break;

            case NotifyChannel.WhatsApp when !string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(rule.Plantilla) && rule.LineaId is Guid lineId:
                // La plantilla HSM llena sus variables por NOMBRE con el mapa de tokens. Si la regla ATA
                // variables a expresiones (rule.Variables), se resuelven aqui (tokens {tarea.x}/{form.x}/
                // {sistema.fecha}) y se inyectan bajo el nombre de la variable, para que el llenado por nombre
                // tome ese valor. Las variables sin binding conservan el llenado automatico (compat. atras).
                var waTokens = tokens;
                if (rule.Variables is { Count: > 0 })
                {
                    var copy = new Dictionary<string, string>(tokens, StringComparer.OrdinalIgnoreCase);
                    foreach (var (varToken, expr) in rule.Variables)
                    {
                        if (string.IsNullOrWhiteSpace(varToken) || string.IsNullOrWhiteSpace(expr)) { continue; }
                        copy[StripAccents(varToken.Trim().ToLowerInvariant())] = _tokens.Render(expr, tokens);
                    }
                    waTokens = copy;
                }
                await _sender.SendWhatsAppTemplateAsync(lineId, phone!, rule.Plantilla!, rule.Idioma, waTokens, actor, ct);

                // Adjunto: PDF de la respuesta de un formulario anclada a la tarea (renderizada con la plantilla
                // de impresion), enviado como documento al mismo destinatario. Best-effort.
                if (rule.AdjuntarPdfFormDefId is Guid formDefId && task is not null)
                {
                    await SendFormPdfAsync(lineId, phone!, task, formDefId, actor, ct);
                }
                break;
        }
    }

    // Resuelve la respuesta del formulario <paramref name="formDefId"/> anclada a la tarea (Reference == numero o
    // "numero-n"), la renderiza a PDF y la manda como documento. Silencioso si no hay respuesta o falla el render.
    private async Task SendFormPdfAsync(Guid lineId, string phone, Domain.Entities.TaskItem task, Guid formDefId, Guid actor, CancellationToken ct)
    {
        try
        {
            var num = task.Number;
            var responseId = await _db.FormResponses.AsNoTracking()
                .Where(r => r.IsActive && r.DefinitionId == formDefId
                    && (r.Reference == num || (r.Reference != null && r.Reference.StartsWith(num + "-"))))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (responseId is not Guid rid) { return; }

            var doc = await _quoteDoc.RenderResponsePdfAsync(rid, null, ct);
            if (doc is null) { return; }
            var base64 = Convert.ToBase64String(doc.Bytes);
            await _sender.SendWhatsAppDocumentAsync(lineId, phone, base64, doc.MimeType, doc.FileName, null, actor, ct);
        }
        catch { /* best-effort: el adjunto no debe romper la notificacion */ }
    }

    private static string AppendLink(string body, string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) { return body; }
        return string.IsNullOrWhiteSpace(body) ? link! : body + "\n\n" + link;
    }

    // Normaliza el nombre de una variable de plantilla igual que BuildTemplateParams (quita acentos) para que
    // el binding inyectado empareje con el llenado por nombre.
    private static string StripAccents(string text)
    {
        var d = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
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
