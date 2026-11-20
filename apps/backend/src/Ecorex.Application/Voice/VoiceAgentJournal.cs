using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Voice;

/// <summary>
/// Ola 4 (ADR-0190, opcion A): lleva la llamada de voz a la MISMA bitacora del agente que ya usa WhatsApp
/// (<see cref="AiAgentRunLog"/>), para que la voz no sea una caja negra frente a SARA ni frente al operador.
/// Escribe en DOS momentos (colocar y resultado) y por los DOS caminos de resultado (webhook y sondeo get-call);
/// las entradas cuelgan del <see cref="VoiceCall"/> (ConversationId null, VoiceCallId seteado: restriccion de BD).
///
/// Ademas, al tener resultado, deja un RESUMEN como <c>Message</c> en la conversacion de WhatsApp del MISMO
/// contacto (si existe) para que SARA lo ingiera como contexto (SARA solo lee Messages, no la bitacora). El
/// transcript COMPLETO queda en <see cref="VoiceCall.TranscriptText"/> y en la bitacora, no en cada Message.
///
/// Todo corre tenant-scoped (el llamador ya fijo el tenant ambiente). Best-effort: no escribe si la llamada no
/// tiene agente ECOREX atribuible (<see cref="VoiceCall.AiAgentId"/> null), igual que el saliente de WhatsApp
/// no registra bitacora sin agente ligado a la linea.
/// </summary>
public static class VoiceAgentJournal
{
    /// <summary>Enmascara el numero dejando visible solo el sufijo (ultimos 4), p.ej. "+57*****4567".</summary>
    public static string MaskPhone(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) { return "(sin numero)"; }
        var s = number.Trim();
        if (s.Length <= 4) { return s; }
        var tail = s[^4..];
        var head = s[0] == '+' ? "+" : "";
        return head + new string('*', Math.Max(1, s.Length - 4 - head.Length)) + tail;
    }

    /// <summary>Registra en la bitacora que el flujo COLOCO una llamada de voz (Kind=Info). Se encola en el mismo
    /// contexto; el <c>SaveChanges</c> lo hace el llamador (mismo que persiste la <see cref="VoiceCall"/>).</summary>
    public static void LogPlaced(IApplicationDbContext db, VoiceCall vc, DateTimeOffset now)
    {
        if (vc.AiAgentId is not Guid agentId) { return; }
        db.AiAgentRunLogs.Add(new AiAgentRunLog
        {
            TenantId = vc.TenantId,
            ConversationId = null,
            VoiceCallId = vc.Id,
            AgentId = agentId,
            OccurredAt = now,
            Kind = AiAgentRunLogKind.Info,
            Title = "El flujo coloco una llamada de voz",
            Content = $"Objetivo: {vc.Objetivo ?? "(sin objetivo)"}\nNumero: {MaskPhone(vc.ToNumber)}"
        });
    }

    /// <summary>Al tener RESULTADO de la llamada (webhook call_ended/analyzed o sondeo get-call), escribe UNA vez
    /// la entrada de resultado en la bitacora (Kind=Reply si hubo contacto, Kind=Error si no) y deja un resumen
    /// como <c>Message</c> en la conversacion de WhatsApp del contacto (si existe) para SARA. Idempotente: si ya
    /// hay una entrada de resultado para esta llamada, no duplica (cubre webhook + sondeo y ended + analyzed). El
    /// <c>SaveChanges</c> lo hace el llamador.</summary>
    public static async Task WriteResultAsync(IApplicationDbContext db, VoiceCall vc, DateTimeOffset now, CancellationToken ct)
    {
        if (vc.AiAgentId is not Guid agentId) { return; }

        // Idempotencia: una sola entrada de resultado por llamada, venga por webhook o por sondeo, en ended o analyzed.
        var already = await db.AiAgentRunLogs.AsNoTracking()
            .AnyAsync(l => l.VoiceCallId == vc.Id
                && (l.Kind == AiAgentRunLogKind.Reply || l.Kind == AiAgentRunLogKind.Error), ct);
        if (already) { return; }

        var noContact = RetellVoiceService.IsNoContactReason(vc.DisconnectionReason)
            || vc.Status is VoiceCallStatus.Failed or VoiceCallStatus.Error;
        var summary = RetellVoiceService.ExtractAnalysisSummary(vc.AnalysisJson);

        var header = new System.Text.StringBuilder();
        header.Append("Estado: ").Append(vc.Status.ToString());
        if (vc.DurationSeconds is int d) { header.Append("\nDuracion: ").Append(d).Append(" s"); }
        if (!string.IsNullOrWhiteSpace(vc.DisconnectionReason)) { header.Append("\nMotivo de corte (Retell): ").Append(vc.DisconnectionReason); }
        if (!string.IsNullOrWhiteSpace(summary)) { header.Append("\n\nResumen:\n").Append(summary); }
        if (noContact && string.IsNullOrWhiteSpace(summary)) { header.Append("\n\nNo hubo contacto con el cliente en esta llamada."); }

        db.AiAgentRunLogs.Add(new AiAgentRunLog
        {
            TenantId = vc.TenantId,
            ConversationId = null,
            VoiceCallId = vc.Id,
            AgentId = agentId,
            OccurredAt = now,
            Kind = noContact ? AiAgentRunLogKind.Error : AiAgentRunLogKind.Reply,
            Title = noContact ? "La llamada de voz no logro contacto" : "Resultado de la llamada de voz",
            Content = header.ToString(),
            // Transcript COMPLETO (clip defensivo) para auditar sin abrir Retell; el resumen va arriba y como Message.
            Response = Clip(vc.TranscriptText, 8000)
        });

        await AddTranscriptAsMessageAsync(db, vc, summary, noContact, now, ct);
    }

    /// <summary>Destino 2 del transcript (4.6): un RESUMEN como <c>Message</c> saliente en la conversacion de
    /// WhatsApp del contacto (misma clave telefono), para que SARA sepa en que estado va el dialogo. Si el
    /// seguimiento fue SOLO por voz y el contacto no tiene conversacion de WhatsApp, no hay donde colgarlo y se
    /// omite (el transcript completo igual queda en la bitacora y en la VoiceCall).</summary>
    private static async Task AddTranscriptAsMessageAsync(
        IApplicationDbContext db, VoiceCall vc, string? summary, bool noContact, DateTimeOffset now, CancellationToken ct)
    {
        var digits = DigitsSuffix(vc.ToNumber);
        if (digits.Length < 7) { return; }
        var conv = await db.Conversations
            .Where(c => c.ContactPhone == digits || c.ContactPhone.EndsWith(digits))
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);
        if (conv is null) { return; }   // sin conversacion de WhatsApp: nada que traspasar a SARA

        var body = new System.Text.StringBuilder("[Llamada de voz] ");
        if (noContact)
        {
            body.Append("No se logro contacto con el cliente");
            if (!string.IsNullOrWhiteSpace(vc.DisconnectionReason)) { body.Append(" (").Append(vc.DisconnectionReason).Append(')'); }
            body.Append('.');
        }
        else
        {
            body.Append(string.IsNullOrWhiteSpace(summary) ? "Llamada atendida por el cliente." : summary!.Trim());
        }

        conv.LastMessageAt = now;
        db.Messages.Add(new Message
        {
            TenantId = vc.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = Clip(body.ToString(), 1500)!,
            MessageType = "text",
            SentByName = "Llamada de voz",
            SentAt = now
        });
    }

    /// <summary>Solo digitos del numero; si tiene mas de 10, los ultimos 10 (empareja +57XXXXXXXXXX con el local).</summary>
    private static string DigitsSuffix(string? number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    private static string? Clip(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max] + "...";
}
