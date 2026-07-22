using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Barrido de los pasos que esperan a un agente de IA (agentes en nodos, ola 2).
///
/// EL DISPARO ASINCRONO ES ESTE BARRIDO, y la "cola" es la propia tabla del historial: un paso
/// vigente + Pending, cuyo nodo tiene agente y que todavia no fue intentado (AgentAttemptedAt
/// null), ES un trabajo pendiente. Se eligio asi -en vez de una cola en memoria o una tabla de
/// jobs propia- porque:
///   - el motor de flujos no tiene que llamar a nadie ni publicar un evento al activar un nodo: la
///     fila que ya escribe dentro de su transaccion es la senal, de modo que no existe la ventana
///     "commit ok pero el aviso se perdio";
///   - sobrevive a un reinicio del proceso (una cola en memoria como AgentReplyDispatcher perderia
///     los pasos encolados, y aqui el trabajo perdido es un caso de negocio parado);
///   - es idempotente por construccion gracias a AgentAttemptedAt, asi que reintentar el ciclo es
///     seguro y no se paga dos veces por la misma decision.
/// El patron (barrido cross-tenant que devuelve SOLO ids + ejecucion acotada por tenant) es el
/// mismo de IScheduledJobDispatcher / ScheduledJobWorker, reusado tal cual.
/// </summary>
public interface IWorkflowAgentStepDispatcher
{
    /// <summary>
    /// Tenants con al menos un paso esperando a su agente. UNICO punto cross-tenant (IgnoreQueryFilters)
    /// y devuelve solo ids de tenant, ningun dato de negocio.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithPendingAgentStepsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atiende los pasos pendientes del tenant ACTIVO (el llamador ya fijo el tenant ambiente).
    /// Devuelve cuantos pasos se intentaron. Un paso que falla no frena a los demas.
    /// </summary>
    Task<int> RunPendingForTenantAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkflowAgentStepDispatcher : IWorkflowAgentStepDispatcher
{
    /// <summary>
    /// Tope de pasos por tenant y por ciclo. Evita que un tenant con una avalancha de casos acapare
    /// el worker (y su cupo de tokens) en una sola pasada; el resto se toma en el ciclo siguiente.
    /// </summary>
    private const int MaxStepsPerTenantPerCycle = 25;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkflowAgentStepRunner _runner;
    private readonly ILogger<WorkflowAgentStepDispatcher> _logger;

    public WorkflowAgentStepDispatcher(
        IApplicationDbContext db, ITenantContext tenantContext,
        IWorkflowAgentStepRunner runner, ILogger<WorkflowAgentStepDispatcher> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _runner = runner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Guid>> FindTenantsWithPendingAgentStepsAsync(
        CancellationToken cancellationToken = default)
        => await _db.WorkflowStepHistories.IgnoreQueryFilters()
            .Where(s => s.IsCurrent && s.Status == WorkflowStepStatus.Pending && s.AgentAttemptedAt == null)
            // El join con el vinculo nodo-agente lleva TenantId a los dos lados: un paso de un tenant
            // jamas puede emparejar con el agente de otro aunque se ignoren los filtros globales.
            .Join(_db.WorkflowNodeAgents.IgnoreQueryFilters(),
                s => new { s.TenantId, NodeId = s.NodeId },
                a => new { a.TenantId, NodeId = a.NodeId },
                (s, a) => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> RunPendingForTenantAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            // Sin tenant activo no se atiende nada: nunca se corre "sobre todos" por accidente.
            return 0;
        }

        // Consulta bajo el filtro global del tenant activo (sin IgnoreQueryFilters).
        var stepIds = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.IsCurrent && s.Status == WorkflowStepStatus.Pending && s.AgentAttemptedAt == null)
            .Join(_db.WorkflowNodeAgents.AsNoTracking(), s => s.NodeId, a => a.NodeId, (s, a) => s)
            .OrderBy(s => s.CreatedAt)
            .Take(MaxStepsPerTenantPerCycle)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var attended = 0;
        foreach (var stepId in stepIds)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            try
            {
                var outcome = await _runner.RunAsync(stepId, cancellationToken);
                if (outcome is WorkflowAgentStepOutcome.Completed
                    or WorkflowAgentStepOutcome.Proposed
                    or WorkflowAgentStepOutcome.ReturnedToPerson)
                {
                    attended++;
                }
            }
            catch (Exception ex)
            {
                // Un paso que revienta no puede tumbar la atencion de los demas. El paso se queda
                // sin marca de intento y se reintenta en el ciclo siguiente (sigue vivo para una
                // persona mientras tanto, que es la garantia que importa).
                _logger.LogError(ex, "Fallo la atencion del paso {StepId} por su agente de IA.", stepId);
            }
        }
        return attended;
    }
}
