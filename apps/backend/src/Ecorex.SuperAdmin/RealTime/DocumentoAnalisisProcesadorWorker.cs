using System.Collections.Concurrent;
using Ecorex.Application.DocumentosAnalisis;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Worker del Analizador de documentos: cada ciclo busca los documentos en estado EnAnalisis (encolados por el
/// usuario) y los procesa (rasterizar -> IA de vision -> separar -> guardar) en el contexto de su tenant. El
/// disparo es asincrono por diseno: la llamada a la IA tarda y puede fallar, no puede vivir en la request del UI.
///
/// Procesa VARIOS documentos a la vez (hasta <see cref="MaxConcurrencia"/>), cada uno en su propio scope y su
/// propio tenant ambiente para que el query filter de EF aisle. Un diccionario en memoria evita que un documento
/// ya "en vuelo" se vuelva a tomar en el siguiente ciclo (un solo proceso en prod: el servicio ecorex-app).
///
/// Vive en Ecorex.SuperAdmin (no en Ecorex.Workers) por lo mismo que WorkflowAgentStepWorker: el compose de prod
/// solo levanta ecorex-app (= SuperAdmin).
/// </summary>
public sealed class DocumentoAnalisisProcesadorWorker : BackgroundService
{
    /// <summary>Cadencia del barrido. 10s para que el spinner reaccione pronto sin martillar la base.</summary>
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    /// <summary>Cuantos documentos se procesan a la vez (varios archivos al tiempo, como pidio el usuario).</summary>
    private const int MaxConcurrencia = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentoAnalisisProcesadorWorker> _logger;
    private readonly SemaphoreSlim _gate = new(MaxConcurrencia, MaxConcurrencia);
    private readonly ConcurrentDictionary<Guid, byte> _enVuelo = new();

    public DocumentoAnalisisProcesadorWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentoAnalisisProcesadorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker del Analizador de documentos iniciado; barrido cada {Period}, hasta {Max} en paralelo.",
            Period, MaxConcurrencia);

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
                _logger.LogError(ex, "Fallo el ciclo del worker del Analizador; se reintenta en {Period}.", Period);
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
        // 1) Barrido cross-tenant: documentos encolados (EnAnalisis) de todos los tenants (solo tenant+id).
        List<(Guid Tenant, Guid Doc)> pendientes;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcorexDbContext>();
            var rows = await db.DocumentosAnalisis.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.Estado == DocumentoAnalisisEstado.EnAnalisis && !d.IsDeleted)
                .OrderBy(d => d.CreatedAt)
                .Select(d => new { d.TenantId, d.Id })
                .ToListAsync(ct);
            pendientes = rows.Select(r => (r.TenantId, r.Id)).ToList();
        }

        // 2) Solo los que no estan ya en vuelo (se reclaman en memoria para no tomarlos dos veces).
        var nuevos = pendientes.Where(p => _enVuelo.TryAdd(p.Doc, 0)).ToList();
        if (nuevos.Count == 0) { return; }

        // 3) Procesar en paralelo, acotado por el semaforo; cada uno en su scope + tenant ambiente.
        await Task.WhenAll(nuevos.Select(p => ProcesarUnoAsync(p.Tenant, p.Doc, ct)));
    }

    private async Task ProcesarUnoAsync(Guid tenantId, Guid documentoId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            using (AmbientTenantContext.Begin(tenantId))
            {
                var svc = scope.ServiceProvider.GetRequiredService<IDocumentoAnalisisService>();
                await svc.ProcesarAsync(documentoId, ct);
            }
        }
        catch (Exception ex)
        {
            // El fallo de un documento no debe frenar a los demas (ProcesarAsync ya marca Error por su cuenta).
            _logger.LogError(ex, "Fallo el procesamiento del documento {DocId} (tenant {TenantId}).", documentoId, tenantId);
        }
        finally
        {
            _gate.Release();
            _enVuelo.TryRemove(documentoId, out _);
        }
    }
}
