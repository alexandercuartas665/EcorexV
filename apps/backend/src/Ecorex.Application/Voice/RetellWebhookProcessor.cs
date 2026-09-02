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
        ApplyEvent(voiceCall, eventType!, call);

        // Volcado a formulario (solo si el objetivo era LlenarFormulario y hay whitelist).
        if (string.Equals(eventType, "call_analyzed", StringComparison.OrdinalIgnoreCase)
            && string.Equals(voiceCall.Objetivo, nameof(ContactCallObjetivo.LlenarFormulario), StringComparison.OrdinalIgnoreCase))
        {
            await DumpCapturedFormsAsync(voiceCall, call, callId!, cancellationToken);
        }

        // Actualizar el run del motor de acciones (best-effort) por su ExternalRef = call_id.
        var run = await _db.ContactWorkflowRuns.FirstOrDefaultAsync(r => r.ExternalRef == callId, cancellationToken);
        if (run is not null)
        {
            run.Status = voiceCall.Status switch
            {
                VoiceCallStatus.Error or VoiceCallStatus.Failed => ContactWorkflowRunStatus.Failed,
                _ => run.Status
            };
        }

        await _db.SaveChangesAsync(cancellationToken);
        _log.LogInformation("Voz IA: webhook {Event} aplicado call_id={CallId} status={Status}", eventType, callId, voiceCall.Status);
        return RetellWebhookOutcome.Ok;
    }

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
                break;
            case "call_analyzed":
                vc.Status = VoiceCallStatus.Analyzed;
                vc.TranscriptText = ReadString(call, "transcript") ?? vc.TranscriptText;
                if (call.TryGetProperty("call_analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object)
                {
                    vc.AnalysisJson = analysis.GetRawText();
                }
                vc.CostUsd = ReadCostUsd(call) ?? vc.CostUsd;
                break;
        }
    }

    // ---- Volcado a formulario (whitelist DURA) ----

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
