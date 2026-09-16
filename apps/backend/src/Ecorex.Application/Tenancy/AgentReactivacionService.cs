using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <inheritdoc />
public sealed class AgentReactivacionService : IAgentReactivacionService
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _connector;
    private readonly INotificationChannelSender _sender;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    /// <summary>Ventana de servicio de WhatsApp (Meta): dentro de 24h desde el ultimo entrante del cliente se
    /// puede enviar texto libre; fuera de ella SOLO plantilla aprobada.</summary>
    private static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

    /// <summary>Tope de conversaciones a evaluar por agente y corrida (acota el costo del barrido).</summary>
    private const int MaxConversationsPerAgent = 500;

    public AgentReactivacionService(IApplicationDbContext db, IWhatsAppConnectorService connector,
        INotificationChannelSender sender, IAuditWriter audit, TimeProvider clock)
    {
        _db = db;
        _connector = connector;
        _sender = sender;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AgentReactivacionConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var json = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.ReactivacionJson)
            .FirstOrDefaultAsync(cancellationToken);
        return AgentReactivacionConfig.Parse(json);
    }

    public async Task<bool> SaveConfigAsync(Guid agentId, AgentReactivacionConfig config, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return false; }

        var previous = agent.ReactivacionJson;
        // Config vacia (deshabilitada o sin pasos) => null: mantiene limpio el campo y la compatibilidad.
        agent.ReactivacionJson = config.IsNoop ? null : config.Serialize();

        _audit.Write(actorUserId, "ai-agent.reactivacion-config", nameof(Domain.Entities.AiAgent), agent.Id,
            previousValue: new { hadConfig = previous is not null },
            newValue: new { config.Habilitada, pasos = config.Pasos?.Count ?? 0 },
            tenantId: agent.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RunTenantAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var sentCount = 0;

        // Agentes activos con reactivacion configurada (el filtro global ya acota al tenant ambiente).
        var agents = await _db.AiAgents.AsNoTracking()
            .Where(a => a.IsActive && a.ReactivacionJson != null)
            .Select(a => new { a.Id, a.Name, a.ReactivacionJson })
            .ToListAsync(cancellationToken);
        if (agents.Count == 0) { return 0; }

        // Lista negra del tenant (opt-out): ningun agente reactiva a un numero bloqueado.
        var blocked = await _db.TenantBlockedNumbers.AsNoTracking()
            .Select(b => b.Phone).ToListAsync(cancellationToken);

        foreach (var agent in agents)
        {
            var config = AgentReactivacionConfig.Parse(agent.ReactivacionJson);
            if (config.IsNoop) { continue; }
            var pasos = config.PasosActivos;   // habilitados, ordenados por offset

            // Lineas conectadas a este agente.
            var lineIds = await _db.AiAgentLineBindings.AsNoTracking()
                .Where(b => b.AgentId == agent.Id && b.IsConnected)
                .Select(b => (Guid?)b.WhatsAppLineId)
                .ToListAsync(cancellationToken);
            if (lineIds.Count == 0) { continue; }

            // Conversaciones activas de esas lineas (TRACKED: actualizamos su estado de reactivacion).
            var convs = await _db.Conversations
                .Where(c => c.ArchivedAt == null && lineIds.Contains(c.WhatsAppLineId))
                .OrderBy(c => c.LastMessageAt)
                .Take(MaxConversationsPerAgent)
                .ToListAsync(cancellationToken);

            foreach (var conv in convs)
            {
                if (cancellationToken.IsCancellationRequested) { break; }
                if (conv.WhatsAppLineId is not Guid lineId) { continue; }
                if (AgentControlCommands.IsBlocked(conv.ContactPhone, blocked)) { continue; }

                // Cierre / asesor humano: si el lead esta cerrado o lo atiende una persona, no reactivar.
                if (conv.LeadId is Guid leadId)
                {
                    var lead = await _db.Leads.AsNoTracking()
                        .Where(l => l.Id == leadId)
                        .Select(l => new { l.ArchivedAt, l.Status, l.AssignedToTenantUserId })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (lead is not null)
                    {
                        if (lead.ArchivedAt is not null || lead.Status != LeadStatus.Open) { continue; }
                        if (lead.AssignedToTenantUserId is not null) { continue; }   // asesor humano al mando
                    }
                }

                // Un paso de flujo espera la respuesta de esta conversacion: no interferir.
                var ownedByStep = await _db.WorkflowStepHistories.AsNoTracking()
                    .AnyAsync(s => s.PendingWhatsAppConversationId == conv.Id && s.IsCurrent, cancellationToken);
                if (ownedByStep) { continue; }

                // Ultimo entrante del cliente (posterior al reinicio de contexto, si lo hubo).
                var afterReset = conv.AgentContextResetAt;
                var lastInboundAt = await _db.Messages.AsNoTracking()
                    .Where(m => m.ConversationId == conv.Id && m.Direction == MessageDirection.Inbound
                        && (afterReset == null || m.SentAt > afterReset))
                    .Select(m => (DateTimeOffset?)m.SentAt)
                    .MaxAsync(cancellationToken);
                if (lastInboundAt is null) { continue; }   // sin entrante que revivir

                // Reinicio: si el cliente respondio DESPUES de nuestro ultimo seguimiento, la secuencia vuelve a 0.
                var paso = conv.ReactivacionUltimoPaso;
                if (conv.ReactivacionUltimoEnvioAt is DateTimeOffset lastSent && lastInboundAt > lastSent)
                {
                    paso = 0;
                    conv.ReactivacionUltimoPaso = 0;
                    conv.ReactivacionUltimoEnvioAt = null;
                }
                if (paso >= pasos.Count) { continue; }   // ya se enviaron todos los pasos; esperando respuesta

                var step = pasos[paso];
                var horas = (now - lastInboundAt.Value).TotalHours;
                if (horas < step.OffsetHoras) { continue; }   // aun no se cumple el offset

                var phone = Digits(conv.ContactPhone);
                if (phone.Length == 0) { continue; }

                // Regla de Meta: <=24h texto libre; >24h plantilla (o se OMITE el paso).
                if (horas <= ServiceWindow.TotalHours)
                {
                    if (string.IsNullOrWhiteSpace(step.MensajeTexto))
                    {
                        // Paso sin texto dentro de 24h: no hay nada que enviar. Se omite (avanza) y se registra.
                        AdvanceAndLog(conv, agent.Id, agent.Name, paso, now,
                            AiAgentRunLogKind.Info, $"Reactivacion paso {paso + 1} omitido", "Sin mensaje de texto para la ventana de 24h.");
                        continue;
                    }
                    var res = await _connector.SendTestAsync(lineId, phone, step.MensajeTexto!.Trim(),
                        actorUserId: Guid.Empty, remoteJid: conv.RemoteJid, cancellationToken: cancellationToken);
                    if (!res.Ok)
                    {
                        LogOnly(conv, agent.Id, AiAgentRunLogKind.Error, $"Reactivacion paso {paso + 1} fallo", res.Error);
                        continue;   // no avanza: se reintenta la proxima corrida
                    }
                    PersistSent(conv, agent.Id, agent.Name, paso, now, "text", step.MensajeTexto!.Trim(), res.MessageId);
                    sentCount++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(step.Plantilla))
                    {
                        // >24h sin plantilla: NUNCA texto libre. Se omite el paso (avanza) y se registra el motivo.
                        AdvanceAndLog(conv, agent.Id, agent.Name, paso, now,
                            AiAgentRunLogKind.Info, $"Reactivacion paso {paso + 1} omitido",
                            "Ventana de 24h cerrada y el paso no tiene plantilla aprobada (regla de Meta).");
                        continue;
                    }
                    var tokenMap = BuildTokenMap(agent.Name, conv);
                    var ok = await _sender.SendWhatsAppTemplateAsync(lineId, phone, step.Plantilla!.Trim(),
                        step.Idioma, tokenMap, actorUserId: Guid.Empty, cancellationToken: cancellationToken);
                    if (!ok)
                    {
                        LogOnly(conv, agent.Id, AiAgentRunLogKind.Error, $"Reactivacion paso {paso + 1} fallo",
                            $"No se pudo enviar la plantilla '{step.Plantilla!.Trim()}'.");
                        continue;   // no avanza: se reintenta
                    }
                    PersistSent(conv, agent.Id, agent.Name, paso, now, "template", $"[plantilla: {step.Plantilla!.Trim()}]", null);
                    sentCount++;
                }
                // Un solo envio por conversacion por corrida: seguimos con la siguiente conversacion.
            }
        }

        // Persistir todo (envios, avances/omisiones de paso, reinicios de estado y bitacora) en una sola vez.
        await _db.SaveChangesAsync(cancellationToken);
        return sentCount;
    }

    // Registra el saliente + avanza el estado de reactivacion + deja la bitacora del paso enviado.
    private void PersistSent(Conversation conv, Guid agentId, string agentName, int pasoIndex, DateTimeOffset now,
        string messageType, string body, string? externalId)
    {
        _db.Messages.Add(new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            ExternalId = externalId,
            Body = body,
            MessageType = messageType,
            SentByName = "Agente de IA (reactivacion)",
            SentAt = now
        });
        conv.LastMessageAt = now;
        conv.ReactivacionUltimoPaso = pasoIndex + 1;
        conv.ReactivacionUltimoEnvioAt = now;
        _db.AiAgentRunLogs.Add(new AiAgentRunLog
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            AgentId = agentId,
            OccurredAt = now,
            Kind = AiAgentRunLogKind.Reactivacion,
            Title = $"Reactivacion paso {pasoIndex + 1} enviado",
            Content = body
        });
    }

    // Omite un paso (lo marca como enviado sin mandar nada) y lo registra: la secuencia sigue avanzando.
    private void AdvanceAndLog(Conversation conv, Guid agentId, string agentName, int pasoIndex, DateTimeOffset now,
        AiAgentRunLogKind kind, string title, string? content)
    {
        conv.ReactivacionUltimoPaso = pasoIndex + 1;
        conv.ReactivacionUltimoEnvioAt = now;
        LogOnly(conv, agentId, kind, title, content);
    }

    private void LogOnly(Conversation conv, Guid agentId, AiAgentRunLogKind kind, string title, string? content)
    {
        _db.AiAgentRunLogs.Add(new AiAgentRunLog
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            AgentId = agentId,
            OccurredAt = _clock.GetUtcNow(),
            Kind = kind,
            Title = title,
            Content = content
        });
    }

    private static Dictionary<string, string> BuildTokenMap(string agentName, Conversation conv)
    {
        var cliente = string.IsNullOrWhiteSpace(conv.ContactName) ? conv.ContactPhone : conv.ContactName!;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cliente"] = cliente,
            ["contacto"] = cliente,
            ["nombre"] = cliente,
            ["telefono"] = conv.ContactPhone,
            ["celular"] = conv.ContactPhone,
            ["whatsapp"] = conv.ContactPhone,
            ["agente"] = agentName,
            ["bot"] = agentName
        };
    }

    private static string Digits(string? s) => string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());
}
