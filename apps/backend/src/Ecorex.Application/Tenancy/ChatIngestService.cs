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

        var lineId = payload.WhatsAppLineId;
        // ADR-0125 v2 (normalizar al ingerir): la llave del hilo es el MSISDN canonico (misma regla que el envio,
        // PhoneNumber), para que el mismo contacto no se parta en dos hilos (p.ej. "573166578604" del entrante vs
        // "3166578604" que guardo el saliente del agente) y un soporte de pago caiga en el hilo equivocado. Se busca
        // por TODAS las formas equivalentes (canonica y nacional) y se toma el hilo MAS ACTIVO si existieran varios:
        // asi el trafico nuevo aterriza deterministamente en UN hilo SIN fusionar los hilos viejos ya partidos.
        var phone = Common.PhoneNumber.ToMsisdn(payload.ContactPhone);
        if (phone.Length == 0) { phone = payload.ContactPhone.Trim(); }
        var lookupForms = Common.PhoneNumber.LookupForms(payload.ContactPhone);
        // Conversacion por (tenant, linea, contacto): el mismo numero en dos lineas son hilos separados,
        // lo que separa la sesion de cache del agente y evita confundir clientes.
        var conversation = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.WhatsAppLineId == lineId && lookupForms.Contains(c.ContactPhone))
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(cancellationToken);

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

        // ADR-0125 (v2): la MEDIA que el cliente envia (comprobante de pago, documento, imagen) debe caer como
        // ADJUNTO en la tarea CORRECTA. El camino del flujo solo lee el texto (WorkflowAgentContextBuilder) y no toca
        // la media; este es el unico punto que tiene a la vez el inbound con su MediaUrl recien creado y el estado del
        // hilo. Se inserta INLINE con IgnoreQueryFilters: el webhook corre SIN tenant ambient (por eso todo el metodo
        // ignora el filtro global), y el AddAttachmentAsync publico filtra _db.TaskItems por tenant -> aqui no
        // encontraria la tarea. Reglas de ruteo deterministico (un soporte llega casi siempre FUERA del hold):
        //  1. Si un paso retiene el hilo (waitingSteps): a la(s) tarea(s) de esos pasos (comportamiento v1).
        //  2. Si no, a la TAREA DUEÑA del hilo (Conversation.CurrentTaskItemId) si sigue ABIERTA.
        //  3. Si no hay dueña abierta, a la UNICA tarea ABIERTA ligada al hilo; si hay varias, a la mas reciente
        //     (con nota de ambiguedad). NUNCA a una tarea Done/Closed: un negocio cerrado no absorbe un pago nuevo.
        //  4. Si no hay candidata abierta, el archivo queda en el chat marcado Pending = BANDEJA de soportes sin
        //     tarea, para que un humano lo archive (no se adivina en una tarea cerrada, no se pierde).
        // De-dup por (tarea, url) porque el barrido de agentes puede re-correr el paso.
        if (!string.IsNullOrWhiteSpace(message.MediaUrl))
        {
            // Adjunta el archivo a una tarea (de-dup por url) + bitacora; marca el mensaje como ruteado. Devuelve
            // true si quedo adjunto (o ya lo estaba). noteSuffix enriquece la linea de la bitacora.
            async Task<bool> AttachToTaskAsync(Guid taskId, string noteSuffix)
            {
                var alreadyAttached = await _db.TaskItemAttachments.IgnoreQueryFilters()
                    .AnyAsync(a => a.TaskItemId == taskId && a.Url == message.MediaUrl, cancellationToken);
                var fileName = !string.IsNullOrWhiteSpace(message.MediaFileName)
                    ? message.MediaFileName!
                    : FileNameFromUrl(message.MediaUrl!);
                if (!alreadyAttached)
                {
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
                        Text = $"adjunto el archivo {fileName} (enviado por WhatsApp{noteSuffix})"
                    });
                }
                message.SupportStatus = ClientSupportStatus.Routed;
                message.SupportTaskItemId = taskId;
                return true;
            }

            if (waitingSteps.Count > 0)
            {
                var instanceIds = waitingSteps.Select(s => s.InstanceId).Distinct().ToList();
                var taskIds = await _db.WorkflowInstances.IgnoreQueryFilters()
                    .Where(i => instanceIds.Contains(i.Id) && i.TaskItemId != null)
                    .Select(i => i.TaskItemId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var taskId in taskIds)
                {
                    await AttachToTaskAsync(taskId, " durante el seguimiento");
                }
            }
            else
            {
                // Un negocio CERRADO (Done/Closed) NUNCA debe absorber un soporte nuevo.
                // (2) Tarea DUEÑA del hilo, si sigue abierta.
                Guid? destino = null;
                if (conversation.CurrentTaskItemId is Guid ownerId)
                {
                    var ownerOpen = await _db.TaskItems.IgnoreQueryFilters()
                        .AnyAsync(t => t.Id == ownerId
                            && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Closed, cancellationToken);
                    if (ownerOpen) { destino = ownerId; }
                }

                // (3) Fallback: tarea(s) ABIERTA(s) ligada(s) al hilo (TaskItem.ConversationId). Una -> esa; varias
                //     -> la mas reciente (CreatedAt) con nota de ambiguedad.
                var ambiguas = false;
                if (destino is null)
                {
                    var abiertas = await _db.TaskItems.IgnoreQueryFilters()
                        .Where(t => t.ConversationId == conversation.Id
                            && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Closed)
                        .OrderByDescending(t => t.CreatedAt)
                        .Select(t => t.Id)
                        .ToListAsync(cancellationToken);
                    if (abiertas.Count >= 1)
                    {
                        destino = abiertas[0];
                        ambiguas = abiertas.Count > 1;
                    }
                }

                if (destino is Guid destinoId)
                {
                    await AttachToTaskAsync(destinoId, ambiguas
                        ? "; habia varias tareas abiertas para este contacto, se adjunto a la mas reciente (verificar)"
                        : "");
                }
                else
                {
                    // (4) Sin candidata abierta: a la BANDEJA de soportes sin tarea (no se pierde, no entra a cerradas).
                    message.SupportStatus = ClientSupportStatus.Pending;
                }
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
