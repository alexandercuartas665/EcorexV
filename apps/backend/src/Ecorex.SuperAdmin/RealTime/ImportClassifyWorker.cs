using Ecorex.Application.DataContainers;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// ADR-0199: worker del PASO DE CLASIFICACION de las programaciones de importacion. Cada minuto busca las
/// programaciones con una clasificacion pedida (la deja el cierre ok de una corrida o "ejecutar ahora") y las
/// corre con <see cref="IImportClassificationService"/>: lotes sobre las filas con la marca vacia hasta no quedar
/// pendientes, el tope de lotes, el cupo del plan o un error.
///
/// Va APARTE de <see cref="ImportSchedulerWorker"/> a proposito: una clasificacion son muchas llamadas a la IA
/// (minutos) y no debe retrasar los disparos de las importaciones ni bloquear el hub que ingiere los datos.
///
/// Multi-tenancy: el barrido cross-tenant devuelve SOLO ids; cada tenant corre con su scope y
/// <see cref="AmbientTenantContext.Begin"/>, de modo que el filtro global de EF lo aisla.
/// </summary>
public sealed class ImportClassifyWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ImportClassifyWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Motor de clasificacion post-importacion iniciado; barrido cada {Period}.", Period);

        using var timer = new PeriodicTimer(Period);
        while (!stoppingToken.IsCancellationRequested)
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
                // Un ciclo fallido NUNCA debe matar al worker.
                logger.LogError(ex, "Fallo el ciclo de clasificacion post-importacion; se reintenta en {Period}.", Period);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) { break; }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> tenants;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<IImportClassificationService>()
                .FindTenantsWithRequestsAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            if (ct.IsCancellationRequested) { break; }
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var svc = scope.ServiceProvider.GetRequiredService<IImportClassificationService>();
                    var corridas = await svc.RunRequestedForTenantAsync(ct);
                    if (corridas > 0)
                    {
                        logger.LogInformation("Clasificacion post-importacion: {Count} programacion(es) en el tenant {TenantId}.",
                            corridas, tenantId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El fallo de un tenant no debe frenar a los demas.
                logger.LogError(ex, "Fallo la clasificacion post-importacion del tenant {TenantId}.", tenantId);
            }
        }
    }
}
