using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Infrastructure.Ai;

/// <summary>
/// Cliente HTTP de inferencia para los proveedores de IA. La API key llega descifrada; no se persiste
/// ni se loggea. Soporta Gemini (REST), OpenAI/ChatGPT y DeepSeek (chat/completions) y Claude (messages).
/// </summary>
public sealed class AiProviderClient : IAiProviderClient
{
    private readonly HttpClient _http;

    public AiProviderClient(HttpClient http) => _http = http;

    public async Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default)
    {
        try
        {
            return provider switch
            {
                AiProvider.Gemini => await Gemini(apiKey, baseUrl, model, systemPrompt, turns, cancellationToken),
                AiProvider.Claude => await Claude(apiKey, baseUrl, model, systemPrompt, turns, cancellationToken),
                _ => await OpenAiCompatible(provider, apiKey, baseUrl, model, systemPrompt, turns, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            return new AiChatResult(false, null, $"No se pudo contactar al proveedor: {ex.Message}");
        }
    }

    public async Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
    {
        try
        {
            return provider switch
            {
                AiProvider.Gemini => await GeminiVision(apiKey, baseUrl, model, systemPrompt, content, cancellationToken),
                AiProvider.Claude => await ClaudeVision(apiKey, baseUrl, model, systemPrompt, content, cancellationToken),
                _ => new AiChatResult(false, null, $"El proveedor {provider} no soporta vision en ECOREX.tareas (usa Gemini o Claude).")
            };
        }
        catch (Exception ex)
        {
            return new AiChatResult(false, null, $"No se pudo contactar al proveedor de vision: {ex.Message}");
        }
    }

    // ===== Gemini (vision) =====
    private async Task<AiChatResult> GeminiVision(string apiKey, string? baseUrl, string model, string systemPrompt,
        IReadOnlyList<AiVisionPart> content, CancellationToken ct)
    {
        var url = $"{Base(baseUrl, "https://generativelanguage.googleapis.com")}/v1beta/models/{model}:generateContent?key={apiKey}";
        var parts = content.Select(p => p.ImageBase64 is not null
            ? (object)new { inlineData = new { mimeType = p.ImageMime ?? "image/jpeg", data = p.ImageBase64 } }
            : new { text = p.Text ?? "" }).ToArray();
        var body = new
        {
            systemInstruction = string.IsNullOrWhiteSpace(systemPrompt) ? null : new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts } }
        };
        using var resp = await _http.PostAsync(url, JsonContent(body), ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return Fail((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usageMetadata", out var um))
        {
            inTok = um.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
            outTok = um.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        }
        return new AiChatResult(true, text, null, inTok, outTok);
    }

    // ===== Claude (vision) =====
    private async Task<AiChatResult> ClaudeVision(string apiKey, string? baseUrl, string model, string systemPrompt,
        IReadOnlyList<AiVisionPart> content, CancellationToken ct)
    {
        var url = $"{Base(baseUrl, "https://api.anthropic.com")}/v1/messages";
        var blocks = content.Select(p => p.ImageBase64 is not null
            ? (object)new { type = "image", source = new { type = "base64", media_type = p.ImageMime ?? "image/jpeg", data = p.ImageBase64 } }
            : new { type = "text", text = p.Text ?? "" }).ToArray();
        var body = new
        {
            model,
            max_tokens = 1024,
            system = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            messages = new[] { new { role = "user", content = blocks } }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return Fail((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            inTok = u.TryGetProperty("input_tokens", out var p) ? p.GetInt32() : 0;
            outTok = u.TryGetProperty("output_tokens", out var c) ? c.GetInt32() : 0;
        }
        return new AiChatResult(true, text, null, inTok, outTok);
    }

    private static string Base(string? baseUrl, string fallback) =>
        (string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl).TrimEnd('/');

    // ===== Gemini =====
    private async Task<AiChatResult> Gemini(string apiKey, string? baseUrl, string model, string systemPrompt,
        IReadOnlyList<AiChatTurn> turns, CancellationToken ct)
    {
        var url = $"{Base(baseUrl, "https://generativelanguage.googleapis.com")}/v1beta/models/{model}:generateContent?key={apiKey}";
        var body = new
        {
            systemInstruction = string.IsNullOrWhiteSpace(systemPrompt) ? null : new { parts = new[] { new { text = systemPrompt } } },
            contents = turns.Select(t => new { role = t.Role == "model" ? "model" : "user", parts = new[] { new { text = t.Text } } }).ToArray()
        };
        using var resp = await _http.PostAsync(url, JsonContent(body), ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return Fail(resp.StatusCode is var s ? (int)s : 0, raw); }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usageMetadata", out var um))
        {
            inTok = um.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
            outTok = um.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        }
        return new AiChatResult(true, text, null, inTok, outTok);
    }

    // ===== OpenAI / ChatGPT / DeepSeek (formato chat/completions) =====
    private async Task<AiChatResult> OpenAiCompatible(AiProvider provider, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken ct)
    {
        var fallback = provider == AiProvider.DeepSeek ? "https://api.deepseek.com" : "https://api.openai.com/v1";
        var url = $"{Base(baseUrl, fallback)}/chat/completions";

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt)) { messages.Add(new { role = "system", content = systemPrompt }); }
        foreach (var t in turns) { messages.Add(new { role = t.Role == "model" ? "assistant" : "user", content = t.Text }); }

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(new { model, messages }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return Fail((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            inTok = u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            outTok = u.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
        }
        return new AiChatResult(true, text, null, inTok, outTok);
    }

    // ===== Claude (messages) =====
    private async Task<AiChatResult> Claude(string apiKey, string? baseUrl, string model, string systemPrompt,
        IReadOnlyList<AiChatTurn> turns, CancellationToken ct)
    {
        var url = $"{Base(baseUrl, "https://api.anthropic.com")}/v1/messages";
        var body = new
        {
            model,
            max_tokens = 1024,
            system = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            messages = turns.Select(t => new { role = t.Role == "model" ? "assistant" : "user", content = t.Text }).ToArray()
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return Fail((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            inTok = u.TryGetProperty("input_tokens", out var p) ? p.GetInt32() : 0;
            outTok = u.TryGetProperty("output_tokens", out var c) ? c.GetInt32() : 0;
        }
        return new AiChatResult(true, text, null, inTok, outTok);
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8, "application/json");

    // Backoff ante saturacion transitoria del proveedor (Gemini/OpenAI suelen devolver 503 bajo carga).
    // 4 intentos en total: inmediato + esperas de 1.2s / 3s / 6s. Solo reintenta codigos transitorios.
    private static readonly int[] RetryDelaysMs = { 1200, 3000, 6000 };
    private static bool IsTransientStatus(int status) => status is 429 or 500 or 502 or 503 or 504;

    // El HttpRequestMessage (y su contenido) no se puede reenviar; la fabrica lo reconstruye por intento.
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var req = requestFactory();
            var resp = await _http.SendAsync(req, ct);
            req.Dispose();
            if (resp.IsSuccessStatusCode || !IsTransientStatus((int)resp.StatusCode) || attempt >= RetryDelaysMs.Length)
            {
                return resp;
            }
            resp.Dispose(); // respuesta transitoria: la descartamos y reintentamos tras el backoff
            await Task.Delay(RetryDelaysMs[attempt], ct);
        }
    }

    private static AiChatResult Fail(int status, string raw)
    {
        var snippet = raw.Length > 300 ? raw[..300] : raw;
        return new AiChatResult(false, null, $"El proveedor respondio HTTP {status}. {snippet}");
    }

    // ===================== Function calling =====================

    public async Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default)
    {
        try
        {
            return provider switch
            {
                AiProvider.Claude => await ClaudeWithTools(apiKey, baseUrl, model, systemPrompt, messages, tools, cancellationToken),
                _ => await OpenAiCompatibleWithTools(provider, apiKey, baseUrl, model, systemPrompt, messages, tools, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            return new AiCompletion(false, null, $"No se pudo contactar al proveedor: {ex.Message}", 0, 0, Array.Empty<AiToolCall>());
        }
    }

    // Gemini (endpoint OpenAI-compatible), OpenAI/ChatGPT y DeepSeek: formato chat/completions con tools.
    private async Task<AiCompletion> OpenAiCompatibleWithTools(AiProvider provider, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken ct)
    {
        var url = provider switch
        {
            AiProvider.Gemini => $"{Base(baseUrl, "https://generativelanguage.googleapis.com")}/v1beta/openai/chat/completions",
            AiProvider.DeepSeek => $"{Base(baseUrl, "https://api.deepseek.com")}/chat/completions",
            _ => $"{Base(baseUrl, "https://api.openai.com/v1")}/chat/completions"
        };

        var msgs = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt)) { msgs.Add(new { role = "system", content = systemPrompt }); }
        foreach (var m in messages)
        {
            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                msgs.Add(new { role = "tool", tool_call_id = m.ToolCallId, content = m.Text ?? "" });
            }
            else if (m.ToolCalls is { Count: > 0 })
            {
                msgs.Add(new
                {
                    role = "assistant",
                    content = m.Text ?? "",
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                    }).ToArray()
                });
            }
            else
            {
                msgs.Add(new { role = m.Role is "model" or "assistant" ? "assistant" : "user", content = m.Text ?? "" });
            }
        }

        var toolDefs = tools.Select(t => new
        {
            type = "function",
            function = new { name = t.Name, description = t.Description ?? "", parameters = ParseSchema(t.ParametersJsonSchema) }
        }).ToArray();

        object body = toolDefs.Length > 0
            ? new { model, messages = msgs, tools = toolDefs, tool_choice = "auto" }
            : new { model, messages = msgs };

        using var resp = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return req;
        }, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return FailTools((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        string? text = message.TryGetProperty("content", out var ce) && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;

        var calls = new List<AiToolCall>();
        if (message.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                var id = tc.TryGetProperty("id", out var ide) ? ide.GetString() ?? "" : "";
                if (!tc.TryGetProperty("function", out var fn)) { continue; }
                var name = fn.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
                var argsJson = fn.TryGetProperty("arguments", out var ae)
                    ? (ae.ValueKind == JsonValueKind.String ? ae.GetString() ?? "{}" : ae.GetRawText())
                    : "{}";
                if (!string.IsNullOrWhiteSpace(name)) { calls.Add(new AiToolCall(id, name, argsJson)); }
            }
        }

        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            inTok = u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            outTok = u.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
        }
        return new AiCompletion(true, text, null, inTok, outTok, calls);
    }

    // Claude (messages) con tool_use / tool_result.
    private async Task<AiCompletion> ClaudeWithTools(string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken ct)
    {
        var url = $"{Base(baseUrl, "https://api.anthropic.com")}/v1/messages";

        var msgs = new List<object>();
        foreach (var m in messages)
        {
            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                msgs.Add(new
                {
                    role = "user",
                    content = new object[] { new { type = "tool_result", tool_use_id = m.ToolCallId, content = m.Text ?? "" } }
                });
            }
            else if (m.ToolCalls is { Count: > 0 })
            {
                var blocks = new List<object>();
                if (!string.IsNullOrWhiteSpace(m.Text)) { blocks.Add(new { type = "text", text = m.Text }); }
                foreach (var tc in m.ToolCalls)
                {
                    blocks.Add(new { type = "tool_use", id = tc.Id, name = tc.Name, input = ParseSchema(tc.ArgumentsJson) });
                }
                msgs.Add(new { role = "assistant", content = blocks.ToArray() });
            }
            else
            {
                msgs.Add(new { role = m.Role is "model" or "assistant" ? "assistant" : "user", content = m.Text ?? "" });
            }
        }

        var toolDefs = tools.Select(t => new
        {
            name = t.Name,
            description = t.Description ?? "",
            input_schema = ParseSchema(t.ParametersJsonSchema)
        }).ToArray();

        object body = toolDefs.Length > 0
            ? new { model, max_tokens = 1024, system = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt, messages = msgs, tools = toolDefs }
            : new { model, max_tokens = 1024, system = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt, messages = msgs };

        using var resp = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            return req;
        }, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { return FailTools((int)resp.StatusCode, raw); }

        using var doc = JsonDocument.Parse(raw);
        string? text = null;
        var calls = new List<AiToolCall>();
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var te) ? te.GetString() : null;
                if (type == "text") { text = (text ?? "") + block.GetProperty("text").GetString(); }
                else if (type == "tool_use")
                {
                    var id = block.TryGetProperty("id", out var ide) ? ide.GetString() ?? "" : "";
                    var name = block.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
                    var argsJson = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                    if (!string.IsNullOrWhiteSpace(name)) { calls.Add(new AiToolCall(id, name, argsJson)); }
                }
            }
        }

        var (inTok, outTok) = (0, 0);
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            inTok = u.TryGetProperty("input_tokens", out var p) ? p.GetInt32() : 0;
            outTok = u.TryGetProperty("output_tokens", out var c) ? c.GetInt32() : 0;
        }
        return new AiCompletion(true, text, null, inTok, outTok, calls);
    }

    // Convierte el JSON Schema (texto) en un elemento JSON para incrustarlo en el body. Fallback: objeto vacio.
    private static JsonElement ParseSchema(string schema)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(schema) ? "{}" : schema);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var fallback = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
            return fallback.RootElement.Clone();
        }
    }

    private static AiCompletion FailTools(int status, string raw)
    {
        var snippet = raw.Length > 400 ? raw[..400] : raw;
        return new AiCompletion(false, null, $"El proveedor respondio HTTP {status}. {snippet}", 0, 0, Array.Empty<AiToolCall>());
    }
}
