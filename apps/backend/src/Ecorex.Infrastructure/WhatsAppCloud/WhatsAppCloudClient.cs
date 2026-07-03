using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ecorex.Application.Tenancy;

namespace Ecorex.Infrastructure.WhatsAppCloud;

/// <summary>
/// Cliente HTTP de la API oficial de WhatsApp Cloud de Meta (graph.facebook.com). El token de acceso va
/// por linea (Authorization: Bearer) y el numero por phone_number_id en la ruta. No maneja instancias ni QR.
/// </summary>
public sealed class WhatsAppCloudClient : IWhatsAppCloudClient
{
    private const string ApiVersion = "v21.0";
    private const string Base = "https://graph.facebook.com/" + ApiVersion;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);
    private readonly HttpClient _http;

    public WhatsAppCloudClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<CloudCheckResult> CheckAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name");
            Auth(req, accessToken);
            using var resp = await SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode) { return new CloudCheckResult(false, null, null, ErrorOf(json, (int)resp.StatusCode)); }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var phone = root.TryGetProperty("display_phone_number", out var p) ? p.GetString() : null;
            var name = root.TryGetProperty("verified_name", out var n) ? n.GetString() : null;
            return new CloudCheckResult(true, phone, name, null);
        }
        catch (Exception ex)
        {
            return new CloudCheckResult(false, null, null, ex.Message);
        }
    }

    public async Task<CloudSendResult> SendTextAsync(string phoneNumberId, string accessToken, string toPhone, string text, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = Digits(toPhone),
            type = "text",
            text = new { preview_url = false, body = text }
        };
        return await PostMessageAsync(phoneNumberId, accessToken, body, cancellationToken);
    }

    public async Task<CloudSendResult> SendMediaAsync(string phoneNumberId, string accessToken, string toPhone, string mediaType, byte[] bytes, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default)
    {
        // 1) Subir el archivo y obtener el media id.
        var (mediaId, upErr) = await UploadMediaAsync(phoneNumberId, accessToken, bytes, mimeType, fileName, cancellationToken);
        if (mediaId is null) { return new CloudSendResult(false, null, upErr ?? "No se pudo subir el archivo a Meta."); }

        // 2) Enviar el mensaje referenciando el media id.
        object mediaObj = mediaType switch
        {
            "image" => new { id = mediaId, caption },
            "video" => new { id = mediaId, caption },
            "document" => new { id = mediaId, caption, filename = fileName ?? "archivo" },
            _ => new { id = mediaId }   // audio: sin caption
        };
        var type = mediaType is "image" or "video" or "document" or "audio" ? mediaType : "document";
        var body = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = Digits(toPhone),
            ["type"] = type,
            [type] = mediaObj
        };
        return await PostMessageAsync(phoneNumberId, accessToken, body, cancellationToken);
    }

    public async Task<CloudSendResult> SendLocationAsync(string phoneNumberId, string accessToken, string toPhone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = Digits(toPhone),
            type = "location",
            location = new { latitude, longitude, name = name ?? "", address = address ?? "" }
        };
        return await PostMessageAsync(phoneNumberId, accessToken, body, cancellationToken);
    }

    // ===== Internos =====

    private async Task<CloudSendResult> PostMessageAsync(string phoneNumberId, string accessToken, object body, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}/messages")
            {
                Content = JsonContent.Create(body)
            };
            Auth(req, accessToken);
            using var resp = await SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) { return new CloudSendResult(false, null, ErrorOf(json, (int)resp.StatusCode)); }
            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array && msgs.GetArrayLength() > 0
                    && msgs[0].TryGetProperty("id", out var mid)) { id = mid.GetString(); }
            }
            catch { /* respuesta sin id: igual fue exitosa */ }
            return new CloudSendResult(true, id, null);
        }
        catch (Exception ex)
        {
            return new CloudSendResult(false, null, ex.Message);
        }
    }

    private async Task<(string? Id, string? Error)> UploadMediaAsync(string phoneNumberId, string accessToken, byte[] bytes, string? mimeType, string? fileName, CancellationToken ct)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("whatsapp"), "messaging_product");
            var fileContent = new ByteArrayContent(bytes);
            var mt = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType!;
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mt);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "archivo" : fileName!);
            form.Add(new StringContent(mt), "type");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}/media") { Content = form };
            Auth(req, accessToken);
            using var resp = await SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) { return (null, ErrorOf(json, (int)resp.StatusCode)); }
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        return await _http.SendAsync(req, cts.Token);
    }

    private static void Auth(HttpRequestMessage req, string token) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string Digits(string phone) => new(phone.Where(char.IsDigit).ToArray());

    // Meta devuelve {"error":{"message":"...","type":"...","code":...}}.
    private static string ErrorOf(string json, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
            {
                return $"HTTP {status}: {m.GetString()}";
            }
        }
        catch { /* no era json */ }
        return $"HTTP {status}: {(json.Length > 160 ? json[..160] : json)}";
    }
}
