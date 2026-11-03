using System.Collections.Concurrent;
using Ecorex.Contracts.Agent;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Canal REQUEST/RESPONSE al sub-agente Archivos sobre el hub. Gemelo de <see cref="IBrowserActionChannel"/>:
/// el hub es push asincrono (se envia un <c>FileRequest</c> por un lado y el <c>FileResult</c> llega por
/// otro, mas tarde), y este canal une las dos mitades por <c>correlationId</c> con un
/// <see cref="TaskCompletionSource{T}"/> para poder AWAITAR el resultado. Lo usa el paso "Procesar archivos"
/// cuando la carpeta de descargas vive en la maquina del agente (Windows) y NO en el servidor (Linux): el
/// servidor lista y lee los ZIP a traves del agente en vez de tocar su propio filesystem.
///
/// Singleton: mantiene las esperas pendientes entre invocaciones del hub. Cada espera tiene su propio
/// timeout; sin respuesta a tiempo, la accion falla (no se cuelga para siempre).
/// </summary>
public interface IFileActionChannel
{
    /// <summary>Empuja la secuencia de acciones de archivos al agente y espera su resultado, o falla por
    /// timeout. La orden DEBE traer su propio correlationId.</summary>
    Task<FileResultMsg> ExecuteFilesAsync(string clientId, FileRequestMsg request, TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Resuelve la espera de un correlationId con el resultado del agente. Lo llama el hub.
    /// Devuelve false si ese correlationId no tenia una espera.</summary>
    bool TryResolve(FileResultMsg msg);
}

public sealed class FileActionChannel(IHubContext<AgenteHub> hub, ILogger<FileActionChannel> log)
    : IFileActionChannel
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FileResultMsg>> _waiters = new();

    public async Task<FileResultMsg> ExecuteFilesAsync(string clientId, FileRequestMsg request, TimeSpan timeout,
        CancellationToken ct = default)
    {
        var corr = request.CorrelationId;
        // RunContinuationsAsynchronously: al resolver desde el hub, la continuacion NO debe correr en el hilo del hub.
        var tcs = new TaskCompletionSource<FileResultMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiters.TryAdd(corr, tcs))
        {
            throw new InvalidOperationException($"Ya hay una orden de archivos en curso con correlationId {corr}.");
        }

        try
        {
            await hub.Clients.Group(AgenteHub.ClientGroup(clientId))
                .SendAsync(AgentHubMethods.FileRequest, request, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, timeoutCts.Token));
            if (completed == tcs.Task)
            {
                timeoutCts.Cancel(); // corta el Delay.
                return await tcs.Task;
            }

            throw new TimeoutException($"El sub-agente Archivos no respondio la orden {corr} en {timeout.TotalSeconds:0} s.");
        }
        finally
        {
            _waiters.TryRemove(corr, out _);
        }
    }

    public bool TryResolve(FileResultMsg msg)
    {
        if (_waiters.TryGetValue(msg.CorrelationId, out var tcs))
        {
            return tcs.TrySetResult(msg);
        }
        log.LogDebug("[ARCHIVOS-CANAL] corr={Corr} sin espera asociada (ignorado)", msg.CorrelationId);
        return false;
    }
}
