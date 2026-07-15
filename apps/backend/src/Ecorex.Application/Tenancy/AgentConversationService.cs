using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Orquesta la atencion REAL de un agente de IA sobre una conversacion de WhatsApp (linea + contacto).
/// Reconstruye la conversacion como turnos, ejecuta al agente (con sus herramientas de agenda), persiste
/// la bitacora de atencion y envia la respuesta por la linea. La sesion de cache es la conversacion, asi
/// que los datos de un cliente nunca se mezclan con los de otro. Se ejecuta en un scope con el TenantId
/// fijado por el despachador (atencion disparada por webhook, sin usuario autenticado).
/// </summary>
public interface IAgentConversationService
{
    Task RunAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

public sealed class AgentConversationService : IAgentConversationService
{
    private readonly IApplicationDbContext _db;
    private readonly IAiInferenceService _inference;
    private readonly IChatService _chat;
    private readonly IAgentAssetReader _assets;

    // Cuantos mensajes recientes se reconstruyen como contexto del agente.
    private const int MaxTurns = 30;

    // Pausa breve entre adjuntos consecutivos para preservar el ORDEN de llegada en WhatsApp
    // (el proveedor puede reordenar envios muy seguidos). Modesta a proposito: el despachador
    // procesa las conversaciones en un bucle secuencial, asi que un delay grande frenaria a las demas.
    private const int InterAttachmentDelayMs = 1500;

    public AgentConversationService(IApplicationDbContext db, IAiInferenceService inference, IChatService chat, IAgentAssetReader assets)
    {
        _db = db;
        _inference = inference;
        _chat = chat;
        _assets = assets;
    }

    public async Task RunAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        // El TenantId lo fija el despachador en el scope; el query filter resuelve el tenant correcto.
        var conv = await _db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv?.WhatsAppLineId is not Guid lineId) { return; }

        // Hay un agente conectado a esta linea?
        var binding = await _db.AiAgentLineBindings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.WhatsAppLineId == lineId && b.IsConnected, cancellationToken);
        if (binding is null) { return; }

        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == binding.AgentId && a.IsActive, cancellationToken);
        if (agent is null) { return; }

        // LISTA NEGRA: si el numero del contacto esta bloqueado por el tenant, ningun agente lo atiende.
        var blockedPhones = await _db.TenantBlockedNumbers.AsNoTracking()
            .Select(b => b.Phone).ToListAsync(cancellationToken);
        if (AgentControlCommands.IsBlocked(conv.ContactPhone, blockedPhones)) { return; }

        // ASESOR HUMANO: si el lead de esta conversacion esta asignado a una persona y sigue ACTIVO
        // (no archivado), el agente se calla y deja que el asesor humano atienda. Si el lead esta
        // archivado, el silencio NO aplica: el agente RETOMA la conversacion al entrar un mensaje nuevo.
        if (conv.LeadId is Guid leadId)
        {
            var lead = await _db.Leads.AsNoTracking()
                .Where(l => l.Id == leadId)
                .Select(l => new { l.AssignedToTenantUserId, l.ArchivedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (lead is { AssignedToTenantUserId: not null, ArchivedAt: null }) { return; }
        }

        // Reconstruimos la conversacion como turnos (lo mas reciente al final).
        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAt)
            .Take(MaxTurns)
            .ToListAsync(cancellationToken);
        messages.Reverse();
        if (messages.Count == 0) { return; }

        // Si el ultimo mensaje ya es nuestro (saliente), no hay nada nuevo que responder.
        if (messages[^1].Direction == MessageDirection.Outbound) { return; }

        var turns = messages.Select(m => new AiChatTurn(
            m.Direction == MessageDirection.Inbound ? "user" : "model",
            string.IsNullOrWhiteSpace(m.Body) ? (m.MediaType == MessageMediaType.None ? "(mensaje vacio)" : "(adjunto)") : m.Body))
            .ToList();

        // Actor del sistema (el agente actua de forma autonoma); la auditoria queda sin usuario humano.
        var actor = Guid.Empty;

        var result = await _inference.RespondAsync(agent.Id, conversationId, turns, binding.AutoConfirm, actor, cancellationToken);

        // Bitacora: mensaje recibido + prompts/herramientas + respuesta.
        await LogAsync(conv.TenantId, conversationId, agent.Id, AiAgentRunLogKind.Inbound,
            "Mensaje recibido", messages[^1].Body, null, cancellationToken);

        if (result.DebugPrompts is not null)
        {
            foreach (var d in result.DebugPrompts)
            {
                var kind = d.Title.StartsWith("Herramienta", StringComparison.OrdinalIgnoreCase)
                    || d.Title.StartsWith("IA solicito", StringComparison.OrdinalIgnoreCase)
                    ? AiAgentRunLogKind.Tool : AiAgentRunLogKind.Prompt;
                await LogAsync(conv.TenantId, conversationId, agent.Id, kind, d.Title, d.Content, d.Response, cancellationToken);
            }
        }

        if (!result.Ok)
        {
            await LogAsync(conv.TenantId, conversationId, agent.Id, AiAgentRunLogKind.Error,
                "El agente no respondio", result.Error, null, cancellationToken);
            return;
        }

        // Enviar la respuesta por la linea (persiste el saliente y lo difunde).
        // Si el envio a WhatsApp falla, lo registramos en la bitacora (antes fallaba en SILENCIO:
        // el agente respondia pero el mensaje no llegaba al WhatsApp y no quedaba rastro del motivo).
        var textFailed = false;
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            var sent = await _chat.SendViaLineAsync(conversationId, lineId, result.Text!, actor, cancellationToken);
            if (!sent.Ok)
            {
                textFailed = true;
                await LogAsync(conv.TenantId, conversationId, agent.Id, AiAgentRunLogKind.Error,
                    "No se pudo enviar la respuesta al WhatsApp", sent.Error, null, cancellationToken);
            }
        }

        if (result.Attachments is { Count: > 0 })
        {
            // Los adjuntos que no se pudieron entregar se registran en la bitacora (antes fallaban en silencio).
            var failures = new List<string>();
            for (var i = 0; i < result.Attachments.Count; i++)
            {
                // Pausa breve entre adjuntos consecutivos para preservar el orden de llegada en WhatsApp.
                if (i > 0) { await Task.Delay(InterAttachmentDelayMs, cancellationToken); }
                var err = await SendAttachmentAsync(conversationId, lineId, result.Attachments[i], actor, cancellationToken);
                if (err is not null) { failures.Add(err); }
            }
            if (failures.Count > 0)
            {
                await LogAsync(conv.TenantId, conversationId, agent.Id, AiAgentRunLogKind.Error,
                    "No se pudieron enviar algunos adjuntos", string.Join("\n", failures), null, cancellationToken);
            }
        }

        // Solo marcamos "Respuesta enviada" si el texto SI salio (si fallo, ya quedo el Error arriba y
        // seria enganoso decir "enviada"). Sin texto (solo adjuntos) tambien se registra.
        if (!textFailed)
        {
            await LogAsync(conv.TenantId, conversationId, agent.Id, AiAgentRunLogKind.Reply,
                "Respuesta enviada", result.Text, AttachmentSummary(result.Attachments), cancellationToken);
        }
    }

    // Devuelve null si el adjunto se envio bien, o un mensaje de error para la bitacora si fallo.
    private async Task<string?> SendAttachmentAsync(Guid conversationId, Guid lineId, AiChatAttachment a, Guid actor, CancellationToken ct)
    {
        switch (a.ResourceType)
        {
            case AgentResourceType.Text:
                if (!string.IsNullOrWhiteSpace(a.Detail)) { await _chat.SendViaLineAsync(conversationId, lineId, a.Detail!, actor, ct); }
                return null;
            case AgentResourceType.Location:
                var parts = (a.Detail ?? "").Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                {
                    await _chat.SendLocationViaLineAsync(conversationId, lineId, lat, lng, a.Name, actor, ct);
                    return null;
                }
                return $"El recurso de ubicacion \"{a.Name}\" no tiene coordenadas validas (esperado \"lat,lng\").";
            default:
                var b64 = await _assets.ReadBase64Async(a.FileUrl, ct);
                if (string.IsNullOrEmpty(b64))
                {
                    return $"El recurso \"{a.Name}\" no se pudo leer (archivo ausente o ilegible: {a.FileUrl ?? "sin ruta"}).";
                }
                var mt = a.ResourceType switch
                {
                    AgentResourceType.Image => MessageMediaType.Image,
                    AgentResourceType.Video => MessageMediaType.Video,
                    AgentResourceType.Audio => MessageMediaType.Audio,
                    _ => MessageMediaType.Document
                };
                var send = await _chat.SendMediaViaLineAsync(conversationId, lineId, mt, b64!, a.FileUrl ?? "", null, a.FileName, a.Detail, actor, ct);
                if (send.Ok) { return null; }
                // Pista comun: WhatsApp no admite SVG/GIF como imagen; solo JPEG o PNG.
                var hint = mt == MessageMediaType.Image && IsUnsupportedImage(a.FileUrl)
                    ? " WhatsApp no admite imagenes SVG/GIF; usa JPEG o PNG."
                    : string.Empty;
                return $"Evolution rechazo el recurso \"{a.Name}\" ({mt}). Detalle: {send.Error}.{hint}";
        }
    }

    // Formatos que WhatsApp no muestra como imagen (Baileys falla al procesarlos).
    private static bool IsUnsupportedImage(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl)) { return false; }
        var ext = System.IO.Path.GetExtension(fileUrl).ToLowerInvariant();
        return ext is ".svg" or ".gif" or ".webp" or ".bmp" or ".tiff" or ".tif";
    }

    private async Task LogAsync(Guid tenantId, Guid conversationId, Guid agentId, AiAgentRunLogKind kind, string title, string? content, string? response, CancellationToken ct)
    {
        _db.AiAgentRunLogs.Add(new AiAgentRunLog
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            AgentId = agentId,
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = kind,
            Title = title.Length > 300 ? title[..300] : title,
            Content = content,
            Response = response
        });
        await _db.SaveChangesAsync(ct);
    }

    private static string? AttachmentSummary(IReadOnlyList<AiChatAttachment>? attachments)
        => attachments is { Count: > 0 } ? "Recursos: " + string.Join(", ", attachments.Select(a => $"{a.Name} ({a.ResourceType})")) : null;
}
