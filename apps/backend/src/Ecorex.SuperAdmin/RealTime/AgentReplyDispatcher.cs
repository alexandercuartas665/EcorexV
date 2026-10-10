using System.Collections.Concurrent;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Despachador en background de las respuestas del agente de IA. Cuando entra un mensaje, la ingesta
/// encola la conversacion (Schedule). Para agrupar las rafagas (el cliente manda varios mensajitos
/// seguidos) aplica un "debounce": espera unos segundos desde el ultimo mensaje antes de atender, y si
/// llega otro mientras tanto, reinicia la espera. Atiende las conversaciones de a una (sin solapes),
/// fijando el TenantId de la linea en un scope aislado para que las herramientas tenant-scoped funcionen.
///
/// Resiliencia (auditoria del "worker que se congela sin dejar rastro"):
///  - REINTENTO ACOTADO: si atender la conversacion falla por algo transitorio (blip de BD/red), se reencola
///    con un retardo, hasta <see cref="MaxAttempts"/> veces, en vez de perder el mensaje a la primera.
///  - VISIBILIDAD: agotados los reintentos, se deja un evento de ERROR en /bitacora-agente (AiAgentRunLog)
///    atribuido al agente ligado a la linea, para que un fallo del despachador NUNCA sea un silencio invisible.
///  LIMITACION conocida: la cola es EN MEMORIA, asi que un reinicio/deploy pierde lo que estuviera pendiente de
///  atender (no persistido). Un barrido de respaldo que reencole conversaciones con un entrante sin responder
///  queda como mejora aparte.
/// </summary>
public sealed class AgentReplyDispatcher : BackgroundService, IAgentReplyQueue
{
    // Ventana de agrupacion de rafagas y cadencia del bucle.
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    // Reintento acotado ante fallos transitorios al atender una conversacion.
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    // Barrido de respaldo: la cola es EN MEMORIA y un reinicio/deploy la pierde. Cada tanto se buscan en la BD
    // las conversaciones con un ENTRANTE sin responder (en lineas con agente conectado) y se reencolan, para que
    // nada quede en silencio por haberse perdido la cola. El primero corre poco despues del arranque (tras deploy).
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromMinutes(2);
    private DateTimeOffset _nextSweepAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AgentReplyDispatcher> _log;
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    private sealed record Pending(Guid TenantId, DateTimeOffset DueAt, int Attempt = 0);

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
                    await ProcessAsync(conversationId, p, stoppingToken);
                }

                // Barrido de respaldo periodico (reencola lo que se haya perdido de la cola en memoria).
                if (now >= _nextSweepAt)
                {
                    _nextSweepAt = now + SweepPeriod;
                    await SweepAsync(stoppingToken);
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

    private async Task ProcessAsync(Guid conversationId, Pending p, CancellationToken ct)
    {
        // Fijamos el tenant de la linea para toda la cadena async (sin usuario autenticado).
        using var _ = AmbientTenantContext.Begin(p.TenantId);
        using var scope = _scopes.CreateScope();
        try
        {
            var runner = scope.ServiceProvider.GetRequiredService<IAgentConversationService>();
            await runner.RunAsync(conversationId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Apagado del servicio: no es un fallo de la conversacion; la cola en memoria se pierde (limitacion).
            return;
        }
        catch (Exception ex)
        {
            var attempt = p.Attempt + 1;
            _log.LogError(ex, "Fallo atendiendo la conversacion {ConversationId} (intento {Attempt}/{Max})",
                conversationId, attempt, MaxAttempts);

            if (attempt < MaxAttempts)
            {
                // C) Reintento acotado: reencola con retardo para absorber blips transitorios (BD/red).
                _pending[conversationId] = p with { Attempt = attempt, DueAt = DateTimeOffset.UtcNow + RetryDelay };
            }
            else
            {
                // B) Visibilidad: agotados los reintentos, deja el fallo VISIBLE en /bitacora-agente.
                await LogFailureToBitacoraAsync(p.TenantId, conversationId, ex);
            }
        }
    }

    /// <summary>Barrido de respaldo: la cola es en memoria y se pierde en un reinicio/deploy. Busca en la BD las
    /// conversaciones con un mensaje ENTRANTE sin responder, ya asentado (fuera del debounce) y no muy viejo, en
    /// lineas con un agente CONECTADO, y las reencola. Es una RED DE SEGURIDAD: RunAsync vuelve a decidir si
    /// corresponde responder (se calla si un paso de flujo o un humano lleva el chat, numero en lista negra, linea
    /// desconectada, etc.), asi que el barrido solo garantiza que la conversacion se ATIENDA, no que se responda.
    /// Patron por-tenant (como el worker del agente de flujo) para que el query filter aisle cada tenant.</summary>
    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var settledBefore = now - TimeSpan.FromSeconds(90); // ya deberia haberse atendido (pasado el debounce)
            var notOlderThan = now - TimeSpan.FromDays(3);        // no resucitar conversaciones viejas

            // 1) Tenants con al menos un agente CONECTADO (cross-tenant: sin filtro de tenant).
            List<Guid> tenants;
            using (var scope0 = _scopes.CreateScope())
            {
                var db0 = scope0.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                tenants = await db0.AiAgentLineBindings.IgnoreQueryFilters().AsNoTracking()
                    .Where(b => b.IsConnected).Select(b => b.TenantId).Distinct().ToListAsync(ct);
            }

            var reencoladas = 0;
            foreach (var tenantId in tenants)
            {
                if (ct.IsCancellationRequested) { break; }
                using var _ = AmbientTenantContext.Begin(tenantId);
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                // Conversaciones de ESTE tenant con el ultimo mensaje ENTRANTE, asentadas, en linea con agente conectado.
                var candidates = await db.Conversations.AsNoTracking()
                    .Where(c => c.WhatsAppLineId != null && c.ArchivedAt == null
                        && c.LastMessageAt != null && c.LastMessageAt < settledBefore && c.LastMessageAt > notOlderThan
                        && db.AiAgentLineBindings.Any(b => b.WhatsAppLineId == c.WhatsAppLineId && b.IsConnected)
                        && db.Messages.Where(m => m.ConversationId == c.Id)
                            .OrderByDescending(m => m.SentAt).Select(m => m.Direction).FirstOrDefault() == MessageDirection.Inbound)
                    .Select(c => c.Id)
                    .Take(200)
                    .ToListAsync(ct);

                foreach (var convId in candidates)
                {
                    if (_pending.ContainsKey(convId)) { continue; } // ya esta en cola (rafaga reciente): no pisar
                    Schedule(tenantId, convId);
                    reencoladas++;
                }
            }

            if (reencoladas > 0)
            {
                _log.LogInformation("Barrido de respaldo del agente: {Count} conversacion(es) con entrante sin responder reencolada(s).", reencoladas);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fallo el barrido de respaldo del despachador de respuestas del agente");
        }
    }

    /// <summary>Deja un evento de ERROR en /bitacora-agente (AiAgentRunLog) atribuido al agente ligado a la linea
    /// de la conversacion, para que un fallo del despachador no quede como un silencio inexplicable. Best-effort,
    /// con scope propio y <see cref="CancellationToken.None"/> para que escriba incluso durante el apagado.</summary>
    private async Task LogFailureToBitacoraAsync(Guid tenantId, Guid conversationId, Exception ex)
    {
        try
        {
            using var _ = AmbientTenantContext.Begin(tenantId);
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var lineId = await db.Conversations.AsNoTracking()
                .Where(c => c.Id == conversationId).Select(c => c.WhatsAppLineId)
                .FirstOrDefaultAsync(CancellationToken.None);
            var agentId = lineId is Guid lid
                ? await db.AiAgentLineBindings.AsNoTracking()
                    .Where(b => b.WhatsAppLineId == lid).Select(b => (Guid?)b.AgentId)
                    .FirstOrDefaultAsync(CancellationToken.None)
                : null;
            if (agentId is not Guid aid) { return; } // sin agente ligado: no hay a quien atribuir el error

            var detail = ex.Message;
            if (detail.Length > 500) { detail = detail[..500]; }
            db.AiAgentRunLogs.Add(new AiAgentRunLog
            {
                TenantId = tenantId,
                ConversationId = conversationId,
                AgentId = aid,
                OccurredAt = DateTimeOffset.UtcNow,
                Kind = AiAgentRunLogKind.Error,
                Title = "El agente no pudo atender la conversacion",
                Content = $"Tras {MaxAttempts} intentos, el despachador no logro procesar la conversacion. Detalle: {detail}"
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception logEx)
        {
            _log.LogError(logEx, "No se pudo registrar en /bitacora-agente el fallo de la conversacion {ConversationId}", conversationId);
        }
    }
}
