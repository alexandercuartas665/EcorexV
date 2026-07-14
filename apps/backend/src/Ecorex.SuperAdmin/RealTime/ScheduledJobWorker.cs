using Ecorex.Application.Scheduling;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker del Motor de programaciones (modulo 000889, ola P2; doc D5). Cada ciclo busca las reglas
/// VENCIDAS de programaciones ACTIVAS y las dispara en el contexto de su tenant.
///
/// Vive DENTRO de Ecorex.SuperAdmin (como AgentReplyDispatcher) y NO en Ecorex.Workers a proposito: el
/// compose de produccion (deploy/docker-prod) solo levanta el servicio `ecorex-app` (= SuperAdmin), asi
/// que un hosted service en Ecorex.Workers NUNCA correria en prod.
///
/// Multi-tenancy: el barrido cross-tenant devuelve SOLO ids de tenant; la ejecucion de cada uno se hace
/// con <see cref="AmbientTenantContext.Begin"/>, de modo que el query filter de EF aisla al tenant dueno
/// aunque no haya HttpContext.
/// </summary>
public sealed class ScheduledJobWorker : BackgroundService
{
    /// <summary>Cadencia del barrido. Un minuto es suficiente: la unidad minima del prototipo es la hora.</summary>
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledJobWorker> _logger;

    public ScheduledJobWorker(
        IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ScheduledJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Motor de programaciones (000889) iniciado; barrido cada {Period}.", Period);

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
                _logger.LogError(ex, "Fallo el ciclo del motor de programaciones; se reintenta en {Period}.", Period);
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

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();

        // 1) Barrido de plataforma: que tenants tienen algo vencido (solo ids, ningun dato de negocio).
        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IScheduledJobDispatcher>();
            tenants = await dispatcher.FindTenantsWithDueRulesAsync(nowUtc, cancellationToken);
        }
        if (tenants.Count == 0) { return; }

        // 2) Ejecucion ACOTADA a cada tenant (scope propio + tenant ambiente).
        foreach (var tenantId in tenants)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IScheduledJobDispatcher>();
                    var fired = await dispatcher.RunDueForTenantAsync(nowUtc, cancellationToken);
                    if (fired > 0)
                    {
                        _logger.LogInformation(
                            "Motor de programaciones: {Fired} ventana(s) disparada(s) en el tenant {TenantId}.",
                            fired, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de un tenant no debe frenar a los demas.
                _logger.LogError(ex, "Fallo el disparo de programaciones del tenant {TenantId}.", tenantId);
            }
        }
    }
}
