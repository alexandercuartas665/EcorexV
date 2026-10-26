using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Voice;

/// <summary>
/// Orquestacion de una llamada IA (Retell/Telnyx): compone el prompt del AiAgent (SystemPrompt + PromptExtra +
/// directiva de objetivo), ASEGURA un agente Retell cuyo prompt ES ese (reemplaza el suyo), coloca la llamada
/// y persiste la <see cref="VoiceCall"/>. Tenant-safe (todo sobre el filtro global). Las funciones de composicion
/// (prompt, hash, variables) son PURAS y separables del I/O.
/// </summary>
public sealed class RetellVoiceService : IRetellVoiceService
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled);
    private const string DefaultVoiceId = "11labs-Adrian"; // fallback si el tenant no eligio voz (configurable)

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IRetellApiClient _retell;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetellVoiceService> _log;

    public RetellVoiceService(IApplicationDbContext db, ISecretProtector protector, IRetellApiClient retell,
        TimeProvider clock, ILogger<RetellVoiceService> log)
    {
        _db = db;
        _protector = protector;
        _retell = retell;
        _clock = clock;
        _log = log;
    }

    public async Task<VoicePlaceCallResult> PlaceCallAsync(VoicePlaceCallRequest request, CancellationToken cancellationToken = default)
    {
        // 1) E.164 del destino (barrera dura antes de gastar).
        if (!IsE164(request.ToNumber))
        {
            return new VoicePlaceCallResult(false, null, "Numero del contacto no esta en formato E.164.", Retryable: false);
        }

        // 2) Config del tenant (ambiente). Sin key habilitada no hay llamada.
        // Si la peticion fija una LINEA concreta (prueba por tarjeta), se usa esa; si no, la habilitada por
        // defecto (o la primera habilitada y configurada completa).
        var lines = _db.RetellVoiceLines.Where(l => l.IsEnabled && l.RetellApiKeyEncrypted != null && l.FromNumber != null);
        var cfg = request.RetellVoiceLineId is Guid lineId
            ? await lines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            : await lines.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            return new VoicePlaceCallResult(false, null,
                request.RetellVoiceLineId is null
                    ? "No hay una linea de voz habilitada y configurada para el tenant."
                    : "La linea elegida no existe, no esta habilitada o le falta API key / numero saliente.",
                Retryable: false);
        }
        if (!IsE164(cfg.FromNumber))
        {
            return new VoicePlaceCallResult(false, null, "El numero saliente configurado no esta en E.164.", Retryable: false);
        }

        string apiKey;
        try { apiKey = _protector.Unprotect(cfg.RetellApiKeyEncrypted); }
        catch { return new VoicePlaceCallResult(false, null, "La API key de Retell no se pudo descifrar.", Retryable: false); }

        // 3-4) Agente Retell. MODO SIMPLE (como el hermano Visal): si la linea trae un agent_id ya armado en Retell,
        //      se usa DIRECTO (override_agent_id) y ECOREX no compone ni crea nada. MODO COMPUESTO (sin agent_id):
        //      ECOREX arma el prompt (AiAgent + extra + objetivo) y asegura el agente al vuelo (crea/reusa LLM+agente).
        string overrideAgentId;
        if (!string.IsNullOrWhiteSpace(cfg.RetellAgentId))
        {
            overrideAgentId = cfg.RetellAgentId!.Trim();
        }
        else
        {
            var systemPrompt = "";
            if (request.AiAgentId is Guid agentId)
            {
                systemPrompt = await _db.AiAgents.AsNoTracking()
                    .Where(a => a.Id == agentId).Select(a => a.SystemPrompt).FirstOrDefaultAsync(cancellationToken) ?? "";
            }
            var voiceId = string.IsNullOrWhiteSpace(cfg.VoiceId) ? DefaultVoiceId : cfg.VoiceId!;
            var language = string.IsNullOrWhiteSpace(cfg.Language) ? "es-419" : cfg.Language;
            var prompt = ComposePrompt(systemPrompt, request.PromptExtra, request.Objetivo);
            var hash = PromptHash(prompt, voiceId, language);

            var ensure = await EnsureAgentAsync(apiKey, hash, prompt, voiceId, language, request.AiAgentId, cancellationToken);
            if (!ensure.Ok)
            {
                return new VoicePlaceCallResult(false, null, ensure.Error, ensure.Retryable);
            }
            overrideAgentId = ensure.AgentId;
        }

        // 5) Colocar la llamada.
        var dyn = BuildDynamicVariables(request.ContactVariables, request.Objetivo);
        // Variables EXTRA (p.ej. las que el humano da en la prueba para los {{placeholders}} del prompt).
        if (request.ExtraDynamicVariables is { Count: > 0 })
        {
            foreach (var kv in request.ExtraDynamicVariables)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value)) { dyn[kv.Key] = kv.Value!.Trim(); }
            }
        }
        var sipHeaders = string.IsNullOrWhiteSpace(cfg.SipUsername)
            ? null
            : new Dictionary<string, string> { ["X-Telnyx-Username"] = cfg.SipUsername! };
        var metadata = new Dictionary<string, string> { ["source"] = "ecorex-contact-workflow" };
        if (request.ContactWorkflowRunId is Guid runId) { metadata["contact_workflow_run_id"] = runId.ToString(); }

        var callRes = await _retell.CreatePhoneCallAsync(apiKey, new RetellCreateCallRequest(
            FromNumber: cfg.FromNumber!,
            ToNumber: request.ToNumber,
            OverrideAgentId: overrideAgentId,
            DynamicVariables: dyn,
            CustomSipHeaders: sipHeaders,
            Metadata: metadata), cancellationToken);

        if (!callRes.Ok || string.IsNullOrEmpty(callRes.CallId))
        {
            _log.LogWarning("Voz IA: fallo al colocar llamada (HTTP {Status}): {Error}", callRes.StatusCode, callRes.Error);
            return new VoicePlaceCallResult(false, null, callRes.Error ?? $"Retell HTTP {callRes.StatusCode}.", Retryable: IsTransient(callRes.StatusCode));
        }

        // 6) Persistir la VoiceCall (snapshot de origen + whitelist de formularios).
        _db.VoiceCalls.Add(new VoiceCall
        {
            CallId = callRes.CallId!,
            RetellVoiceLineId = cfg.Id,
            RetellAgentId = overrideAgentId,
            FromNumber = cfg.FromNumber!,
            ToNumber = request.ToNumber,
            Status = VoiceCallStatus.Registered,
            AiAgentId = request.AiAgentId,
            Objetivo = request.Objetivo,
            FormulariosPermitidosJson = request.FormulariosPermitidos.Count > 0
                ? JsonSerializer.Serialize(request.FormulariosPermitidos) : null,
            ContactWorkflowRunId = request.ContactWorkflowRunId
        });
        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation("Voz IA: llamada colocada call_id={CallId} agent={AgentId}", callRes.CallId, overrideAgentId);
        return new VoicePlaceCallResult(true, callRes.CallId, null, Retryable: false);
    }

    public async Task<VoiceCallResultDto?> GetCallResultAsync(string callId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callId)) { return null; }
        var vc = await _db.VoiceCalls.AsNoTracking()
            .FirstOrDefaultAsync(v => v.CallId == callId, cancellationToken);
        if (vc is null) { return null; }
        return new VoiceCallResultDto(
            CallId: vc.CallId,
            Status: vc.Status.ToString(),
            StartedAt: vc.StartedAt,
            EndedAt: vc.EndedAt,
            DurationSeconds: vc.DurationSeconds,
            CostUsd: vc.CostUsd,
            TranscriptText: vc.TranscriptText,
            RecordingUrl: vc.RecordingUrl,
            AnalysisSummary: ExtractAnalysisSummary(vc.AnalysisJson));
    }

    public async Task<VoiceTestAgentInfoDto?> GetTestAgentInfoAsync(Guid lineId, Guid? aiAgentId, string? promptExtra, string? objetivo, CancellationToken cancellationToken = default)
    {
        var line = await _db.RetellVoiceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return null; }

        // MODO SIMPLE: la linea trae un Agent ID ya armado en Retell -> leemos su config real (voz/idioma/prompt).
        if (!string.IsNullOrWhiteSpace(line.RetellAgentId))
        {
            if (string.IsNullOrEmpty(line.RetellApiKeyEncrypted))
            {
                return new VoiceTestAgentInfoDto("simple", line.VoiceId, line.Language, null, null, Array.Empty<string>(),
                    "La linea no tiene API key configurada; no se puede leer el agente de Retell.");
            }
            string apiKey;
            try { apiKey = _protector.Unprotect(line.RetellApiKeyEncrypted); }
            catch { return new VoiceTestAgentInfoDto("simple", line.VoiceId, line.Language, null, null, Array.Empty<string>(), "La API key de Retell no se pudo descifrar."); }

            var agent = await _retell.GetAgentAsync(apiKey, line.RetellAgentId!.Trim(), cancellationToken);
            if (!agent.Ok)
            {
                return new VoiceTestAgentInfoDto("simple", line.VoiceId, line.Language, null, null, Array.Empty<string>(),
                    $"No se pudo leer el agente de Retell: {agent.Error}");
            }
            string? prompt = null, model = null;
            if (!string.IsNullOrWhiteSpace(agent.LlmId))
            {
                var llm = await _retell.GetLlmAsync(apiKey, agent.LlmId!, cancellationToken);
                if (llm.Ok) { prompt = llm.GeneralPrompt; model = llm.Model; }
            }
            return new VoiceTestAgentInfoDto("simple", agent.VoiceId ?? line.VoiceId, agent.Language ?? line.Language, model, prompt,
                ExtractPromptVariables(prompt),
                "En modo simple el prompt lo controla Retell: no se puede cambiar por llamada, pero las variables si se rellenan.");
        }

        // MODO COMPUESTO: ECOREX arma el prompt al vuelo (AiAgent + prompt adicional + objetivo). No hay agente fijo.
        var systemPrompt = "";
        if (aiAgentId is Guid agentId)
        {
            systemPrompt = await _db.AiAgents.AsNoTracking()
                .Where(a => a.Id == agentId).Select(a => a.SystemPrompt).FirstOrDefaultAsync(cancellationToken) ?? "";
        }
        var composed = ComposePrompt(systemPrompt, promptExtra, objetivo);
        var voiceId = string.IsNullOrWhiteSpace(line.VoiceId) ? DefaultVoiceId : line.VoiceId;
        var language = string.IsNullOrWhiteSpace(line.Language) ? "es-419" : line.Language;
        return new VoiceTestAgentInfoDto("compuesto", voiceId, language, null, composed, ExtractPromptVariables(composed), null);
    }

    private static readonly Regex PromptVar = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Nombres de variables {{...}} que aparecen en el prompt (distintas, en orden de aparicion).</summary>
    public static IReadOnlyList<string> ExtractPromptVariables(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) { return Array.Empty<string>(); }
        var seen = new List<string>();
        foreach (Match m in PromptVar.Matches(prompt))
        {
            var name = m.Groups[1].Value;
            if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase)) { seen.Add(name); }
        }
        return seen;
    }

    /// <summary>Saca un resumen legible del call_analysis (call_summary + exito + sentimiento) para la UI. Best-effort.</summary>
    public static string? ExtractAnalysisSummary(string? analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(analysisJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }
            var parts = new List<string>();
            if (root.TryGetProperty("call_summary", out var s) && s.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(s.GetString())) { parts.Add(s.GetString()!.Trim()); }
            if (root.TryGetProperty("user_sentiment", out var se) && se.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(se.GetString())) { parts.Add("Sentimiento: " + se.GetString()); }
            if (root.TryGetProperty("call_successful", out var ok)
                && (ok.ValueKind == JsonValueKind.True || ok.ValueKind == JsonValueKind.False))
            {
                parts.Add(ok.GetBoolean() ? "Resultado: exitosa" : "Resultado: no exitosa");
            }
            return parts.Count > 0 ? string.Join("\n", parts) : null;
        }
        catch (JsonException) { return null; }
    }

    // ---- Aseguramiento del agente (LLM + agente Retell keyed por hash del prompt) ----

    private sealed record EnsureResult(bool Ok, string? AgentId, string? Error, bool Retryable);

    private async Task<EnsureResult> EnsureAgentAsync(string apiKey, string hash, string prompt, string voiceId,
        string language, Guid? aiAgentId, CancellationToken ct)
    {
        var existing = await _db.RetellAgentMaps.FirstOrDefaultAsync(m => m.PromptHash == hash, ct);
        if (existing is not null)
        {
            existing.LastUsedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
            return new EnsureResult(true, existing.RetellAgentId, null, false);
        }

        var llm = await _retell.CreateLlmAsync(apiKey, new RetellCreateLlmRequest(GeneralPrompt: prompt), ct);
        if (!llm.Ok || string.IsNullOrEmpty(llm.LlmId))
        {
            return new EnsureResult(false, null, llm.Error ?? $"Retell create-llm HTTP {llm.StatusCode}.", IsTransient(llm.StatusCode));
        }

        var agent = await _retell.CreateAgentAsync(apiKey, new RetellCreateAgentRequest(
            LlmId: llm.LlmId!, VoiceId: voiceId, Language: language, AgentName: "ECOREX voz"), ct);
        if (!agent.Ok || string.IsNullOrEmpty(agent.AgentId))
        {
            return new EnsureResult(false, null, agent.Error ?? $"Retell create-agent HTTP {agent.StatusCode}.", IsTransient(agent.StatusCode));
        }

        _db.RetellAgentMaps.Add(new RetellAgentMap
        {
            PromptHash = hash,
            RetellLlmId = llm.LlmId!,
            RetellAgentId = agent.AgentId!,
            AiAgentId = aiAgentId,
            LastUsedAt = _clock.GetUtcNow()
        });
        await _db.SaveChangesAsync(ct);
        return new EnsureResult(true, agent.AgentId, null, false);
    }

    // ---- Funciones PURAS (sin I/O) ----

    public static bool IsE164(string? number) => !string.IsNullOrWhiteSpace(number) && E164.IsMatch(number);

    /// <summary>Compone el prompt que sera el general_prompt del agente Retell (reemplaza cualquiera que tuviera).</summary>
    public static string ComposePrompt(string? systemPrompt, string? promptExtra, string? objetivo)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt)) { sb.Append(systemPrompt!.Trim()); }
        if (!string.IsNullOrWhiteSpace(promptExtra))
        {
            if (sb.Length > 0) { sb.Append("\n\n"); }
            sb.Append("Instruccion adicional para esta llamada:\n").Append(promptExtra!.Trim());
        }
        var directiva = ObjetivoDirective(objetivo);
        if (!string.IsNullOrEmpty(directiva))
        {
            if (sb.Length > 0) { sb.Append("\n\n"); }
            sb.Append(directiva);
        }
        return sb.ToString();
    }

    public static string ObjetivoDirective(string? objetivo)
    {
        if (Enum.TryParse<ContactCallObjetivo>(objetivo, ignoreCase: true, out var o))
        {
            return o switch
            {
                ContactCallObjetivo.OfrecerProducto =>
                    "Objetivo de la llamada: ofrecer el producto o servicio al contacto de forma clara y cordial, resolviendo dudas.",
                ContactCallObjetivo.LlenarFormulario =>
                    "Objetivo de la llamada: recopilar del contacto los datos necesarios para completar el formulario indicado; confirma cada dato antes de cerrar.",
                _ => ""
            };
        }
        return "";
    }

    /// <summary>Hash de identidad del agente Retell = prompt + voz + idioma.</summary>
    public static string PromptHash(string prompt, string voiceId, string language)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{prompt}{voiceId}{language}"));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) { sb.Append(b.ToString("x2")); }
        return sb.ToString();
    }

    /// <summary>Variables dinamicas para inyectar en el prompt del Response Engine (datos del contacto + objetivo).</summary>
    public static Dictionary<string, string> BuildDynamicVariables(IReadOnlyDictionary<string, string?> contact, string? objetivo)
    {
        var d = new Dictionary<string, string>();
        void Put(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) { d[k] = v!.Trim(); } }
        Put("nombre", Get(contact, "nombre"));
        Put("empresa", Get(contact, "empresa"));
        Put("cargo", Get(contact, "cargo"));
        Put("ciudad", Get(contact, "ciudad"));
        Put("producto", Get(contact, "producto"));
        if (!string.IsNullOrWhiteSpace(objetivo)) { d["objetivo"] = objetivo!; }
        return d;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> map, string key)
        => map.TryGetValue(key, out var v) ? v : null;

    private static bool IsTransient(int statusCode) => statusCode == 0 || statusCode == 429 || statusCode >= 500;
}
