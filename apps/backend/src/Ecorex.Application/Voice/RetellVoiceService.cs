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
        // Elige la LINEA a usar: la habilitada por defecto; si no hay default, la primera habilitada completa.
        var cfg = await _db.RetellVoiceLines
            .Where(l => l.IsEnabled && l.RetellApiKeyEncrypted != null && l.FromNumber != null)
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            return new VoicePlaceCallResult(false, null, "No hay una linea de voz habilitada y configurada para el tenant.", Retryable: false);
        }
        if (!IsE164(cfg.FromNumber))
        {
            return new VoicePlaceCallResult(false, null, "El numero saliente configurado no esta en E.164.", Retryable: false);
        }

        string apiKey;
        try { apiKey = _protector.Unprotect(cfg.RetellApiKeyEncrypted); }
        catch { return new VoicePlaceCallResult(false, null, "La API key de Retell no se pudo descifrar.", Retryable: false); }

        // 3) Prompt COMPUESTO por ECOREX (reemplaza el del agente Retell).
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

        // 4) Asegurar el agente Retell para ese prompt (crea LLM + agente si no existe; reusa si si).
        var ensure = await EnsureAgentAsync(apiKey, hash, prompt, voiceId, language, request.AiAgentId, cancellationToken);
        if (!ensure.Ok)
        {
            return new VoicePlaceCallResult(false, null, ensure.Error, ensure.Retryable);
        }

        // 5) Colocar la llamada.
        var dyn = BuildDynamicVariables(request.ContactVariables, request.Objetivo);
        var sipHeaders = string.IsNullOrWhiteSpace(cfg.SipUsername)
            ? null
            : new Dictionary<string, string> { ["X-Telnyx-Username"] = cfg.SipUsername! };
        var metadata = new Dictionary<string, string> { ["source"] = "ecorex-contact-workflow" };
        if (request.ContactWorkflowRunId is Guid runId) { metadata["contact_workflow_run_id"] = runId.ToString(); }

        var callRes = await _retell.CreatePhoneCallAsync(apiKey, new RetellCreateCallRequest(
            FromNumber: cfg.FromNumber!,
            ToNumber: request.ToNumber,
            OverrideAgentId: ensure.AgentId,
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
            RetellAgentId = ensure.AgentId,
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

        _log.LogInformation("Voz IA: llamada colocada call_id={CallId} agent={AgentId}", callRes.CallId, ensure.AgentId);
        return new VoicePlaceCallResult(true, callRes.CallId, null, Retryable: false);
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
