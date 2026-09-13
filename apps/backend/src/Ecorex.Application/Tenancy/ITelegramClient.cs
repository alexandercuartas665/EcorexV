namespace Ecorex.Application.Tenancy;

/// <summary>Resultado de un envio por Telegram (sin exponer el token).</summary>
public sealed record TelegramSendResult(bool Ok, string? Error);

/// <summary>
/// Cliente del Bot API de Telegram. La implementacion (Infrastructure) hace el POST a
/// https://api.telegram.org/bot&lt;token&gt;/sendMessage. El token lo entrega el llamador por invocacion
/// (no se guarda en el cliente) y nunca se loggea.
/// </summary>
public interface ITelegramClient
{
    Task<TelegramSendResult> SendMessageAsync(string botToken, string chatId, string text, CancellationToken cancellationToken = default);
}
