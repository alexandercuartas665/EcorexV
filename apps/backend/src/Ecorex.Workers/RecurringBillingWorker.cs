using Ecorex.Application.Admin;

namespace Ecorex.Workers;

/// <summary>
/// Worker de facturacion recurrente (Super Admin SaaS sec.15): cada intervalo revisa las
/// suscripciones con debito automatico cuyo periodo vencio y las cobra contra su fuente de pago.
/// </summary>
public sealed class RecurringBillingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringBillingWorker> _logger;
    private readonly TimeSpan _interval;

    public RecurringBillingWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RecurringBillingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = config.GetValue("RecurringBilling:IntervalMinutes", 60);
        _interval = TimeSpan.FromMinutes(minutes < 1 ? 1 : minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecurringBillingWorker iniciado. Intervalo: {Interval}", _interval);

        // Espera breve inicial para que la app/migraciones esten listas.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<IRecurringBillingService>();
                var processed = await billing.ChargeDueSubscriptionsAsync(stoppingToken);
                if (processed > 0)
                {
                    _logger.LogInformation("Cobro recurrente: {Count} suscripciones procesadas.", processed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el ciclo de cobro recurrente.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
