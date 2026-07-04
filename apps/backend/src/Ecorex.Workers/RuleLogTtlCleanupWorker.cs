using Ecorex.Application.Rules;

namespace Ecorex.Workers;

/// <summary>
/// Worker de limpieza del TTL del historial de reglas (ADR-0016): una vez al dia borra los
/// RuleExecutionLog con ExpiresAt vencido (TTL 90 dias). Es el UNICO DELETE fisico
/// permitido en el modulo de reglas: el historial es append-only mientras vive.
/// </summary>
public sealed class RuleLogTtlCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RuleLogTtlCleanupWorker> _logger;
    private readonly TimeSpan _interval;

    public RuleLogTtlCleanupWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RuleLogTtlCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var hours = config.GetValue("RuleLogTtlCleanup:IntervalHours", 24);
        _interval = TimeSpan.FromHours(hours < 1 ? 1 : hours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RuleLogTtlCleanupWorker iniciado. Intervalo: {Interval}", _interval);

        // Espera breve inicial para que la app/migraciones esten listas.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<IRuleExecutionLogCleaner>();
                var deleted = await cleaner.CleanupExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("TTL de historial de reglas: {Count} filas expiradas eliminadas.", deleted);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la limpieza TTL del historial de reglas.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
