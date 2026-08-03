using Ecorex.Application.Gestor;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker del motor de acciones por filtro de contactos (ADR-0056, Fase 2). Cada ciclo barre los
/// tenants con algun <c>ContactWorkflow</c> ACTIVO y dispara sus ventanas vigentes en el contexto de su
/// tenant, via <see cref="IContactWorkflowDispatcher"/>.
///
/// Vive DENTRO de Ecorex.SuperAdmin (como ScheduledJobWorker/ImportSchedulerWorker) y NO en
/// Ecorex.Workers a proposito: el compose de produccion solo levanta el servicio `ecorex-app` (=
/// SuperAdmin), asi que un hosted service en Ecorex.Workers NUNCA correria en prod.
///
/// Multi-tenancy: el barrido cross-tenant devuelve SOLO ids; cada ejecucion va con
/// <see cref="AmbientTenantContext.Begin"/>, de modo que el query filter de EF aisla al tenant dueno
/// aunque no haya HttpContext.
/// </summary>
public sealed class ContactWorkflowWorker : BackgroundService
{
    /// <summary>Cadencia del barrido. Un minuto basta: la unidad minima del prototipo es la hora.</summary>
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContactWorkflowWorker> _logger;

    public ContactWorkflowWorker(
        IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ContactWorkflowWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Motor de acciones por filtro (ADR-0056) iniciado; barrido cada {Period}.", Period);

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
                _logger.LogError(ex, "Fallo el ciclo del motor de acciones; se reintenta en {Period}.", Period);
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

        // 1) Barrido de plataforma: que tenants tienen algun workflow activo (solo ids, ningun dato).
        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IContactWorkflowDispatcher>();
            tenants = await dispatcher.FindTenantsWithActiveWorkflowsAsync(nowUtc, cancellationToken);
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
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IContactWorkflowDispatcher>();
                    var written = await dispatcher.RunDueForTenantAsync(nowUtc, cancellationToken);
                    if (written > 0)
                    {
                        _logger.LogInformation(
                            "Motor de acciones: {Written} accion(es) ejecutada(s) en el tenant {TenantId}.",
                            written, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de un tenant no debe frenar a los demas.
                _logger.LogError(ex, "Fallo el disparo de acciones del tenant {TenantId}.", tenantId);
            }
        }
    }
}
