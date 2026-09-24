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
    private readonly IWorkflowDecisionLinkService _decisionLinks;

    public NodeNotifyService(IApplicationDbContext db, INotifyTokenResolver tokens, INotificationChannelSender sender,
        INotifyLinkBuilder link, Forms.IQuoteDocumentRenderer quoteDoc, IWorkflowDecisionLinkService decisionLinks)
    {
        _db = db;
        _tokens = tokens;
        _sender = sender;
        _link = link;
        _quoteDoc = quoteDoc;
        _decisionLinks = decisionLinks;
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
                try { await DispatchRuleAsync(rule, task, step?.AssignedToTenantUserId, tokens, link, actorUserId, stepId, cancellationToken); }
                catch { /* un envio fallido no frena las demas reglas */ }
            }
        }
        catch
        {
            // Best-effort: una notificacion nunca debe romper el avance del flujo.
        }
    }

    private async Task DispatchRuleAsync(NodeNotifyRule rule, Domain.Entities.TaskItem? task, Guid? stepAssigneeId,
        IReadOnlyDictionary<string, string> tokens, string? link, Guid actor, Guid stepId, CancellationToken ct)
    {
        // Enlaces publicos de decision del cliente que ESTA regla emite: se genera (o reusa) un token por
        // salida y su URL /d/{token} se inyecta bajo la variable configurada, para que el llenado por
        // nombre (plantilla WhatsApp) o el token {Variable} del cuerpo (correo/texto) tomen el enlace.
        if (rule.EnlacesDecision is { Count: > 0 })
        {
            var copy = new Dictionary<string, string>(tokens, StringComparer.OrdinalIgnoreCase);
            foreach (var d in rule.EnlacesDecision)
            {
                if (string.IsNullOrWhiteSpace(d.Variable)) { continue; }
                var url = await _decisionLinks.EnsureLinkAsync(stepId, d.TargetNodeId, d.Capture,
                    d.ObservationRequired, d.ButtonLabel, d.ExpiryHours, ct);
                if (!string.IsNullOrWhiteSpace(url)) { copy[d.Variable.Trim()] = url!; }
            }
            tokens = copy;
        }

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
                // Cotizacion adjunta (si la regla la pide): se resuelve/renderiza ANTES para decidir el envio.
                // En Evolution va COMBINADA (un solo mensaje = documento con el cuerpo de la plantilla como
                // caption) para no mandar "texto" + "documento" por separado. En otros proveedores se manda la
                // plantilla y luego el documento aparte (comportamiento anterior).
                Forms.QuoteDocument? cotDoc = null;
                if (rule.AdjuntarPdfFormDefId is Guid formDefId && task is not null)
                {
                    cotDoc = await ResolveFormPdfAsync(task, formDefId, rule.AdjuntarPdfTemplateId, ct);
                }
                var provider = await _db.WhatsAppLines.AsNoTracking()
                    .Where(l => l.Id == lineId).Select(l => (Domain.Enums.WhatsAppProvider?)l.Provider).FirstOrDefaultAsync(ct);

                if (cotDoc is not null && provider == Domain.Enums.WhatsAppProvider.Evolution)
                {
                    var b64 = Convert.ToBase64String(cotDoc.Bytes);
                    await _sender.SendWhatsAppTemplateAsync(lineId, phone!, rule.Plantilla!, rule.Idioma, waTokens, actor,
                        b64, cotDoc.MimeType, cotDoc.FileName, ct);
                }
                else
                {
                    await _sender.SendWhatsAppTemplateAsync(lineId, phone!, rule.Plantilla!, rule.Idioma, waTokens, actor, cancellationToken: ct);
                    if (cotDoc is not null)
                    {
                        var b64 = Convert.ToBase64String(cotDoc.Bytes);
                        await _sender.SendWhatsAppDocumentAsync(lineId, phone!, b64, cotDoc.MimeType, cotDoc.FileName, null, actor, ct);
                    }
                }

                // Contexto para el agente conversacional (SARA): si el mensaje fue al CONTACTO (cliente) con un
                // enlace de decision y/o un archivo, se deja una NOTA en su conversacion, para que si el cliente
                // responde, el agente sepa de que se trata (el envio del flujo antes no dejaba rastro en el hilo).
                if (rule.Destino == NodeNotifyRecipient.ContactoTarea && task is not null)
                {
                    try
                    {
                        await RecordContactShareObservationAsync(
                            task, lineId, phone!, rule.EnlacesDecision is { Count: > 0 }, cotDoc, ct);
                    }
                    catch { /* best-effort: la nota de contexto nunca debe romper la notificacion */ }
                }
                break;
        }
    }

    // Resuelve la respuesta del formulario <paramref name="formDefId"/> anclada a la tarea (Reference == numero o
    // "numero-n") y la renderiza a PDF. Devuelve null si no hay respuesta o falla el render (best-effort).
    private async Task<Forms.QuoteDocument?> ResolveFormPdfAsync(Domain.Entities.TaskItem task, Guid formDefId, Guid? templateId, CancellationToken ct)
    {
        try
        {
            // Respuesta del COT anclada a la tarea. IsActive (ADR-0065) es la marca EXCLUSIVA y OPCIONAL de
            // "formulario activo por defecto": si NINGUNA respuesta esta marcada, la tarea usa igual la
            // original (misma logica que la UI). Antes se exigia IsActive==true estricto, asi que una
            // cotizacion nunca marcada activa (el caso normal, una sola respuesta) NO se adjuntaba. Ahora se
            // prefiere la marcada activa y, si no hay, la original/mas antigua anclada (aunque sea borrador).
            var num = task.Number;
            var responseId = await _db.FormResponses.AsNoTracking()
                .Where(r => r.DefinitionId == formDefId && r.Reference != null
                    && (r.Reference == num || r.Reference.StartsWith(num + "-")))
                .OrderByDescending(r => r.IsActive)
                .ThenBy(r => r.CreatedAt)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (responseId is not Guid rid) { return null; }

            return await _quoteDoc.RenderResponsePdfAsync(rid, templateId, ct);
        }
        catch { return null; } // best-effort: el adjunto no debe romper la notificacion
    }

    /// <summary>
    /// Deja una NOTA en la conversacion de WhatsApp del contacto (misma clave (linea, telefono) que usa la
    /// ingesta de chat), para que el agente conversacional (SARA) tenga CONTEXTO si el cliente responde: sabe
    /// que se le envio un enlace de decision y/o un archivo, y por que proceso. Se guarda como saliente (nota
    /// simple): reusa la conversacion si existe o la crea. Best-effort; el llamador la envuelve en try/catch.
    /// </summary>
    private async Task RecordContactShareObservationAsync(
        Domain.Entities.TaskItem task, Guid lineId, string phone, bool hasDecisionLink,
        Forms.QuoteDocument? cotDoc, CancellationToken ct)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return; }

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.WhatsAppLineId == lineId && c.ContactPhone == digits, ct);
        var now = DateTimeOffset.UtcNow;
        if (conversation is null)
        {
            conversation = new Domain.Entities.Conversation
            {
                TenantId = task.TenantId,
                ContactPhone = digits,
                WhatsAppLineId = lineId,
                LastMessageAt = now
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = now;
        }

        var sb = new StringBuilder();
        sb.Append("Nota interna del flujo: se le envio a este contacto un mensaje de WhatsApp");
        if (hasDecisionLink) { sb.Append(" con un enlace de decision (el cliente puede responder por el enlace)"); }
        if (cotDoc is not null)
        {
            sb.Append(hasDecisionLink ? " y" : " con");
            sb.Append($" un archivo adjunto (cotizacion: {cotDoc.FileName})");
        }
        sb.Append($", en el proceso {task.Number}");
        if (!string.IsNullOrWhiteSpace(task.Title)) { sb.Append($" - {task.Title}"); }
        sb.Append(". Si el cliente escribe, es en respuesta a esto.");

        _db.Messages.Add(new Domain.Entities.Message
        {
            TenantId = task.TenantId,
            ConversationId = conversation.Id,
            Direction = Domain.Enums.MessageDirection.Outbound,
            Body = sb.ToString(),
            MessageType = "text",
            SentByName = "Sistema (flujo)",
            SentAt = now
        });
        await _db.SaveChangesAsync(ct);
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
