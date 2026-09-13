using System.Net.Http.Json;
using Ecorex.Application.Tenancy;

namespace Ecorex.Infrastructure.Telegram;

/// <summary>
/// Cliente del Bot API de Telegram. HttpClient inyectado por DI (AddHttpClient). El token del bot viaja en
/// la URL del Bot API (asi lo exige Telegram); NUNCA se loggea. Un unico endpoint: sendMessage.
/// </summary>
public sealed class TelegramBotClient : ITelegramClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient _http;

    public TelegramBotClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TelegramSendResult> SendMessageAsync(string botToken, string chatId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            return new TelegramSendResult(false, "Falta el token del bot o el chat de destino.");
        }
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            var url = $"https://api.telegram.org/bot{botToken.Trim()}/sendMessage";
            var body = new { chat_id = chatId.Trim(), text, disable_web_page_preview = true };
            using var resp = await _http.PostAsJsonAsync(url, body, cts.Token);
            if (resp.IsSuccessStatusCode) { return new TelegramSendResult(true, null); }
            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            return new TelegramSendResult(false, $"HTTP {(int)resp.StatusCode}: {Trim(json)}");
        }
        catch (Exception ex)
        {
            return new TelegramSendResult(false, ex.GetType().Name);
        }
    }

    private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s[..200] : s);
}
