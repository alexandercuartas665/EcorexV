using Ecorex.Application.Workflows;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker de los AGENTES DE IA EN NODOS (ola 2): cada ciclo busca los pasos vigentes cuyo nodo
/// tiene agente y que nadie intento todavia, y los atiende en el contexto de su tenant.
///
/// Es el DISPARO ASINCRONO exigido por el diseno: la llamada al proveedor de IA tarda segundos y
/// puede fallar, asi que no puede ocurrir dentro de la transaccion del motor de flujos. El motor
/// solo escribe la fila del paso y termina; este worker la recoge despues, en su propio scope y su
/// propia transaccion corta. Un proveedor caido deja el paso para una persona y jamas revierte el
/// avance del flujo.
///
/// Vive en Ecorex.SuperAdmin y NO en Ecorex.Workers por la misma razon que ScheduledJobWorker: el
/// compose de produccion solo levanta el servicio `ecorex-app` (= SuperAdmin), de modo que un
/// hosted service en Ecorex.Workers nunca correria en prod.
///
/// Multi-tenancy: el barrido cross-tenant devuelve SOLO ids de tenant; cada tenant se atiende con
/// <see cref="AmbientTenantContext.Begin"/>, para que el query filter de EF aisle aunque no haya
/// HttpContext.
/// </summary>
public sealed class WorkflowAgentStepWorker : BackgroundService
{
    /// <summary>
    /// Cadencia del barrido. Medio minuto: un paso de proceso lo espera una persona, no un chat, y
    /// bajarlo mas solo agregaria consultas vacias contra la base.
    /// </summary>
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowAgentStepWorker> _logger;

    public WorkflowAgentStepWorker(IServiceScopeFactory scopeFactory, ILogger<WorkflowAgentStepWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Motor de agentes en nodos iniciado; barrido cada {Period}.", Period);

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
                // Un ciclo fallido NUNCA debe matar al worker: se registra y se reintenta.
                _logger.LogError(ex, "Fallo el ciclo del motor de agentes en nodos; se reintenta en {Period}.", Period);
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
        // 1) Barrido de plataforma: que tenants tienen pasos esperando a su agente (solo ids).
        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowAgentStepDispatcher>();
            tenants = await dispatcher.FindTenantsWithPendingAgentStepsAsync(cancellationToken);
        }
        if (tenants.Count == 0) { return; }

        // 2) Atencion ACOTADA a cada tenant (scope propio + tenant ambiente).
        foreach (var tenantId in tenants)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowAgentStepDispatcher>();
                    var attended = await dispatcher.RunPendingForTenantAsync(cancellationToken);
                    if (attended > 0)
                    {
                        _logger.LogInformation(
                            "Agentes en nodos: {Attended} paso(s) atendido(s) en el tenant {TenantId}.",
                            attended, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de un tenant no debe frenar a los demas.
                _logger.LogError(ex, "Fallo la atencion de pasos por agente del tenant {TenantId}.", tenantId);
            }
        }
    }
}
