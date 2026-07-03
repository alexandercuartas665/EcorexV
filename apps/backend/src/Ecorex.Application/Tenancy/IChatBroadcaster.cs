namespace Ecorex.Application.Tenancy;

/// <summary>
/// Notifica en tiempo real (SignalR) que se agrego un mensaje a una conversacion, para que los
/// asesores con el chat abierto lo vean "en caliente". La implementacion vive en la app host.
/// </summary>
public interface IChatBroadcaster
{
    Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada) para procesos sin SignalR (Api, tests).</summary>
public sealed class NoOpChatBroadcaster : IChatBroadcaster
{
    public Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
