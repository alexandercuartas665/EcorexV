using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class ChatIngestService : IChatIngestService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly IAgentReplyQueue _agentQueue;
    private readonly TimeProvider _timeProvider;

    public ChatIngestService(IApplicationDbContext db, ISecretProtector secretProtector, IChatBroadcaster broadcaster, IAgentReplyQueue agentQueue, TimeProvider timeProvider)
    {
        _db = db;
        _secretProtector = secretProtector;
        _broadcaster = broadcaster;
        _agentQueue = agentQueue;
        _timeProvider = timeProvider;
    }

    public async Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken cancellationToken = default)
    {
        // Sin JWT: validamos el token del webhook contra la config Evolution del tenant.
        var config = await _db.TenantEvolutionConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null || string.IsNullOrEmpty(config.ApiTokenEncrypted)
            || string.IsNullOrEmpty(providedToken) || !TokenMatches(config.ApiTokenEncrypted, providedToken))
        {
            return ChatIngestResult.Unauthorized;
        }

        return await IngestTrustedAsync(tenantId, payload, cancellationToken: cancellationToken);
    }

    // Persiste un entrante ya autorizado por el llamador (webhook crudo de Evolution validado
    // con token global + instancia conocida). Mantiene idempotencia y difusion en tiempo real.
    public async Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, bool enqueueDispatch = true, CancellationToken cancellationToken = default)
    {
        // Idempotencia por id externo.
        var duplicate = await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ExternalId == payload.ExternalMessageId, cancellationToken);
        if (duplicate)
        {
            return ChatIngestResult.Duplicate;
        }

        var phone = payload.ContactPhone.Trim();
        var lineId = payload.WhatsAppLineId;
        // Conversacion por (tenant, linea, contacto): el mismo numero en dos lineas son hilos separados,
        // lo que separa la sesion de cache del agente y evita confundir clientes.
        var conversation = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.WhatsAppLineId == lineId && c.ContactPhone == phone, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var sentAt = payload.SentAt ?? now;

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = phone,
                RemoteJid = string.IsNullOrWhiteSpace(payload.RemoteJid) ? null : payload.RemoteJid,
                ContactName = payload.ContactName?.Trim(),
                WhatsAppLineId = lineId,
                LastMessageAt = sentAt
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = sentAt;
            if (conversation.WhatsAppLineId is null && lineId is not null) { conversation.WhatsAppLineId = lineId; }
            // Guardamos/actualizamos el jid completo (por si cambia o si la conversacion es vieja y no lo tenia):
            // es el destino real del envio saliente (imprescindible para contactos por LID).
            if (!string.IsNullOrWhiteSpace(payload.RemoteJid)) { conversation.RemoteJid = payload.RemoteJid; }
            if (conversation.ContactName is null && payload.ContactName is not null)
            {
                conversation.ContactName = payload.ContactName.Trim();
            }
        }

        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            ExternalId = payload.ExternalMessageId,
            Body = payload.Body,
            MessageType = string.IsNullOrWhiteSpace(payload.MessageType) ? "text" : payload.MessageType!.Trim(),
            MediaType = payload.MediaType,
            MediaUrl = payload.MediaUrl,
            MediaMimeType = payload.MediaMimeType,
            MediaFileName = string.IsNullOrWhiteSpace(payload.MediaFileName) ? null : payload.MediaFileName!.Trim(),
            SentAt = sentAt
        };
        _db.Messages.Add(message);

        // ADR-0092: si un PASO de flujo estaba EN ESPERA de una respuesta por esta conversacion, reanudarlo.
        // Se limpia AgentAttemptedAt (conservando PendingWhatsAppConversationId, que el agente lee para tener la
        // respuesta en su contexto) para que el barrido de agentes vuelva a correr el paso. Consulta acotada e
        // indexada por conversacion; el guard de colision en AgentConversationService evita la doble respuesta.
        // ADR-0122: si la conversacion fue "tomada por el flujo", la respuesta se enruta EXACTAMENTE al paso/nodo
        // que la tomo (FlowHoldStepId) -un solo dueno-, no a todos los pasos que casualmente la esperen. Sin
        // marca (holds viejos) se cae al comportamiento anterior por PendingWhatsAppConversationId.
        var waitingSteps = conversation.FlowHoldStepId is Guid ownerStepId
            ? await _db.WorkflowStepHistories.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && s.Id == ownerStepId && s.IsCurrent)
                .ToListAsync(cancellationToken)
            : await _db.WorkflowStepHistories.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && s.PendingWhatsAppConversationId == conversation.Id && s.IsCurrent)
                .ToListAsync(cancellationToken);
        foreach (var s in waitingSteps)
        {
            s.AgentAttemptedAt = null;
            s.AgentNextRetryAt = null;   // ADR-0121: el cliente respondio -> el reintento programado ya no hace falta.
        }

        // ADR-0120: si la conversacion esta "tomada por el flujo" (un paso vigente espera esta respuesta), el
        // arnes del flujo deja la respuesta del cliente en la BITACORA DEL AGENTE. Asi la bitacora queda completa
        // (el agente pregunto -> el cliente respondio -> el agente decide) y, cuando el agente conversacional
        // (SARA) retome esa linea al liberarse el paso, tiene el rastro. Atribuida al agente ligado a la linea.
        if (waitingSteps.Count > 0 && lineId is Guid heldLineId)
        {
            var boundAgentId = await _db.AiAgentLineBindings.AsNoTracking()
                .Where(b => b.WhatsAppLineId == heldLineId)
                .Select(b => (Guid?)b.AgentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (boundAgentId is Guid aid)
            {
                _db.AiAgentRunLogs.Add(new AiAgentRunLog
                {
                    TenantId = tenantId,
                    ConversationId = conversation.Id,
                    AgentId = aid,
                    OccurredAt = now,
                    Kind = AiAgentRunLogKind.Info,
                    Title = "El cliente respondio al flujo",
                    Content = string.IsNullOrWhiteSpace(payload.Body) ? "(mensaje sin texto)" : payload.Body
                });
            }
        }

        // ADR-0125: la MEDIA que el cliente envia DURANTE un hold de flujo (comprobante de pago, documento,
        // imagen) debe caer como ADJUNTO en la tarea que origino el flujo. El camino del flujo solo lee el texto
        // (WorkflowAgentContextBuilder) y no toca la media; este es el unico punto que tiene a la vez el inbound con
        // su MediaUrl recien creado y la senal de que un paso de flujo retiene la conversacion. Se inserta INLINE con
        // IgnoreQueryFilters: el webhook corre SIN tenant ambient (por eso todo el metodo ignora el filtro global), y
        // el AddAttachmentAsync publico filtra _db.TaskItems por tenant -> aqui no encontraria la tarea. De-dup por
        // (tarea, url) porque el barrido de agentes puede re-correr el paso.
        if (waitingSteps.Count > 0 && !string.IsNullOrWhiteSpace(message.MediaUrl))
        {
            var instanceIds = waitingSteps.Select(s => s.InstanceId).Distinct().ToList();
            var taskIds = await _db.WorkflowInstances.IgnoreQueryFilters()
                .Where(i => instanceIds.Contains(i.Id) && i.TaskItemId != null)
                .Select(i => i.TaskItemId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var taskId in taskIds)
            {
                var alreadyAttached = await _db.TaskItemAttachments.IgnoreQueryFilters()
                    .AnyAsync(a => a.TaskItemId == taskId && a.Url == message.MediaUrl, cancellationToken);
                if (alreadyAttached) { continue; }

                var fileName = !string.IsNullOrWhiteSpace(message.MediaFileName)
                    ? message.MediaFileName!
                    : FileNameFromUrl(message.MediaUrl!);
                _db.TaskItemAttachments.Add(new TaskItemAttachment
                {
                    TenantId = tenantId,
                    TaskItemId = taskId,
                    FileName = fileName,
                    Url = message.MediaUrl!,
                    MimeType = message.MediaMimeType,
                    SizeBytes = 0,
                    UploadedByName = "Cliente (flujo)"
                });
                _db.TaskItemActivities.Add(new TaskItemActivity
                {
                    TenantId = tenantId,
                    TaskItemId = taskId,
                    Type = TaskActivityType.Action,
                    ActorName = "Cliente (flujo)",
                    Text = $"adjunto el archivo {fileName} (enviado por WhatsApp durante el seguimiento)"
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body,
            message.MessageType, message.SentAt, message.MediaType, message.MediaUrl, message.MediaMimeType, message.SentByName);
        await _broadcaster.MessageAddedAsync(tenantId, conversation.Id, dto, cancellationToken);

        // Si la conversacion llego por una linea, encolamos la atencion del agente (el despachador
        // decide si hay un agente conectado a esa linea; aqui no bloqueamos el webhook).
        if (lineId is not null && enqueueDispatch)
        {
            _agentQueue.Schedule(tenantId, conversation.Id);
        }

        return ChatIngestResult.Accepted;
    }

    private bool TokenMatches(string encrypted, string provided)
    {
        string actual;
        try
        {
            actual = _secretProtector.Unprotect(encrypted);
        }
        catch
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(provided));
    }

    /// <summary>Nombre de archivo a partir de la URL de la media (ultimo segmento del path, sin query). "archivo" si no hay.</summary>
    private static string FileNameFromUrl(string url)
    {
        try
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs.AbsolutePath : url;
            var name = path.TrimEnd('/');
            var slash = name.LastIndexOf('/');
            if (slash >= 0) { name = name[(slash + 1)..]; }
            var q = name.IndexOf('?');
            if (q >= 0) { name = name[..q]; }
            return string.IsNullOrWhiteSpace(name) ? "archivo" : name;
        }
        catch
        {
            return "archivo";
        }
    }
}
