using Ecorex.Application.Tenancy;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Sincroniza el estado de aprobacion de las plantillas HSM sometidas a Meta via YCloud (ADR-0184). Cada
/// ciclo busca los tenants con plantillas en revision (Submitted) y, en el contexto de cada tenant, consulta
/// YCloud (una llamada de listado por linea) para pasarlas a Aprobada / Rechazada (con motivo) / Pausada.
/// Asi una plantilla sometida aparece sola en el paso WhatsApp del gestor de contactos al aprobarse, sin
/// depender del boton "Importar de YCloud".
///
/// Vive en Ecorex.SuperAdmin (como ContactWorkflowWorker) porque el compose de prod solo levanta ese servicio.
/// Meta suele revisar en minutos u horas: 10 minutos es suficiente y no castiga el rate limit de YCloud.
/// </summary>
public sealed class WhatsAppTemplateStatusWorker : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppTemplateStatusWorker> _logger;

    public WhatsAppTemplateStatusWorker(IServiceScopeFactory scopeFactory, ILogger<WhatsAppTemplateStatusWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sincronizador de estado de plantillas WhatsApp (ADR-0184) iniciado; cada {Period}.", Period);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

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
                _logger.LogError(ex, "Fallo el ciclo de sincronizacion de plantillas; se reintenta en {Period}.", Period);
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
        IReadOnlyList<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IWhatsAppTemplateService>();
            tenants = await svc.FindTenantsWithPendingTemplatesAsync(cancellationToken);
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
                    var svc = scope.ServiceProvider.GetRequiredService<IWhatsAppTemplateService>();
                    var result = await svc.SyncPendingAsync(cancellationToken);
                    if (result.Value is { } report)
                    {
                        foreach (var c in report.Changes)
                        {
                            _logger.LogInformation(
                                "Plantilla WhatsApp '{Name}' ({Lang}) del tenant {TenantId}: {From} -> {To}.",
                                c.Name, c.Language, tenantId, c.From, c.To);
                        }
                        foreach (var m in report.Messages)
                        {
                            _logger.LogWarning("Sincronizacion de plantillas del tenant {TenantId}: {Message}", tenantId, m);
                        }
                    }
                    else if (result.Error is not null)
                    {
                        _logger.LogWarning("Sincronizacion de plantillas del tenant {TenantId}: {Error}", tenantId, result.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo la sincronizacion de plantillas del tenant {TenantId}.", tenantId);
            }
        }
    }
}
