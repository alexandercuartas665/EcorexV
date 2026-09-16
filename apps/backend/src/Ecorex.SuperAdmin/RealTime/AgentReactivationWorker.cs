using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker de la SECUENCIA DE REACTIVACION del agente: revive contactos dormidos (dejaron de responder sin
/// cerrar). Cada ciclo hace un barrido cross-tenant para saber que tenants tienen algun agente con
/// reactivacion configurada, y ejecuta la pasada de cada uno acotada a su tenant.
///
/// Vive DENTRO de Ecorex.SuperAdmin (como ScheduledJobWorker/AgentReplyDispatcher) y NO en Ecorex.Workers a
/// proposito: el compose de produccion solo levanta `ecorex-app` (= SuperAdmin), asi que un hosted service
/// en Ecorex.Workers NUNCA correria en prod.
///
/// Multi-tenancy: el barrido devuelve SOLO ids de tenant (con IgnoreQueryFilters); la pasada de cada uno se
/// hace con <see cref="AmbientTenantContext.Begin"/>, de modo que el query filter de EF aisla al tenant
/// dueno aunque no haya HttpContext. Respeta la ventana de 24h de Meta (texto vs plantilla) en el servicio.
/// </summary>
public sealed class AgentReactivationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentReactivationWorker> _logger;
    private readonly TimeSpan _period;

    public AgentReactivationWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<AgentReactivationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = config.GetValue("AgentReactivation:IntervalMinutes", 5);
        _period = TimeSpan.FromMinutes(minutes < 1 ? 1 : minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Secuencia de reactivacion iniciada; barrido cada {Period}.", _period);

        // Espera breve inicial para que la app/migraciones esten listas.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_period);
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
                _logger.LogError(ex, "Fallo el ciclo de reactivacion; se reintenta en {Period}.", _period);
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
        // 1) Barrido de plataforma: que tenants tienen algun agente activo con reactivacion configurada.
        List<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            tenants = await db.AiAgents.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.IsActive && a.ReactivacionJson != null)
                .Select(a => a.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        if (tenants.Count == 0) { return; }

        // 2) Pasada ACOTADA a cada tenant (scope propio + tenant ambiente).
        foreach (var tenantId in tenants)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(tenantId))
                {
                    var svc = scope.ServiceProvider.GetRequiredService<IAgentReactivacionService>();
                    var sent = await svc.RunTenantAsync(cancellationToken);
                    if (sent > 0)
                    {
                        _logger.LogInformation("Reactivacion: {Sent} mensaje(s) enviado(s) en el tenant {TenantId}.", sent, tenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de un tenant no debe frenar a los demas.
                _logger.LogError(ex, "Fallo la pasada de reactivacion del tenant {TenantId}.", tenantId);
            }
        }
    }
}
