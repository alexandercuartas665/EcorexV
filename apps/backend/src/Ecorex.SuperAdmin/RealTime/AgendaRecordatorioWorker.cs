using Ecorex.Application.Agendas;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker de RECORDATORIOS de turnos (ADR-0167). Cada minuto barre los tenants con reglas activas y, en el ambito de
/// cada uno (<see cref="AmbientTenantContext.Begin"/>, para que el filtro global de EF aisle al tenant), procesa los
/// recordatorios que ya tocan. Vive dentro de Ecorex.SuperAdmin como los demas workers (prod solo levanta ese
/// servicio). En desarrollo con ECOREX_DISABLE_WORKERS=true no corre: la pantalla tiene "Procesar ahora".
/// </summary>
public sealed class AgendaRecordatorioWorker : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgendaRecordatorioWorker> _logger;

    public AgendaRecordatorioWorker(IServiceScopeFactory scopeFactory, ILogger<AgendaRecordatorioWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        do
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el ciclo de recordatorios de agenda; se reintenta en {Period}.", Period);
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<IAgendaRecordatoriosService>().TenantsConReglasAsync(cancellationToken);
        }
        foreach (var tenantId in tenants)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var corrida = await scope.ServiceProvider.GetRequiredService<IAgendaRecordatoriosService>().ProcesarAsync(cancellationToken);
                    if (corrida.Enviados + corrida.Fallidos + corrida.Omitidos > 0)
                    {
                        _logger.LogInformation("Recordatorios de agenda tenant {Tenant}: {Enviados} enviados, {Fallidos} fallidos, {Omitidos} omitidos.",
                            tenantId, corrida.Enviados, corrida.Fallidos, corrida.Omitidos);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Fallo el procesamiento de recordatorios del tenant {Tenant}.", tenantId);
            }
        }
    }
}
