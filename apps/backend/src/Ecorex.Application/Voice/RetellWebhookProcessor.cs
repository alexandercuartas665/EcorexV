using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Voice;

public enum RetellWebhookOutcome { Ok, BadRequest, NotFound, Unauthorized }

/// <summary>
/// Procesa un evento de webhook de Retell (call_started/ended/analyzed). El endpoint (SuperAdmin) resuelve el
/// tenant por call_id (cross-tenant, IgnoreQueryFilters) y ABRE el scope ambiente ANTES de llamar a
/// <see cref="ProcessAsync"/>; aqui todo corre tenant-scoped. La firma se verifica con la API key del tenant
/// (HMAC-SHA256, raw body). Si el objetivo era LlenarFormulario, vuelca los datos capturados a FormResponse
/// usando SOLO los formularios de la whitelist (jamas fuera de ella).
/// </summary>
public interface IRetellWebhookProcessor
{
    /// <summary>Lee call_id y event del raw body (sin validar firma). Para resolver el tenant.</summary>
    (string? Event, string? CallId) Peek(string rawBody);

    /// <summary>TenantId de la VoiceCall con ese call_id (cross-tenant). Null si no existe.</summary>
    Task<Guid?> ResolveTenantByCallIdAsync(string callId, CancellationToken cancellationToken = default);

    /// <summary>Procesa el evento (tenant ambiente ya fijado). Verifica firma y actualiza.</summary>
    Task<RetellWebhookOutcome> ProcessAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// RESPALDO del webhook (ADR-0190): aplica el objeto "call" leido con GET get-call (tenant ambiente ya fijado)
    /// con los MISMOS efectos que el webhook (estado, motivo de corte, volcado a formulario, reanudar el paso en
    /// espera, estado del run). No guarda: el llamador hace SaveChanges. Devuelve true si la llamada cambio de estado.
    /// </summary>
    Task<bool> ApplyPolledCallAsync(Domain.Entities.VoiceCall voiceCall, JsonElement call, CancellationToken cancellationToken = default);
}

public sealed class RetellWebhookProcessor : IRetellWebhookProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IFormResponseService _forms;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetellWebhookProcessor> _log;

    public RetellWebhookProcessor(IApplicationDbContext db, ISecretProtector protector, IFormResponseService forms,
        TimeProvider clock, ILogger<RetellWebhookProcessor> log)
    {
        _db = db;
        _protector = protector;
        _forms = forms;
        _clock = clock;
        _log = log;
    }

    public (string? Event, string? CallId) Peek(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var ev = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? callId = null;
            if (root.TryGetProperty("call", out var call) && call.ValueKind == JsonValueKind.Object
                && call.TryGetProperty("call_id", out var cid) && cid.ValueKind == JsonValueKind.String)
            {
                callId = cid.GetString();
            }
            return (ev, callId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public async Task<Guid?> ResolveTenantByCallIdAsync(string callId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callId)) { return null; }
        // Cross-tenant: solo para RESOLVER el tenant dueno; no expone datos sensibles.
        return await _db.VoiceCalls.IgnoreQueryFilters()
            .Where(v => v.CallId == callId)
            .Select(v => (Guid?)v.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RetellWebhookOutcome> ProcessAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        var (eventType, callId) = Peek(rawBody);
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(eventType))
        {
            return RetellWebhookOutcome.BadRequest;
        }

        var voiceCall = await _db.VoiceCalls.FirstOrDefaultAsync(v => v.CallId == callId, cancellationToken);
        if (voiceCall is null)
        {
            return RetellWebhookOutcome.NotFound;
        }

        // Firma: HMAC con la API key de la LINEA que coloco la llamada. Sin key valida no se verifica -> 401.
        var line = voiceCall.RetellVoiceLineId is Guid lineId
            ? await _db.RetellVoiceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            : null;
        string? apiKey = null;
        if (line is not null && !string.IsNullOrEmpty(line.RetellApiKeyEncrypted))
        {
            try { apiKey = _protector.Unprotect(line.RetellApiKeyEncrypted); } catch { apiKey = null; }
        }
        if (RetellSignatureVerifier.Verify(rawBody, apiKey, signatureHeader, _clock.GetUtcNow()) != RetellSignatureVerifier.Result.Valid)
        {
            _log.LogWarning("Voz IA: firma de webhook invalida para call_id={CallId}", callId);
            return RetellWebhookOutcome.Unauthorized;
        }

        // Actualizar la VoiceCall segun el evento + datos del payload.
        using var doc = JsonDocument.Parse(rawBody);
        var call = doc.RootElement.GetProperty("call");
        await ApplyWithEffectsAsync(voiceCall, eventType!, call, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _log.LogInformation("Voz IA: webhook {Event} aplicado call_id={CallId} status={Status}", eventType, callId, voiceCall.Status);
        return RetellWebhookOutcome.Ok;
    }

    public async Task<bool> ApplyPolledCallAsync(Domain.Entities.VoiceCall voiceCall, JsonElement call, CancellationToken cancellationToken = default)
    {
        var before = (voiceCall.Status, voiceCall.DisconnectionReason);
        var callStatus = ReadString(call, "call_status")?.ToLowerInvariant();
        var hasAnalysis = call.TryGetProperty("call_analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object;

        // GET get-call trae el estado ACTUAL: se reproduce como los eventos que el webhook habria mandado.
        if (callStatus is "ongoing" && voiceCall.Status == VoiceCallStatus.Registered)
        {
            await ApplyWithEffectsAsync(voiceCall, "call_started", call, cancellationToken);
        }
        else if (callStatus is "ended" or "not_connected" or "error")
        {
            if (voiceCall.Status is VoiceCallStatus.Registered or VoiceCallStatus.Ongoing)
            {
                voiceCall.StartedAt = ReadUnixMs(call, "start_timestamp") ?? voiceCall.StartedAt;
                await ApplyWithEffectsAsync(voiceCall, "call_ended", call, cancellationToken);
            }
            if (hasAnalysis && voiceCall.Status != VoiceCallStatus.Analyzed)
            {
                await ApplyWithEffectsAsync(voiceCall, "call_analyzed", call, cancellationToken);
            }
            if (callStatus == "error" && voiceCall.Status != VoiceCallStatus.Analyzed)
            {
                voiceCall.Status = VoiceCallStatus.Error;
                await SyncRunStatusAsync(voiceCall, cancellationToken);
            }
        }
        return before != (voiceCall.Status, voiceCall.DisconnectionReason);
    }

    /// <summary>Aplica un evento a la VoiceCall y sus efectos: volcado a formulario, paso de flujo en espera y run.</summary>
    private async Task ApplyWithEffectsAsync(Domain.Entities.VoiceCall voiceCall, string eventType, JsonElement call, CancellationToken cancellationToken)
    {
        var callId = voiceCall.CallId;

        // Orden (ADR-0190): el respaldo por get-call y un webhook tardio pueden llegar fuera de orden. Un evento
        // ANTERIOR al estado PREVIO (call_ended tardio sobre una ya analizada) se ignora: la llamada no retrocede.
        // OJO: NO se condiciona call_analyzed a "primera vez". El sondeo de la UI (RetellVoiceService
        // TryRefreshFromRetellAsync) puede dejar la llamada en Analyzed SIN efectos; el webhook real debe igual
        // volcar y reanudar. Los efectos ya son idempotentes: el borrador "voz:{callId}" se reusa, la bitacora
        // revisa su entrada y el runner pone PendingVoiceCallId = null al consumir el resultado.
        var incomingRank = EventRank(eventType);
        if (incomingRank >= 0 && incomingRank < StatusRank(voiceCall.Status)) { return; }
        var isAnalyzed = string.Equals(eventType, "call_analyzed", StringComparison.OrdinalIgnoreCase);

        ApplyEvent(voiceCall, eventType, call);

        // Volcado a formulario (solo si el objetivo era LlenarFormulario y hay whitelist). Las llamadas del motor de
        // acciones de contactos (ContactWorkflowRunId) vuelcan con cualquier objetivo: la whitelist decide (fase B).
        if (isAnalyzed
            && (string.Equals(voiceCall.Objetivo, nameof(ContactCallObjetivo.LlenarFormulario), StringComparison.OrdinalIgnoreCase)
                || voiceCall.ContactWorkflowRunId is not null))
        {
            await DumpCapturedFormsAsync(voiceCall, call, callId, cancellationToken);
        }

        // Ola 4 (ADR-0190): deja la entrada de RESULTADO en la bitacora del agente (y un resumen como Message para
        // SARA), idempotente. En 'call_analyzed' (trae resumen) o en 'call_ended' SIN contacto (no contesto / buzon
        // / ocupado / fallo: puede no llegar a 'analyzed'); un 'call_ended' CON contacto espera al 'analyzed'.
        var writeJournalResult =
            string.Equals(eventType, "call_analyzed", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(eventType, "call_ended", StringComparison.OrdinalIgnoreCase)
                && RetellVoiceService.IsNoContactReason(voiceCall.DisconnectionReason));
        if (writeJournalResult)
        {
            await VoiceAgentJournal.WriteResultAsync(_db, voiceCall, _clock.GetUtcNow(), cancellationToken);
        }

        // ADR-0091: si un PASO de flujo estaba EN ESPERA de esta llamada, reanudarlo. Se limpia
        // AgentAttemptedAt (conservando PendingVoiceCallId, que el agente lee para tener el resultado en su
        // contexto) para que el barrido de agentes vuelva a correr el paso con el transcript/datos capturados.
        if (isAnalyzed)
        {
            var waitingSteps = await _db.WorkflowStepHistories
                .Where(s => s.PendingVoiceCallId == callId && s.IsCurrent)
                .ToListAsync(cancellationToken);
            foreach (var s in waitingSteps)
            {
                s.AgentAttemptedAt = null;
            }
        }

        await SyncRunStatusAsync(voiceCall, cancellationToken);
    }

    /// <summary>
    /// Estado del run del motor de acciones (best-effort, por ExternalRef = call_id). Solo un FALLO real marca el
    /// run como fallido; "no contesto", "ocupado" o "buzon" lo dejan Enviado para que el reintento del paso
    /// (ADR-0190) decida si vuelve a llamar.
    /// </summary>
    private async Task SyncRunStatusAsync(Domain.Entities.VoiceCall voiceCall, CancellationToken cancellationToken)
    {
        var run = await _db.ContactWorkflowRuns.FirstOrDefaultAsync(r => r.ExternalRef == voiceCall.CallId, cancellationToken);
        if (run is null) { return; }
        var outcome = VoiceCallOutcomes.Classify(voiceCall.Status, voiceCall.DisconnectionReason);
        if (outcome == VoiceCallOutcome.Fallo
            && (voiceCall.Status is VoiceCallStatus.Error or VoiceCallStatus.Failed || voiceCall.DisconnectionReason is not null))
        {
            run.Status = ContactWorkflowRunStatus.Failed;
            run.Error = "Llamada: " + (VoiceCallOutcomes.ReasonText(voiceCall.DisconnectionReason) ?? "error del proveedor de voz");
        }
    }

    /// <summary>Orden de los eventos de Retell (-1 = desconocido, no se ordena).</summary>
    private static int EventRank(string eventType) => eventType.ToLowerInvariant() switch
    {
        "call_started" => 1,
        "call_ended" => 2,
        "call_analyzed" => 3,
        _ => -1
    };

    /// <summary>Orden del estado actual de la llamada; Error no bloquea eventos posteriores.</summary>
    private static int StatusRank(VoiceCallStatus status) => status switch
    {
        VoiceCallStatus.Ongoing => 1,
        VoiceCallStatus.Ended => 2,
        VoiceCallStatus.Analyzed => 3,
        _ => 0
    };

    private static void ApplyEvent(Domain.Entities.VoiceCall vc, string eventType, JsonElement call)
    {
        switch (eventType.ToLowerInvariant())
        {
            case "call_started":
                vc.Status = VoiceCallStatus.Ongoing;
                vc.StartedAt = ReadUnixMs(call, "start_timestamp") ?? vc.StartedAt;
                break;
            case "call_ended":
                vc.Status = VoiceCallStatus.Ended;
                vc.EndedAt = ReadUnixMs(call, "end_timestamp") ?? vc.EndedAt;
                vc.DurationSeconds = ReadDurationSeconds(call) ?? vc.DurationSeconds;
                vc.TranscriptText = ReadString(call, "transcript") ?? vc.TranscriptText;
                vc.RecordingUrl = ReadString(call, "recording_url") ?? vc.RecordingUrl;
                vc.DisconnectionReason = Truncate(ReadString(call, "disconnection_reason"), 80) ?? vc.DisconnectionReason;
                break;
            case "call_analyzed":
                vc.Status = VoiceCallStatus.Analyzed;
                vc.TranscriptText = ReadString(call, "transcript") ?? vc.TranscriptText;
                vc.RecordingUrl = ReadString(call, "recording_url") ?? vc.RecordingUrl;
                vc.DisconnectionReason = Truncate(ReadString(call, "disconnection_reason"), 80) ?? vc.DisconnectionReason;
                vc.EndedAt ??= ReadUnixMs(call, "end_timestamp");
                vc.DurationSeconds ??= ReadDurationSeconds(call);
                if (call.TryGetProperty("call_analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object)
                {
                    vc.AnalysisJson = analysis.GetRawText();
                }
                vc.CostUsd = ReadCostUsd(call) ?? vc.CostUsd;
                break;
        }
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];

    // ---- Volcado a formulario (whitelist DURA) ----

    /// <summary>
    /// Volcado de una llamada del motor de acciones al formulario del contacto: whitelist DURA (solo los formularios
    /// permitidos de la VoiceCall), referencia TERCERO:{id} (run y tercero leidos con el filtro del tenant; si no
    /// existen no se escribe nada), etiquetas de opcion -&gt; ids y mezcla sobre los datos previos (lo que no vino en
    /// la llamada se conserva). Si la respuesta ya esta enviada y el formulario no es "registro editable",
    /// SaveAsync la rechaza y no se toca.
    /// </summary>
    private async Task DumpToContactFormsAsync(Guid runId, IReadOnlyList<Guid> allowed,
        IReadOnlyDictionary<string, FormFieldValue> captured, string callId, CancellationToken ct)
    {
        var terceroId = await _db.ContactWorkflowRuns.AsNoTracking()
            .Where(r => r.Id == runId).Select(r => (Guid?)r.TerceroId).FirstOrDefaultAsync(ct);
        if (terceroId is not Guid tid || !await _db.Terceros.AsNoTracking().AnyAsync(t => t.Id == tid, ct))
        {
            _log.LogWarning("Voz IA: llamada {CallId} del motor sin run/contacto vigente; no se vuelcan respuestas.", callId);
            return;
        }
        var reference = Directorio.ITerceroFormService.ReferenceFor(tid);
        var raw = captured.ToDictionary(kv => kv.Key, kv => kv.Value.Value ?? string.Empty, StringComparer.Ordinal);

        foreach (var defId in allowed.Distinct())
        {
            var questions = await _db.FormQuestions.AsNoTracking()
                .Where(q => q.DefinitionId == defId)
                .OrderBy(q => q.SortOrder)
                .Select(q => new VoiceFormQuestion(q.FieldCode, q.Label, q.HelpText, q.ControlType, q.OptionsJson, q.SourceKind, q.IsHidden))
                .ToListAsync(ct);
            var mapped = ContactCallFormQuestions.MapCaptured(ContactCallFormQuestions.Askable(questions), raw);
            if (mapped.Count == 0) { continue; }

            var draft = await _forms.GetOrCreateDraftAsync(defId, reference, ct);
            if (!draft.IsOk || draft.Value is null) { continue; }

            var merged = new Dictionary<string, FormFieldValue>(draft.Value.Data, StringComparer.Ordinal);
            foreach (var (code, value) in mapped) { merged[code] = new FormFieldValue(value, "text"); }
            var saved = await _forms.SaveAsync(draft.Value.Id, merged, submit: false, cancellationToken: ct);
            if (!saved.IsOk)
            {
                _log.LogWarning("Voz IA: no se guardaron las respuestas de {CallId} en el formulario {DefId}: {Error}",
                    callId, defId, saved.Error);
            }
        }
    }

    private async Task DumpCapturedFormsAsync(Domain.Entities.VoiceCall vc, JsonElement call, string callId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vc.FormulariosPermitidosJson)) { return; }
        List<Guid>? allowed;
        try { allowed = JsonSerializer.Deserialize<List<Guid>>(vc.FormulariosPermitidosJson); }
        catch { allowed = null; }
        if (allowed is null || allowed.Count == 0) { return; }

        // Datos capturados: call_analysis.custom_analysis_data (clave -> valor). Best-effort.
        var captured = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        if (call.TryGetProperty("call_analysis", out var analysis)
            && analysis.ValueKind == JsonValueKind.Object
            && analysis.TryGetProperty("custom_analysis_data", out var custom)
            && custom.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in custom.EnumerateObject())
            {
                var val = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
                if (val is not null) { captured[prop.Name] = new FormFieldValue(val, "text"); }
            }
        }
        if (captured.Count == 0) { return; }

        // Llamada del MOTOR DE ACCIONES de contactos (ADR-0190 fase B): las respuestas van al formulario DEL CONTACTO
        // en el Directorio (TERCERO:{id}), traducidas por tipo de pregunta y MEZCLADAS con lo que ya tenia. Las
        // llamadas de flujo (sin ContactWorkflowRunId) siguen por el camino de abajo, sin cambios.
        if (vc.ContactWorkflowRunId is Guid runId)
        {
            await DumpToContactFormsAsync(runId, allowed, captured, callId, ct);
            return;
        }

        // SOLO los formularios de la whitelist. SaveAsync ademas ignora claves que no existan en la definicion.
        foreach (var defId in allowed)
        {
            var draft = await _forms.GetOrCreateDraftAsync(defId, reference: $"voz:{callId}", ct);
            if (!draft.IsOk || draft.Value is null) { continue; }
            await _forms.SaveAsync(draft.Value.Id, captured, submit: false, cancellationToken: ct);
        }
    }

    // ---- Lectura defensiva del payload ----

    private static string? ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? ReadUnixMs(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    private static int? ReadDurationSeconds(JsonElement el)
    {
        if (el.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt64(out var ms))
        {
            return (int)(ms / 1000);
        }
        var start = ReadUnixMs(el, "start_timestamp");
        var end = ReadUnixMs(el, "end_timestamp");
        return start is not null && end is not null ? (int)(end.Value - start.Value).TotalSeconds : null;
    }

    private static decimal? ReadCostUsd(JsonElement el)
    {
        // call_cost.combined_cost suele venir en centavos de USD.
        if (el.TryGetProperty("call_cost", out var cost) && cost.ValueKind == JsonValueKind.Object
            && cost.TryGetProperty("combined_cost", out var cc) && cc.ValueKind == JsonValueKind.Number
            && cc.TryGetDecimal(out var cents))
        {
            return cents / 100m;
        }
        return null;
    }
}
