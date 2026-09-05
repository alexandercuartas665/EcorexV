using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion real de <see cref="IWorkflowAgentWhatsApp"/> (ADR-0092). Corre tenant-scoped (el barrido de
/// agentes fija el tenant ambiente). Resuelve/crea la conversacion de <c>(tenant, linea, telefono)</c>, decide
/// plantilla vs texto libre segun la ventana de 24h, envia por el conector y persiste el saliente. El TenantId
/// se asigna a mano en las entidades creadas (convencion del codebase, no hay interceptor de tenant).
/// </summary>
public sealed class WorkflowAgentWhatsApp : IWorkflowAgentWhatsApp
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _connector;
    private readonly TimeProvider _clock;

    /// <summary>Ventana de servicio de WhatsApp: dentro de 24h desde el ultimo entrante se puede enviar texto
    /// libre; fuera de ella Meta exige plantilla aprobada.</summary>
    private static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

    public WorkflowAgentWhatsApp(IApplicationDbContext db, IWhatsAppConnectorService connector, TimeProvider clock)
    {
        _db = db;
        _connector = connector;
        _clock = clock;
    }

    public async Task<WhatsAppAskResult> AskAsync(WhatsAppAskCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Phone))
        {
            return WhatsAppAskResult.Fail("El agente no indico un numero de WhatsApp.");
        }
        if (string.IsNullOrWhiteSpace(command.Question))
        {
            return WhatsAppAskResult.Fail("El agente no indico que preguntar por WhatsApp.");
        }

        var phone = new string(command.Phone.Where(char.IsDigit).ToArray());
        if (phone.Length == 0)
        {
            return WhatsAppAskResult.Fail("El numero de WhatsApp no tiene digitos validos.");
        }

        // Conversacion por (tenant, linea, telefono): la misma clave con la que el webhook de entrada
        // correlaciona la respuesta, para no abrir un hilo aparte.
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.WhatsAppLineId == command.LineId && c.ContactPhone == phone, cancellationToken);

        var now = _clock.GetUtcNow();

        // Ventana de 24h: hay un entrante reciente en esta conversacion?
        var windowOpen = false;
        if (conversation is not null)
        {
            var since = now - ServiceWindow;
            windowOpen = await _db.Messages.AnyAsync(
                m => m.ConversationId == conversation.Id && m.Direction == MessageDirection.Inbound && m.SentAt >= since,
                cancellationToken);
        }

        // Enviar: dentro de la ventana -> texto libre; fuera -> plantilla (si esta configurada) o error legible.
        LineSendResult send;
        if (windowOpen)
        {
            send = await _connector.SendTestAsync(command.LineId, phone, command.Question.Trim(), actorUserId: Guid.Empty,
                remoteJid: conversation?.RemoteJid, cancellationToken: cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(command.TemplateName))
        {
            var lang = string.IsNullOrWhiteSpace(command.TemplateLang) ? "es" : command.TemplateLang!.Trim();
            send = await _connector.SendTemplateAsync(command.LineId, phone, command.TemplateName!.Trim(), lang,
                new[] { command.Question.Trim() }, actorUserId: Guid.Empty, cancellationToken: cancellationToken);
        }
        else
        {
            return WhatsAppAskResult.Fail(
                "No hay una plantilla configurada y la ventana de 24h de WhatsApp esta cerrada: "
                    + "no se puede iniciar la conversacion en frio.");
        }

        if (!send.Ok)
        {
            return WhatsAppAskResult.Fail($"No se pudo enviar el WhatsApp: {send.Error}");
        }

        // Persistir la conversacion (si es nueva) y el saliente, para que la bitacora y la reanudacion tengan
        // rastro. Si la conversacion es nueva, su Id ya esta generado (Guid v7 al construir la entidad).
        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = command.TenantId,
                ContactPhone = phone,
                WhatsAppLineId = command.LineId,
                LastMessageAt = now
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = now;
        }

        _db.Messages.Add(new Message
        {
            TenantId = command.TenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            ExternalId = send.MessageId,
            Body = command.Question.Trim(),
            MessageType = "text",
            SentByName = "Agente de IA",
            SentAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return WhatsAppAskResult.Ok(conversation.Id);
    }
}
