using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Implementacion MOCK de <see cref="IHiveConnection"/> para la Ola A (cascara visual). NO conecta
/// SignalR: solo emite eventos simulados para DEMOSTRAR las transiciones del panal. En la Ola B se
/// reemplaza por el cliente SignalR real SIN tocar la GUI ni el ViewModel (misma interfaz).
///
/// Marcado claramente como MOCK a proposito.
/// </summary>
public sealed class MockHiveConnection : IHiveConnection
{
    private ConnectionState _state = ConnectionState.Offline;

    public ConnectionState State => _state;

    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<HiveRequest>? RequestStarted;
    public event Action<HiveRequestResult>? RequestFinished;

    /// <summary>Stub de "Probar conexion": alterna Offline -> Connecting -> Online (o vuelve a Offline).</summary>
    public async Task<bool> TestConnectionAsync(AgentConfig config, CancellationToken cancellationToken = default)
    {
        if (!config.IsComplete)
        {
            SetState(ConnectionState.Offline);
            return false;
        }
        SetState(ConnectionState.Connecting);
        await Task.Delay(700, cancellationToken);
        var online = _state == ConnectionState.Connecting; // no cancelado
        SetState(online ? ConnectionState.Online : ConnectionState.Offline);
        return online;
    }

    /// <summary>DEMO: fuerza el estado de conexion (para el atajo de demo).</summary>
    public void DemoSetConnection(ConnectionState state) => SetState(state);

    /// <summary>DEMO: simula una peticion entrante para una capacidad (abre un worker efimero).</summary>
    public string DemoStartRequest(SubAgentKind kind, string? detail = null)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        RequestStarted?.Invoke(new HiveRequest(correlationId, kind, detail));
        return correlationId;
    }

    /// <summary>DEMO: termina una peticion (ok o error) -> el worker se apaga/retira.</summary>
    public void DemoFinishRequest(string correlationId, bool ok, string? detail = null)
        => RequestFinished?.Invoke(new HiveRequestResult(correlationId, ok, detail));

    private void SetState(ConnectionState state)
    {
        if (_state == state) { return; }
        _state = state;
        ConnectionChanged?.Invoke(state);
    }
}
