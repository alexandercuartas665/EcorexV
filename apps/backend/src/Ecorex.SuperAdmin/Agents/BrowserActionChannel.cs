using System.Collections.Concurrent;
using Ecorex.Contracts.Agent;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Canal REQUEST/RESPONSE al sub-agente Navegador sobre el hub (Ola 4). El hub es push asincrono: se
/// envia un <c>BrowserRequest</c> por un lado y el <c>BrowserResult</c> llega por otro, mas tarde. Este
/// canal une las dos mitades por <c>correlationId</c> con un <see cref="TaskCompletionSource{T}"/>, para
/// poder AWAITAR el resultado de una accion antes de decidir la siguiente. Es lo que hace posible el
/// bucle interactivo del paso de IA (el agente ve el resultado de una tool y elige la proxima) y la
/// ejecucion secuencial de los pasos deterministas (esperar, extraer, y solo entonces avanzar).
///
/// Singleton: mantiene las esperas pendientes entre invocaciones del hub. Cada espera tiene su propio
/// timeout; sin respuesta a tiempo, la accion falla (no se cuelga para siempre) y se le pide al agente
/// que aborte.
/// </summary>
public interface IBrowserActionChannel
{
    /// <summary>Empuja la orden al agente y espera su resultado, o falla por timeout. La orden DEBE
    /// traer su propio correlationId (lo pone quien compila, para poder ligar tambien la ingesta).</summary>
    Task<BrowserResultMsg> ExecuteAsync(string clientId, BrowserRequestMsg request, TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Resuelve la espera de un correlationId con el resultado del agente. Lo llama el hub.
    /// Devuelve false si ese correlationId no tenia una espera (p.ej. es una corrida de otro tipo).</summary>
    bool TryResolve(BrowserResultMsg msg);
}

public sealed class BrowserActionChannel(IHubContext<AgenteHub> hub, ILogger<BrowserActionChannel> log)
    : IBrowserActionChannel
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserResultMsg>> _waiters = new();

    public async Task<BrowserResultMsg> ExecuteAsync(string clientId, BrowserRequestMsg request, TimeSpan timeout,
        CancellationToken ct = default)
    {
        var corr = request.CorrelationId;
        // RunContinuationsAsynchronously: al resolver desde el hub, la continuacion (la ingesta, el
        // siguiente paso) NO debe correr en el hilo del hub.
        var tcs = new TaskCompletionSource<BrowserResultMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiters.TryAdd(corr, tcs))
        {
            throw new InvalidOperationException($"Ya hay una accion en curso con correlationId {corr}.");
        }

        try
        {
            await hub.Clients.Group(AgenteHub.ClientGroup(clientId))
                .SendAsync(AgentHubMethods.BrowserRequest, request, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, timeoutCts.Token));
            if (completed == tcs.Task)
            {
                timeoutCts.Cancel(); // corta el Delay.
                return await tcs.Task;
            }

            // Timeout (o cancelacion externa). Se le pide al agente que aborte esa orden: sin esto
            // seguiria ejecutando acciones cuyo resultado ya nadie espera.
            await PushCancelAsync(clientId, corr, ct.IsCancellationRequested ? "cancelado" : "timeout");
            throw new TimeoutException($"El Navegador no respondio la orden {corr} en {timeout.TotalSeconds:0} s.");
        }
        finally
        {
            _waiters.TryRemove(corr, out _);
        }
    }

    public bool TryResolve(BrowserResultMsg msg)
    {
        if (_waiters.TryGetValue(msg.CorrelationId, out var tcs))
        {
            return tcs.TrySetResult(msg);
        }
        return false;
    }

    private async Task PushCancelAsync(string clientId, string correlationId, string reason)
    {
        try
        {
            await hub.Clients.Group(AgenteHub.ClientGroup(clientId))
                .SendAsync(AgentHubMethods.Cancel, new CancelMsg(correlationId, reason));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[NAV-CANAL] corr={Corr} no se pudo empujar el Cancel", correlationId);
        }
    }
}
