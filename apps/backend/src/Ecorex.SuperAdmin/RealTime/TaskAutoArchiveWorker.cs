using Ecorex.Application.Tenancy;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker de auto-archivado de tareas cerradas (ADR-0123). Una vez al dia archiva las tareas que
/// llevan en una columna de cierre, ya cerradas (Done/Closed), mas dias de los que el tablero
/// permite (<c>TaskBoard.AutoArchiveDoneDays</c>, 0 = nunca). Reloj = <c>TaskItem.ColumnEnteredAt</c>.
///
/// Vive DENTRO de Ecorex.SuperAdmin (como los demas workers) porque el compose de prod solo levanta
/// el servicio <c>ecorex-app</c> (= SuperAdmin); un hosted service en Ecorex.Workers no correria en prod.
///
/// Multi-tenancy: barrido de ids cross-tenant y luego ejecucion ACOTADA a cada tenant con
/// <see cref="AmbientTenantContext.Begin"/>, de modo que el query filter de EF aisla al tenant dueno.
/// </summary>
public sealed class TaskAutoArchiveWorker : BackgroundService
{
    /// <summary>Cadencia del barrido. Diario basta: el plazo minimo util es 1 dia.</summary>
    private static readonly TimeSpan Period = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TaskAutoArchiveWorker> _logger;

    public TaskAutoArchiveWorker(
        IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<TaskAutoArchiveWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Auto-archivado de tareas (ADR-0123) iniciado; barrido cada {Period}.", Period);
        // Espera breve inicial para que la app/migraciones esten listas.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }

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
                _logger.LogError(ex, "Fallo el ciclo de auto-archivado; se reintenta en {Period}.", Period);
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
        var nowUtc = _timeProvider.GetUtcNow();

        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ITaskAutoArchiveService>();
            tenants = await svc.ListTenantIdsAsync(cancellationToken);
        }
        if (tenants.Count == 0) { return; }

        foreach (var tenantId in tenants)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var svc = scope.ServiceProvider.GetRequiredService<ITaskAutoArchiveService>();
                    var archived = await svc.ArchiveDueForTenantAsync(nowUtc, cancellationToken);
                    if (archived > 0)
                    {
                        _logger.LogInformation(
                            "Auto-archivado: {Count} tarea(s) archivada(s) en el tenant {TenantId}.", archived, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el auto-archivado del tenant {TenantId}.", tenantId);
            }
        }
    }
}
