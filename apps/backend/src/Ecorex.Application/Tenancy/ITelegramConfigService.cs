namespace Ecorex.Application.Tenancy;

/// <summary>Estado del bot de Telegram del tenant (sin exponer el token).</summary>
public sealed record TelegramConfigDto(bool HasToken, string? BotUsername, bool IsEnabled);

/// <summary>Guarda/lee el bot de Telegram del tenant activo (token cifrado). Un registro por tenant.</summary>
public interface ITelegramConfigService
{
    Task<TelegramConfigDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el bot. Si <paramref name="botToken"/> es null/vacio, se conserva el token existente (para
    /// permitir editar solo la etiqueta/habilitado sin re-teclear el token). Devuelve el estado resultante.
    /// </summary>
    Task<TelegramConfigDto> SaveAsync(string? botToken, string? botUsername, bool isEnabled, Guid actorUserId, CancellationToken cancellationToken = default);
}
