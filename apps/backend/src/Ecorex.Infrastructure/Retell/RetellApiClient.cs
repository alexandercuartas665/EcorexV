using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ecorex.Application.Voice;

namespace Ecorex.Infrastructure.Retell;

/// <summary>
/// Cliente HTTP de Retell (api.retellai.com). HttpClient inyectado por DI (AddHttpClient). La API key del
/// tenant se pasa DESENCRIPTADA por llamada (header Authorization: Bearer), nunca se guarda ni se loguea.
/// Errores 4xx/5xx/red -> resultado (Ok=false + StatusCode); NUNCA lanza. Reintento con backoff SOLO en
/// transitorios (red/5xx) para llamadas idempotentes-ish; create-phone-call NO se reintenta (evita doble
/// llamada real). Sin secretos en codigo.
/// </summary>
internal sealed class RetellApiClient : IRetellApiClient
{
    private const string BaseUrl = "https://api.retellai.com";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public RetellApiClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<RetellCreateLlmResult> CreateLlmAsync(string apiKey, RetellCreateLlmRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["general_prompt"] = request.GeneralPrompt,
            ["start_speaker"] = request.StartSpeaker
        };
        if (!string.IsNullOrWhiteSpace(request.Model)) { payload["model"] = request.Model; }
        if (!string.IsNullOrWhiteSpace(request.BeginMessage)) { payload["begin_message"] = request.BeginMessage; }

        var (status, body, err) = await SendAsync(HttpMethod.Post, "/create-retell-llm", apiKey, payload, allowRetry: true, cancellationToken);
        if (err is not null || !IsSuccess(status))
        {
            return new RetellCreateLlmResult(false, null, err ?? ExtractError(body) ?? $"HTTP {status}", status);
        }
        return new RetellCreateLlmResult(true, ReadString(body, "llm_id"), null, status);
    }

    public async Task<RetellCreateAgentResult> CreateAgentAsync(string apiKey, RetellCreateAgentRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["response_engine"] = new Dictionary<string, object?> { ["type"] = "retell-llm", ["llm_id"] = request.LlmId },
            ["voice_id"] = request.VoiceId
        };
        if (!string.IsNullOrWhiteSpace(request.Language)) { payload["language"] = request.Language; }
        if (!string.IsNullOrWhiteSpace(request.AgentName)) { payload["agent_name"] = request.AgentName; }

        var (status, body, err) = await SendAsync(HttpMethod.Post, "/create-agent", apiKey, payload, allowRetry: true, cancellationToken);
        if (err is not null || !IsSuccess(status))
        {
            return new RetellCreateAgentResult(false, null, err ?? ExtractError(body) ?? $"HTTP {status}", status);
        }
        return new RetellCreateAgentResult(true, ReadString(body, "agent_id"), null, status);
    }

    public async Task<RetellCreateCallResult> CreatePhoneCallAsync(string apiKey, RetellCreateCallRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from_number"] = request.FromNumber,
            ["to_number"] = request.ToNumber
        };
        if (!string.IsNullOrWhiteSpace(request.OverrideAgentId)) { payload["override_agent_id"] = request.OverrideAgentId; }
        if (request.DynamicVariables is { Count: > 0 }) { payload["retell_llm_dynamic_variables"] = request.DynamicVariables; }
        if (request.CustomSipHeaders is { Count: > 0 }) { payload["custom_sip_headers"] = request.CustomSipHeaders; }
        if (request.Metadata is { Count: > 0 }) { payload["metadata"] = request.Metadata; }
        if (request.IgnoreE164Validation) { payload["ignore_e164_validation"] = true; }

        // NO reintentar: un 5xx puede haber colocado la llamada; reintentar llamaria dos veces a una persona.
        var (status, body, err) = await SendAsync(HttpMethod.Post, "/v2/create-phone-call", apiKey, payload, allowRetry: false, cancellationToken);
        if (err is not null || !IsSuccess(status))
        {
            return new RetellCreateCallResult(false, null, null, null, err ?? ExtractError(body) ?? $"HTTP {status}", status);
        }
        return new RetellCreateCallResult(true, ReadString(body, "call_id"), ReadString(body, "agent_id"), ReadString(body, "call_status"), null, status);
    }

    public async Task<RetellImportNumberResult> ImportPhoneNumberAsync(string apiKey, RetellImportNumberRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["phone_number"] = request.PhoneNumber,
            ["termination_uri"] = request.TerminationUri
        };
        if (!string.IsNullOrWhiteSpace(request.SipTrunkAuthUsername)) { payload["sip_trunk_auth_username"] = request.SipTrunkAuthUsername; }
        if (!string.IsNullOrWhiteSpace(request.SipTrunkAuthPassword)) { payload["sip_trunk_auth_password"] = request.SipTrunkAuthPassword; }
        if (request.OutboundAgents is { Count: > 0 })
        {
            payload["outbound_agents"] = request.OutboundAgents.Select(a => new Dictionary<string, object?> { ["agent_id"] = a.AgentId, ["weight"] = a.Weight });
        }
        if (request.InboundAgents is { Count: > 0 })
        {
            payload["inbound_agents"] = request.InboundAgents.Select(a => new Dictionary<string, object?> { ["agent_id"] = a.AgentId, ["weight"] = a.Weight });
        }

        var (status, body, err) = await SendAsync(HttpMethod.Post, "/import-phone-number", apiKey, payload, allowRetry: true, cancellationToken);
        if (err is not null || !IsSuccess(status))
        {
            return new RetellImportNumberResult(false, null, err ?? ExtractError(body) ?? $"HTTP {status}", status);
        }
        return new RetellImportNumberResult(true, ReadString(body, "phone_number") ?? request.PhoneNumber, null, status);
    }

    public async Task<RetellPingResult> PingAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        // Lectura barata para validar la key (sin colocar llamadas ni costo).
        var (status, body, err) = await SendAsync(HttpMethod.Get, "/list-agents", apiKey, payload: null, allowRetry: true, cancellationToken);
        if (err is not null || !IsSuccess(status))
        {
            return new RetellPingResult(false, err ?? ExtractError(body) ?? $"HTTP {status}", status);
        }
        return new RetellPingResult(true, null, status);
    }

    // ---- Transporte comun ----

    private async Task<(int Status, string Body, string? NetworkError)> SendAsync(
        HttpMethod method, string path, string apiKey, object? payload, bool allowRetry, CancellationToken ct)
    {
        const int maxAttempts = 3;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var req = new HttpRequestMessage(method, BaseUrl + path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                if (payload is not null)
                {
                    req.Content = JsonContent.Create(payload, options: Json);
                }

                using var res = await _http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                var status = (int)res.StatusCode;

                // Reintento SOLO en 5xx transitorio (nunca 4xx) y solo si allowRetry.
                if (allowRetry && status >= 500 && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, ct);
                    continue;
                }
                return (status, body, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fallo de red/timeout: transitorio. Reintenta si se permite; si no, resultado con error.
                if (allowRetry && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, ct);
                    continue;
                }
                return (0, string.Empty, ex.Message);
            }
        }
    }

    private static async Task BackoffAsync(int attempt, CancellationToken ct)
        => await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(3, attempt - 1)), ct);

    private static bool IsSuccess(int status) => status is >= 200 and < 300;

    private static string? ReadString(string body, string name)
    {
        if (string.IsNullOrWhiteSpace(body)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var key in new[] { "error_message", "message", "error" })
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { return v.GetString(); }
            }
        }
        catch (JsonException) { }
        return null;
    }
}
