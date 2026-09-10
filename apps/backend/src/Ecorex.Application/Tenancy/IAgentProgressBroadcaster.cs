namespace Ecorex.Application.Tenancy;

/// <summary>
/// Capa 2 (ADR-0091): transmite EN VIVO el progreso del agente mientras atiende un paso, para que el
/// nodo abierto en el detalle de la tarea muestre su "pensamiento" (narracion de lo que hace, ronda por
/// ronda) y el consumo de tokens creciendo, sin recargar. Es best-effort y decorativo: si nadie escucha
/// o falla, el paso sigue igual. La implementacion real vive en la app host (SignalR sobre TaskHub);
/// procesos sin SignalR (Api, tests) usan el NoOp.
/// </summary>
public interface IAgentProgressBroadcaster
{
    /// <summary>Emite un latido de progreso del agente para un nodo de la tarea. <paramref name="phase"/>
    /// es la linea legible que se muestra (lo que el agente esta haciendo/pensando); <paramref name="tokens"/>
    /// es el total acumulado de tokens hasta ese momento.</summary>
    Task AgentProgressAsync(
        Guid tenantId, Guid taskId, Guid nodeId, string phase, long tokens,
        CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada) para procesos sin SignalR (Api, tests).</summary>
public sealed class NoOpAgentProgressBroadcaster : IAgentProgressBroadcaster
{
    public Task AgentProgressAsync(
        Guid tenantId, Guid taskId, Guid nodeId, string phase, long tokens,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
