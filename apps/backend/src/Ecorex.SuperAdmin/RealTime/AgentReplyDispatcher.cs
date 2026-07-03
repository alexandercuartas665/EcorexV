using System.Collections.Concurrent;
using Ecorex.Application.Tenancy;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Despachador en background de las respuestas del agente de IA. Cuando entra un mensaje, la ingesta
/// encola la conversacion (Schedule). Para agrupar las rafagas (el cliente manda varios mensajitos
/// seguidos) aplica un "debounce": espera unos segundos desde el ultimo mensaje antes de atender, y si
/// llega otro mientras tanto, reinicia la espera. Atiende las conversaciones de a una (sin solapes),
/// fijando el TenantId de la linea en un scope aislado para que las herramientas tenant-scoped funcionen.
/// </summary>
public sealed class AgentReplyDispatcher : BackgroundService, IAgentReplyQueue
{
    // Ventana de agrupacion de rafagas y cadencia del bucle.
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AgentReplyDispatcher> _log;
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    private sealed record Pending(Guid TenantId, DateTimeOffset DueAt);

    public AgentReplyDispatcher(IServiceScopeFactory scopes, ILogger<AgentReplyDispatcher> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public void Schedule(Guid tenantId, Guid conversationId)
        => _pending[conversationId] = new Pending(tenantId, DateTimeOffset.UtcNow + Debounce);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var due = _pending.Where(kv => kv.Value.DueAt <= now).Select(kv => kv.Key).ToList();
                foreach (var conversationId in due)
                {
                    if (!_pending.TryRemove(conversationId, out var p)) { continue; }
                    await ProcessAsync(p.TenantId, conversationId, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error en el bucle del despachador de respuestas del agente");
            }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
    {
        // Fijamos el tenant de la linea para toda la cadena async (sin usuario autenticado).
        using var _ = AmbientTenantContext.Begin(tenantId);
        using var scope = _scopes.CreateScope();
        try
        {
            var runner = scope.ServiceProvider.GetRequiredService<IAgentConversationService>();
            await runner.RunAsync(conversationId, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fallo atendiendo la conversacion {ConversationId}", conversationId);
        }
    }
}
