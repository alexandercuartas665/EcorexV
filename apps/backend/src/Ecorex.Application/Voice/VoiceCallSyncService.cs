using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Voice;

/// <summary>
/// RESPALDO del webhook de Retell (ADR-0190). Si una llamada sigue sin resultado unos minutos despues de colocada
/// (el webhook no llego: URL no configurada en Retell, caida, etc.), se le pregunta a Retell con GET get-call y se
/// aplica lo que responda con los MISMOS efectos que el webhook. Lo corre el worker del motor de acciones.
/// </summary>
public interface IVoiceCallSyncService
{
    /// <summary>Tenants con llamadas recientes aun sin resultado. Barrido de PLATAFORMA: devuelve SOLO ids.</summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithPendingCallsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Consulta a Retell las llamadas pendientes del tenant ACTIVO (ambiente). Devuelve cuantas cambiaron.</summary>
    Task<int> SyncPendingAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescate de pasos de FLUJO pausados esperando una llamada que ya termino pero cuyo webhook no los reanudo
    /// (p. ej. el sondeo de la UI dejo la llamada en Analyzed sin efectos, o una llamada sin contacto nunca
    /// recibio call_analyzed). Solo limpia AgentAttemptedAt del tenant ACTIVO. Devuelve cuantos pasos reanudo.
    /// </summary>
    Task<int> ResumeStalledFlowStepsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class VoiceCallSyncService : IVoiceCallSyncService
{
    /// <summary>Margen para que llegue el webhook antes de preguntar (y entre dos consultas a la misma llamada).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    /// <summary>Solo se respaldan llamadas de las ultimas horas (despues ya no se esperan eventos).</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(6);

    /// <summary>Una llamada terminada que no trae analisis tras este tiempo ya no se vuelve a consultar.</summary>
    public static readonly TimeSpan AnalysisWait = TimeSpan.FromMinutes(30);

    /// <summary>Un paso solo se rescata si su ultimo intento del agente tiene al menos este tiempo, para no pisar
    /// un paso que el barrido de agentes esta corriendo en ese momento (acordado con la sesion de flujos).</summary>
    public static readonly TimeSpan StepSettle = TimeSpan.FromMinutes(5);

    private const int BatchCap = 20;
    private const int StepBatchCap = 100;

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IRetellApiClient _retell;
    private readonly IRetellWebhookProcessor _processor;
    private readonly ILogger<VoiceCallSyncService> _log;

    public VoiceCallSyncService(IApplicationDbContext db, ISecretProtector protector, IRetellApiClient retell,
        IRetellWebhookProcessor processor, ILogger<VoiceCallSyncService> log)
    {
        _db = db;
        _protector = protector;
        _retell = retell;
        _processor = processor;
        _log = log;
    }

    public async Task<IReadOnlyList<Guid>> FindTenantsWithPendingCallsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var since = nowUtc - Horizon;
        var stepSettled = nowUtc - StepSettle;
        // IgnoreQueryFilters: barrido de plataforma (aun sin tenant). Solo ids; cada sync va acotado al tenant.
        var withCalls = await _db.VoiceCalls.IgnoreQueryFilters()
            .Where(v => v.CreatedAt >= since && v.RetellVoiceLineId != null
                && (v.Status == VoiceCallStatus.Registered || v.Status == VoiceCallStatus.Ongoing || v.Status == VoiceCallStatus.Ended))
            .Select(v => v.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        // + tenants con pasos de flujo esperando una llamada (candidatos al rescate).
        var withWaitingSteps = await _db.WorkflowStepHistories.IgnoreQueryFilters()
            .Where(s => s.IsCurrent && s.PendingVoiceCallId != null && s.AgentAttemptedAt != null && s.AgentAttemptedAt < stepSettled)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return withCalls.Concat(withWaitingSteps).Distinct().ToList();
    }

    public async Task<int> ResumeStalledFlowStepsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var stepSettled = nowUtc - StepSettle;

        // Acotado al tenant ACTIVO por el filtro global (sin IgnoreQueryFilters). Los mas recientes primero para
        // que un paso viejo que espera para siempre (Ended con contacto sin analisis) no tape a los nuevos.
        var candidates = await (
                from s in _db.WorkflowStepHistories
                join v in _db.VoiceCalls on s.PendingVoiceCallId equals v.CallId
                where s.IsCurrent && s.PendingVoiceCallId != null && s.AgentAttemptedAt != null
                    && s.AgentAttemptedAt < stepSettled
                    && (v.Status == VoiceCallStatus.Analyzed || v.Status == VoiceCallStatus.Failed
                        || v.Status == VoiceCallStatus.Error || v.Status == VoiceCallStatus.Ended)
                orderby s.AgentAttemptedAt descending
                select new { Step = s, v.Status, v.DisconnectionReason })
            .Take(StepBatchCap)
            .ToListAsync(cancellationToken);

        var resumed = 0;
        foreach (var c in candidates)
        {
            // Ended solo cuenta si fue SIN contacto (con contacto aun se espera el call_analyzed).
            if (c.Status == VoiceCallStatus.Ended && !RetellVoiceService.IsNoContactReason(c.DisconnectionReason)) { continue; }
            // Solo se limpia AgentAttemptedAt: PendingVoiceCallId lo lee y lo limpia el runner al consumir el resultado.
            c.Step.AgentAttemptedAt = null;
            resumed++;
        }
        if (resumed == 0) { return 0; }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Carrera con el barrido de agentes o el webhook: gana el otro; la proxima pasada reevalua.
            _log.LogDebug(ex, "Voz IA: no se guardo el rescate de pasos de flujo en espera.");
            return 0;
        }
        _log.LogInformation("Voz IA: {Resumed} paso(s) de flujo reanudado(s) por respaldo (llamada ya terminada).", resumed);
        return resumed;
    }

    public async Task<int> SyncPendingAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var since = nowUtc - Horizon;
        var settled = nowUtc - Grace;
        var analysisLimit = nowUtc - AnalysisWait;

        var pending = await _db.VoiceCalls
            .Where(v => v.CreatedAt >= since && v.CreatedAt <= settled && v.RetellVoiceLineId != null
                && (v.Status == VoiceCallStatus.Registered || v.Status == VoiceCallStatus.Ongoing
                    || (v.Status == VoiceCallStatus.Ended && (v.EndedAt == null || v.EndedAt >= analysisLimit)))
                && (v.LastSyncedAtUtc == null || v.LastSyncedAtUtc <= settled))
            .OrderBy(v => v.CreatedAt)
            .Take(BatchCap)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) { return 0; }

        var lineIds = pending.Select(v => v.RetellVoiceLineId!.Value).Distinct().ToList();
        var keys = await _db.RetellVoiceLines.AsNoTracking()
            .Where(l => lineIds.Contains(l.Id) && l.RetellApiKeyEncrypted != null)
            .Select(l => new { l.Id, l.RetellApiKeyEncrypted })
            .ToListAsync(cancellationToken);
        var apiKeys = new Dictionary<Guid, string>();
        foreach (var k in keys)
        {
            try { apiKeys[k.Id] = _protector.Unprotect(k.RetellApiKeyEncrypted!); }
            catch { /* key ilegible: esas llamadas se saltan */ }
        }

        var changed = 0;
        foreach (var vc in pending)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            vc.LastSyncedAtUtc = nowUtc;
            if (apiKeys.TryGetValue(vc.RetellVoiceLineId!.Value, out var apiKey))
            {
                var res = await _retell.GetCallAsync(apiKey, vc.CallId, cancellationToken);
                if (res.Ok && !string.IsNullOrWhiteSpace(res.RawCall))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(res.RawCall);
                        if (await _processor.ApplyPolledCallAsync(vc, doc.RootElement, cancellationToken)) { changed++; }
                    }
                    catch (JsonException)
                    {
                        _log.LogWarning("Voz IA: respuesta de get-call ilegible para call_id={CallId}", vc.CallId);
                    }
                }
            }
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Carrera con el webhook sobre la misma fila: el webhook gana; la proxima pasada reintenta.
                _log.LogDebug(ex, "Voz IA: no se guardo el respaldo de call_id={CallId}", vc.CallId);
            }
        }
        if (changed > 0)
        {
            _log.LogInformation("Voz IA: {Changed} llamada(s) actualizada(s) por respaldo (get-call).", changed);
        }
        return changed;
    }
}
