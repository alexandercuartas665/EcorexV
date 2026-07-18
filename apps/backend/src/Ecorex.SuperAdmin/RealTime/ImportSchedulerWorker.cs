using Ecorex.SuperAdmin.Agents;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker de las programaciones de importacion (contenedor de datos). Cada ciclo:
///   1. barre las peticiones al agente que vencieron (ver <see cref="IAgentImportService.SweepAsync"/>);
///   2. dispara las programaciones vencidas de cada tenant, llamando al MISMO `IProcessRunner` que el
///      boton "Actualizar datos".
///
/// El barrido va PRIMERO y aparte: es lo que cierra las corridas que quedaron colgadas porque el
/// agente se cayo a mitad. Si dependiera de que haya programaciones vencidas, una corrida manual
/// colgada seguiria en "Ejecutando" para siempre.
///
/// Vive DENTRO de Ecorex.SuperAdmin (como ScheduledJobWorker) y NO en Ecorex.Workers a proposito: el
/// compose de produccion solo levanta `ecorex-app` (= SuperAdmin), asi que un hosted service en
/// Ecorex.Workers NUNCA correria en prod.
///
/// Multi-tenancy: el barrido cross-tenant devuelve SOLO ids; cada ejecucion va con
/// <see cref="AmbientTenantContext.Begin"/>, de modo que el query filter de EF aisla al tenant dueno
/// aunque no haya HttpContext.
/// </summary>
public sealed class ImportSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IAgentImportService imports,
    IBrowserRunService browserRuns,
    TimeProvider timeProvider,
    ILogger<ImportSchedulerWorker> logger) : BackgroundService
{
    /// <summary>Cadencia del barrido, y por tanto la granularidad real de los horarios: pedir "cada 10
    /// segundos" no haria que corriera mas seguido (ver ImportRecurrence.MinIntervalMinutes).</summary>
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Motor de importaciones programadas iniciado; barrido cada {Period}.", Period);

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
                // Un ciclo fallido NUNCA debe matar al worker: se registra y se reintenta en el siguiente.
                logger.LogError(ex, "Fallo el ciclo de importaciones programadas; se reintenta en {Period}.", Period);
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
        var nowUtc = timeProvider.GetUtcNow();

        // 1) Cerrar lo que vencio (peticiones colgadas + resultados viejos). Va aparte del disparo.
        await imports.SweepAsync(ct);
        // Y las corridas de flujo de extraccion (Navegador) que colgaron sin respuesta (Ola 3).
        await browserRuns.SweepAsync(ct);

        // 2) Que tenants tienen algo que hacer -vencido o esperando a su agente- (solo ids).
        IReadOnlyList<Guid> tenants;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IImportScheduleDispatcher>();
            tenants = await dispatcher.FindTenantsWithWorkAsync(nowUtc, ct);
        }
        if (tenants.Count == 0) { return; }

        // 3) Disparo ACOTADO a cada tenant (scope propio + tenant ambiente).
        foreach (var tenantId in tenants)
        {
            if (ct.IsCancellationRequested) { break; }
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IImportScheduleDispatcher>();
                    var fired = await dispatcher.RunDueForTenantAsync(nowUtc, ct);
                    if (fired > 0)
                    {
                        logger.LogInformation(
                            "Importaciones programadas: {Fired} disparada(s) en el tenant {TenantId}.",
                            fired, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de un tenant no debe frenar a los demas.
                logger.LogError(ex, "Fallo el disparo de importaciones del tenant {TenantId}.", tenantId);
            }
        }
    }
}
