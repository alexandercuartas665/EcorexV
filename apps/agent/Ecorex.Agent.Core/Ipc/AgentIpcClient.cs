using System.IO.Pipes;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// Extremo CLIENTE del canal local (ADR-0039 s3): vive en la colmena WPF, que dejo de ser dueno de
/// nada y pasa a ser la cara del servicio. Se reconecta sola: el servicio puede reiniciarse (o no
/// estar instalado todavia) sin que la colmena se rompa.
/// </summary>
public sealed class AgentIpcClient : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _cts = new();
    private readonly IBrowserSubAgent? _browser;
    private IpcChannel? _channel;

    /// <param name="browser">
    /// Quien atiende las ordenes de Navegador que el servicio delegue. Si es null, la colmena no se
    /// ofrece a servirlo (no tiene escritorio util) y el servicio respondera el NO explicito.
    /// </param>
    public AgentIpcClient(IBrowserSubAgent? browser) => _browser = browser;

    /// <summary>El servicio esta al otro lado. Si es false, la colmena no puede hacer nada util.</summary>
    public bool IsConnected => _channel?.IsConnected == true;

    /// <summary>Motivo del ultimo fallo al hablar con el servicio (null si nunca fallo).</summary>
    public string? LastError { get; private set; }

    public event Action<AgentIpc.StateMsg>? StateChanged;
    public event Action<HiveRequest>? RequestStarted;
    public event Action<HiveRequestResult>? RequestFinished;
    public event Action<AgentIpc.AckMsg>? Acked;
    public event Action<bool>? ConnectivityChanged;

    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", AgentIpc.PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(_cts.Token);

                using var channel = new IpcChannel(pipe);
                _channel = channel;
                ConnectivityChanged?.Invoke(true);

                await channel.SendAsync(new AgentIpc.Envelope(
                    AgentIpc.Kinds.Hello, null, AgentIpc.Serialize(new AgentIpc.HelloMsg(_browser is not null))), _cts.Token);

                while (!_cts.IsCancellationRequested)
                {
                    var msg = await channel.ReceiveAsync(_cts.Token);
                    if (msg is null) { break; } // el servicio se detuvo
                    Dispatch(channel, msg);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // El motivo no se pierde. Sin esto, "el servicio no responde" tapa por igual al
                // servicio apagado y a un ACL mal puesto en el pipe (que, encima, se presenta como
                // TimeoutException y no como acceso denegado).
                LastError = ex.Message;
            }
            finally
            {
                _channel = null;
                ConnectivityChanged?.Invoke(false);
            }

            try { await Task.Delay(RetryDelay, _cts.Token); } catch { break; }
        }
    }

    private void Dispatch(IpcChannel channel, AgentIpc.Envelope msg)
    {
        switch (msg.Kind)
        {
            case AgentIpc.Kinds.State:
                var state = AgentIpc.Deserialize<AgentIpc.StateMsg>(msg.Payload);
                if (state is not null) { StateChanged?.Invoke(state); }
                break;

            case AgentIpc.Kinds.RequestStarted:
                var started = AgentIpc.Deserialize<HiveRequest>(msg.Payload);
                if (started is not null) { RequestStarted?.Invoke(started); }
                break;

            case AgentIpc.Kinds.RequestFinished:
                var finished = AgentIpc.Deserialize<HiveRequestResult>(msg.Payload);
                if (finished is not null) { RequestFinished?.Invoke(finished); }
                break;

            case AgentIpc.Kinds.Ack:
                var ack = AgentIpc.Deserialize<AgentIpc.AckMsg>(msg.Payload);
                if (ack is not null) { Acked?.Invoke(ack); }
                break;

            case AgentIpc.Kinds.BrowserRequest:
                // En su propia tarea: navegar tarda, y bloquear aqui congelaria el bucle de lectura y
                // con el todo el canal (estado, otras ordenes).
                var req = AgentIpc.Deserialize<BrowserRequestMsg>(msg.Payload);
                if (req is not null) { _ = Task.Run(() => ServeBrowserAsync(channel, req)); }
                break;
        }
    }

    private async Task ServeBrowserAsync(IpcChannel channel, BrowserRequestMsg req)
    {
        BrowserResultMsg result;
        try
        {
            result = _browser is null
                ? new BrowserResultMsg(req.CorrelationId, false, [], "Esta colmena no atiende el Navegador.")
                : await _browser.ExecuteAsync(req);
        }
        catch (Exception ex)
        {
            result = new BrowserResultMsg(req.CorrelationId, false, [], ex.Message);
        }

        try
        {
            await channel.SendAsync(new AgentIpc.Envelope(
                AgentIpc.Kinds.BrowserResult, req.CorrelationId, AgentIpc.Serialize(result)), _cts.Token);
        }
        catch { /* el servicio se cayo; su timeout lo resuelve */ }
    }

    public Task SendAsync(string kind, object? payload = null, string? id = null)
    {
        var channel = _channel;
        if (channel is null) { return Task.CompletedTask; }
        return channel.SendAsync(new AgentIpc.Envelope(
            kind, id, payload is null ? null : AgentIpc.Serialize(payload)), _cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _channel?.Dispose();
        _cts.Dispose();
    }
}
